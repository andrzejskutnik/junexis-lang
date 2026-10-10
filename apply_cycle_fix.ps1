param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$file = 'compiler\JunexisLang.Compiler.CodeGen\GoCodeGenerator.cs'
$bytes = [System.IO.File]::ReadAllBytes($file)
$text  = [System.Text.Encoding]::UTF8.GetString($bytes)

# Normalize to LF for manipulation
$text = $text -replace "`r`n", "`n"

# ── 1) Insert DetectCycleEdges method before FlattenConnections ────────────
$flattenSig = "    private static IEnumerable<IRPipelineConn> FlattenConnections("
$newMethod = @'
    /// <summary>
    /// Detects back-edges (cycle-closing edges) in the pipeline connection graph.
    /// Returns the set of (source, target) pairs that form back-edges via DFS.
    /// </summary>
    private static HashSet<(string Source, string Target)> DetectCycleEdges(
        IReadOnlyList<IRPipeline> pipelines,
        IEnumerable<string> platformTaskNames)
    {
        var taskSet = new HashSet<string>(platformTaskNames);
        // Build adjacency list from all connections
        var adj = new Dictionary<string, List<string>>();
        foreach (var pipeline in pipelines)
        {
            foreach (var conn in FlattenConnections(pipeline.Connections))
            {
                if (conn is not IRUseConn use || use.TargetTask is null) continue;
                if (!taskSet.Contains(use.SourceTask) || !taskSet.Contains(use.TargetTask)) continue;
                if (!adj.ContainsKey(use.SourceTask)) adj[use.SourceTask] = [];
                if (!adj[use.SourceTask].Contains(use.TargetTask))
                    adj[use.SourceTask].Add(use.TargetTask);
            }
        }

        var backEdges = new HashSet<(string, string)>();
        var color = new Dictionary<string, int>(); // 0=white,1=gray,2=black

        void Dfs(string node)
        {
            color[node] = 1; // gray = in stack
            if (adj.TryGetValue(node, out var neighbors))
            {
                foreach (var next in neighbors)
                {
                    if (!color.TryGetValue(next, out var c) || c == 0)
                    {
                        color[next] = 0;
                        Dfs(next);
                    }
                    else if (c == 1)
                    {
                        // back-edge: next is ancestor in current DFS path
                        backEdges.Add((node, next));
                    }
                }
            }
            color[node] = 2; // black = done
        }

        foreach (var node in taskSet)
        {
            if (!color.TryGetValue(node, out var c) || c == 0)
                Dfs(node);
        }

        return backEdges;
    }

'@
$text = $text -replace [regex]::Escape($flattenSig), ($newMethod + $flattenSig)

# ── 2) Replace the wire-up block: add cycle detection, pre-create queues, bridge goroutines ──
$oldWireup = @'
        int wireIdx = 0;
        foreach (var pipeline in ir.Pipelines)
        {
            foreach (var conn in FlattenConnections(pipeline.Connections))
            {
                if (conn is not IRUseConn use || use.TargetTask is null)
                    continue;
                if (!wireArgs.ContainsKey(use.TargetTask))
                    continue;  // target not on this platform

                // Skip duplicate source→target connections across pipelines
                if (!seenConnections.Add((use.SourceTask, use.TargetTask)))
                    continue;

                string wireVar;

                if (wireArgs.ContainsKey(use.SourceTask))
                {
                    if (taskOutputWire.TryGetValue(use.SourceTask, out var existingWire))
                    {
                        // Fan-out: source already invoked, reuse its output wire
                        wireVar = existingWire;
                    }
                    else
                    {
                        // First time invoking this source task
                        wireVar = $"__wire{wireIdx++}";
                        var srcArgs = string.Join(", ", wireArgs[use.SourceTask]);
                        sb.AppendLine($"\t{wireVar} := {use.SourceTask}({srcArgs})");
                        invokedInWireup.Add(use.SourceTask);
                        taskOutputWire[use.SourceTask] = wireVar;
                    }
                }
                else
                {
                    if (taskOutputWire.TryGetValue(use.SourceTask, out var existingWire))
                    {
                        // Cross-platform source already created, reuse
                        wireVar = existingWire;
                    }
                    else
                    {
                        // Cross-platform source: create placeholder channel
                        wireVar = $"__wire{wireIdx++}";
                        var srcTask = ir.Tasks.FirstOrDefault(t => t.Name == use.SourceTask);
                        var elemType = srcTask?.OutputType switch
                        {
                            IRCommType { Element: var el } => TypeMappings.ToGo(el),
                            IRType t2 => TypeMappings.ToGo(t2),
                            _ => "interface{}"
                        };
                        sb.AppendLine($"\t// {use.SourceTask} runs on another platform — placeholder channel");
                        sb.AppendLine($"\t{wireVar} := make(chan {elemType})");
                        taskOutputWire[use.SourceTask] = wireVar;
                    }
                }
                wireArgs[use.TargetTask].Add(wireVar);
            }
        }
'@

$newWireup = @'
        // ── Cycle detection ─────────────────────────────────────────────────
        var cycleEdges = DetectCycleEdges(ir.Pipelines, tasks.Select(t => t.Name));
        // For each cycle-closing edge (src→tgt), pre-create the input queue for `tgt`
        // so it can be passed as an arg when `tgt` is invoked before `src`.
        // Maps targetTask → (preCreatedWireVar, elementGoType)
        var cyclePreCreated = new Dictionary<string, (string WireVar, string ElemType)>();
        foreach (var (src, tgt) in cycleEdges)
        {
            if (cyclePreCreated.ContainsKey(tgt)) continue;
            var srcTask = tasks.FirstOrDefault(t => t.Name == src) ?? ir.Tasks.FirstOrDefault(t => t.Name == src);
            var elemType2 = srcTask?.OutputType switch
            {
                IRCommType { Element: var el } => TypeMappings.ToGo(el),
                IRType t2 => TypeMappings.ToGo(t2),
                _ => "interface{}"
            };
            var preVar = $"__cycleIn{wireIdx++}";
            sb.AppendLine($"\t// Cycle back-edge {src} → {tgt}: pre-create input queue");
            sb.AppendLine($"\t{preVar} := NewQueue[{elemType2}]()");
            cyclePreCreated[tgt] = (preVar, elemType2);
            // Register as a known wire for the target task
            wireArgs[tgt].Add(preVar);
            // Register as known output wire for the source (will be overwritten when src is actually invoked)
            // We also need to track so bridge goroutine can connect later
        }

        int wireIdx_start = wireIdx; // snapshot after cycle pre-creation
        _ = wireIdx_start;

        // Track which cycle back-edge sources produce an output we need to bridge
        // cycleOutputPending: srcTask → (actualOutputWireVar) — filled during wire-up
        var cycleOutputPending = new Dictionary<string, (string TargetTask, string ElemType)>();
        foreach (var (src, tgt) in cycleEdges)
        {
            if (cyclePreCreated.TryGetValue(tgt, out var info))
                cycleOutputPending[src] = (tgt, info.ElemType);
        }

        foreach (var pipeline in ir.Pipelines)
        {
            foreach (var conn in FlattenConnections(pipeline.Connections))
            {
                if (conn is not IRUseConn use || use.TargetTask is null)
                    continue;
                if (!wireArgs.ContainsKey(use.TargetTask))
                    continue;  // target not on this platform

                // Skip cycle back-edges — already handled via pre-created queue
                if (cycleEdges.Contains((use.SourceTask, use.TargetTask)))
                    continue;

                // Skip duplicate source→target connections across pipelines
                if (!seenConnections.Add((use.SourceTask, use.TargetTask)))
                    continue;

                string wireVar;

                if (wireArgs.ContainsKey(use.SourceTask))
                {
                    if (taskOutputWire.TryGetValue(use.SourceTask, out var existingWire))
                    {
                        // Fan-out: source already invoked, reuse its output wire
                        wireVar = existingWire;
                    }
                    else
                    {
                        // First time invoking this source task
                        wireVar = $"__wire{wireIdx++}";
                        var srcArgs = string.Join(", ", wireArgs[use.SourceTask]);
                        sb.AppendLine($"\t{wireVar} := {use.SourceTask}({srcArgs})");
                        invokedInWireup.Add(use.SourceTask);
                        taskOutputWire[use.SourceTask] = wireVar;
                    }
                }
                else
                {
                    if (taskOutputWire.TryGetValue(use.SourceTask, out var existingWire))
                    {
                        // Cross-platform source already created, reuse
                        wireVar = existingWire;
                    }
                    else
                    {
                        // Cross-platform source: create placeholder channel
                        wireVar = $"__wire{wireIdx++}";
                        var srcTask = ir.Tasks.FirstOrDefault(t => t.Name == use.SourceTask);
                        var elemType = srcTask?.OutputType switch
                        {
                            IRCommType { Element: var el } => TypeMappings.ToGo(el),
                            IRType t2 => TypeMappings.ToGo(t2),
                            _ => "interface{}"
                        };
                        sb.AppendLine($"\t// {use.SourceTask} runs on another platform — placeholder channel");
                        sb.AppendLine($"\t{wireVar} := make(chan {elemType})");
                        taskOutputWire[use.SourceTask] = wireVar;
                    }
                }
                wireArgs[use.TargetTask].Add(wireVar);
            }
        }

        // ── Bridge goroutines for cycle back-edges ──────────────────────────
        // For each cycle source that was invoked, connect its output to the pre-created queue
        foreach (var (srcTask, (tgtTask, elemType)) in cycleOutputPending)
        {
            if (!taskOutputWire.TryGetValue(srcTask, out var outputWire)) continue;
            if (!cyclePreCreated.TryGetValue(tgtTask, out var preInfo)) continue;
            sb.AppendLine($"\t// Bridge: {srcTask} output → {tgtTask} cycle input");
            sb.AppendLine($"\twg.Add(1)");
            sb.AppendLine($"\tgo func() {{");
            sb.AppendLine($"\t\tdefer wg.Done()");
            sb.AppendLine($"\t\tfor {{");
            sb.AppendLine($"\t\t\tr, msgId := {outputWire}.Recv()");
            sb.AppendLine($"\t\t\tif !r.Ok || msgId == nil {{ break }}");
            sb.AppendLine($"\t\t\t{preInfo.WireVar}.Enqueue(r.Value)");
            sb.AppendLine($"\t\t\t{outputWire}.Ack(*msgId)");
            sb.AppendLine($"\t\t}}");
            sb.AppendLine($"\t}}()");
        }
'@

# Escape the old string for regex
$escaped = [regex]::Escape($oldWireup)
if ($text -match $escaped) {
    $text = $text -replace $escaped, $newWireup
    Write-Host "Wire-up block replaced successfully."
} else {
    Write-Error "Could not find the wire-up block to replace. Check line endings or whitespace."
}

# ── Write back with CRLF ───────────────────────────────────────────────────
$text = $text -replace "`n", "`r`n"
$outBytes = [System.Text.Encoding]::UTF8.GetBytes($text)
[System.IO.File]::WriteAllBytes($file, $outBytes)
Write-Host "Done. File written."
