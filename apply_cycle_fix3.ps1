param()
$ErrorActionPreference = 'Stop'

$file = 'compiler\JunexisLang.Compiler.CodeGen\GoCodeGenerator.cs'
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
$list = [System.Collections.Generic.List[string]]::new($lines)

Write-Host "Total lines: $($list.Count)"

# Verify key lines
Write-Host "Line 172 (0-idx 171): $($list[171])"
Write-Host "Line 240 (0-idx 239): $($list[239])"
Write-Host "Line 241 (0-idx 240): $($list[240])"

$wireStart = 171
$wireEnd   = 239

$newWireupLines = [string[]]@(
    '        // -- Cycle detection -------------------------------------------',
    '        var cycleEdges = DetectCycleEdges(ir.Pipelines, tasks.Select(t => t.Name));',
    '        // For each cycle-closing back-edge (src->tgt), pre-create the input queue for tgt',
    '        // so it can be passed as wireArg when tgt is invoked before src output is available.',
    '        var cyclePreCreated = new Dictionary<string, (string WireVar, string ElemType)>();',
    '        int wireIdx = 0;',
    '        foreach (var (src, tgt) in cycleEdges)',
    '        {',
    '            if (cyclePreCreated.ContainsKey(tgt)) continue;',
    '            var srcTaskDef = tasks.FirstOrDefault(t => t.Name == src) ?? ir.Tasks.FirstOrDefault(t => t.Name == src);',
    '            var cycleElemType = srcTaskDef?.OutputType switch',
    '            {',
    '                IRCommType { Element: var el } => TypeMappings.ToGo(el),',
    '                IRType t2 => TypeMappings.ToGo(t2),',
    '                _ => "interface{}"',
    '            };',
    '            var preVar = $"__cycleIn{wireIdx++}";',
    '            sb.AppendLine($"\t// Cycle back-edge {src} -> {tgt}: pre-create input queue");',
    '            sb.AppendLine($"\t{preVar} := NewQueue[{cycleElemType}]()");',
    '            cyclePreCreated[tgt] = (preVar, cycleElemType);',
    '            wireArgs[tgt].Add(preVar);',
    '        }',
    '',
    '        // cycleOutputPending: srcTask -> (targetTask, elemType)',
    '        var cycleOutputPending = new Dictionary<string, (string TargetTask, string ElemType)>();',
    '        foreach (var (src, tgt) in cycleEdges)',
    '            if (cyclePreCreated.TryGetValue(tgt, out var preInfoPending))',
    '                cycleOutputPending[src] = (tgt, preInfoPending.ElemType);',
    '',
    '        foreach (var pipeline in ir.Pipelines)',
    '        {',
    '            foreach (var conn in FlattenConnections(pipeline.Connections))',
    '            {',
    '                if (conn is not IRUseConn use || use.TargetTask is null)',
    '                    continue;',
    '                if (!wireArgs.ContainsKey(use.TargetTask))',
    '                    continue;  // target not on this platform',
    '',
    '                // Skip cycle back-edges -- already handled via pre-created queue',
    '                if (cycleEdges.Contains((use.SourceTask, use.TargetTask)))',
    '                    continue;',
    '',
    '                // Skip duplicate source->target connections across pipelines',
    '                if (!seenConnections.Add((use.SourceTask, use.TargetTask)))',
    '                    continue;',
    '',
    '                string wireVar;',
    '',
    '                if (wireArgs.ContainsKey(use.SourceTask))',
    '                {',
    '                    if (taskOutputWire.TryGetValue(use.SourceTask, out var existingWire))',
    '                    {',
    '                        // Fan-out: source already invoked, reuse its output wire',
    '                        wireVar = existingWire;',
    '                    }',
    '                    else',
    '                    {',
    '                        // First time invoking this source task',
    '                        wireVar = $"__wire{wireIdx++}";',
    '                        var srcArgs = string.Join(", ", wireArgs[use.SourceTask]);',
    '                        sb.AppendLine($"\t{wireVar} := {use.SourceTask}({srcArgs})");',
    '                        invokedInWireup.Add(use.SourceTask);',
    '                        taskOutputWire[use.SourceTask] = wireVar;',
    '                    }',
    '                }',
    '                else',
    '                {',
    '                    if (taskOutputWire.TryGetValue(use.SourceTask, out var existingWire))',
    '                    {',
    '                        // Cross-platform source already created, reuse',
    '                        wireVar = existingWire;',
    '                    }',
    '                    else',
    '                    {',
    '                        // Cross-platform source: create placeholder channel',
    '                        wireVar = $"__wire{wireIdx++}";',
    '                        var srcTask = ir.Tasks.FirstOrDefault(t => t.Name == use.SourceTask);',
    '                        var elemType = srcTask?.OutputType switch',
    '                        {',
    '                            IRCommType { Element: var el } => TypeMappings.ToGo(el),',
    '                            IRType t2 => TypeMappings.ToGo(t2),',
    '                            _ => "interface{}"',
    '                        };',
    '                        sb.AppendLine($"\t// {use.SourceTask} runs on another platform -- placeholder channel");',
    '                        sb.AppendLine($"\t{wireVar} := make(chan {elemType})");',
    '                        taskOutputWire[use.SourceTask] = wireVar;',
    '                    }',
    '                }',
    '                wireArgs[use.TargetTask].Add(wireVar);',
    '            }',
    '        }',
    '',
    '        // -- Bridge goroutines for cycle back-edges ----------------------',
    '        // For each cycle source that was invoked, connect its output to the pre-created queue',
    '        foreach (var (bridgeSrc, (bridgeTgt, bridgeElemType)) in cycleOutputPending)',
    '        {',
    '            _ = bridgeElemType;',
    '            if (!taskOutputWire.TryGetValue(bridgeSrc, out var outputWire)) continue;',
    '            if (!cyclePreCreated.TryGetValue(bridgeTgt, out var preInfo)) continue;',
    '            sb.AppendLine($"\t// Bridge: {bridgeSrc} output -> {bridgeTgt} cycle input");',
    '            sb.AppendLine($"\twg.Add(1)");',
    '            sb.AppendLine($"\tgo func(out *Queue[{preInfo.ElemType}], inp *Queue[{preInfo.ElemType}]) {{");',
    '            sb.AppendLine($"\t\tdefer wg.Done()");',
    '            sb.AppendLine($"\t\tfor {{");',
    '            sb.AppendLine($"\t\t\tr, msgId := out.Recv()");',
    '            sb.AppendLine($"\t\t\tif !r.Ok || msgId == nil {{ break }}");',
    '            sb.AppendLine($"\t\t\tinp.Enqueue(r.Value)");',
    '            sb.AppendLine($"\t\t\tout.Ack(*msgId)");',
    '            sb.AppendLine($"\t\t}}");',
    '            sb.AppendLine($"\t}}({outputWire}, {preInfo.WireVar})");',
    '        }'
)

$removeCount = $wireEnd - $wireStart + 1
$list.RemoveRange($wireStart, $removeCount)
$list.InsertRange($wireStart, $newWireupLines)
Write-Host "Wire-up replaced. Lines now: $($list.Count)"

# Find FlattenConnections
$flatIdx = -1
for ($i = 0; $i -lt $list.Count; $i++) {
    if ($list[$i] -match 'private static IEnumerable<IRPipelineConn> FlattenConnections\(') {
        $flatIdx = $i; break
    }
}
if ($flatIdx -lt 0) { Write-Error "FlattenConnections not found!"; exit 1 }
Write-Host "FlattenConnections at 0-idx $flatIdx"

$detectLines = [string[]]@(
    '    /// <summary>',
    '    /// Detects back-edges (cycle-closing edges) in the pipeline connection graph using DFS.',
    '    /// Returns the set of (Source, Target) pairs that form back-edges.',
    '    /// </summary>',
    '    private static HashSet<(string Source, string Target)> DetectCycleEdges(',
    '        IReadOnlyList<IRPipeline> pipelines,',
    '        IEnumerable<string> platformTaskNames)',
    '    {',
    '        var taskSet = new HashSet<string>(platformTaskNames);',
    '        var adj = new Dictionary<string, List<string>>();',
    '        foreach (var pipeline in pipelines)',
    '        {',
    '            foreach (var conn in FlattenConnections(pipeline.Connections))',
    '            {',
    '                if (conn is not IRUseConn use || use.TargetTask is null) continue;',
    '                if (!taskSet.Contains(use.SourceTask) || !taskSet.Contains(use.TargetTask)) continue;',
    '                if (!adj.ContainsKey(use.SourceTask)) adj[use.SourceTask] = [];',
    '                if (!adj[use.SourceTask].Contains(use.TargetTask))',
    '                    adj[use.SourceTask].Add(use.TargetTask);',
    '            }',
    '        }',
    '',
    '        var backEdges = new HashSet<(string, string)>();',
    '        var color = new Dictionary<string, int>(); // 0=white, 1=gray (in stack), 2=black',
    '',
    '        void Dfs(string node)',
    '        {',
    '            color[node] = 1;',
    '            if (adj.TryGetValue(node, out var neighbors))',
    '            {',
    '                foreach (var next in neighbors)',
    '                {',
    '                    if (!color.TryGetValue(next, out var c) || c == 0)',
    '                    {',
    '                        if (!color.ContainsKey(next)) color[next] = 0;',
    '                        Dfs(next);',
    '                    }',
    '                    else if (c == 1)',
    '                    {',
    '                        backEdges.Add((node, next));',
    '                    }',
    '                }',
    '            }',
    '            color[node] = 2;',
    '        }',
    '',
    '        foreach (var node in taskSet)',
    '        {',
    '            if (!color.TryGetValue(node, out var c) || c == 0)',
    '                Dfs(node);',
    '        }',
    '',
    '        return backEdges;',
    '    }',
    ''
)

$list.InsertRange($flatIdx, $detectLines)
Write-Host "DetectCycleEdges inserted. Final lines: $($list.Count)"

$allText = $list -join "`r`n"
$outBytes = [System.Text.Encoding]::UTF8.GetBytes($allText)
[System.IO.File]::WriteAllBytes($file, $outBytes)
Write-Host "Done."
