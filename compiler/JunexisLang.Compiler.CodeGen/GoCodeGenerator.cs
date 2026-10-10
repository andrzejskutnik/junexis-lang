namespace JunexisLang.Compiler.CodeGen;

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.Core.Platform;
using JunexisLang.Compiler.IR;
using JunexisLang.Compiler.Topology;

/// <summary>
/// Generuje Go dla zadań z adnotacją <c>@server</c>.
/// Wyjście: <c>server/main.go</c> z goroutynami per task.
/// </summary>
public sealed class GoCodeGenerator : ICodeGenerator
{
    /// <inheritdoc/>
    public string Platform => "server";

    /// <inheritdoc/>
    public IReadOnlyList<GeneratedFile> Generate(IRDocument ir, CodeGenOptions options)
    {
        var tasks = ir.Tasks
            .Where(t => t.Platforms.Any(p => p.Target == "server"))
            .ToList();

        if (tasks.Count == 0)
            return [];

        var distributedTopology = DistributedTopologyLowerer.Lower(ir);
        if (!distributedTopology.Success)
            throw new InvalidOperationException("Distributed topology lowering failed before Go code generation.");
        var restBindings = distributedTopology.Plan.ProtocolBindings
            .Where(binding => binding.Protocol == "rest" &&
                tasks.Any(task => task.Name == binding.Task))
            .ToList();
        var anyRestBindings = restBindings.Count > 0;
        var tracePolicy = TraceRuntimeContract.ResolvePolicy(ir.Narratives);
        var tracingEnabled = tracePolicy is not TraceEmissionPolicy.Off;

        var profile = CodeGenPlatformProfiles.Resolve(tasks.First().Platforms.First(p => p.Target == "server"));
        var anyStorageBackedState = tasks.Any(task =>
        {
            var taskProfile = CodeGenPlatformProfiles.Resolve(task.Platforms.First(p => p.Target == "server"));
            return task.States.Count > 0 && taskProfile.SupportsFileIO;
        });
        var anyNetworkQueueBridge = profile.SupportsNetworking && ir.QueueBridges.Count > 0;
        var anyNetworkStreamBridge = profile.SupportsNetworking && ir.StreamBridges.Count > 0;
        var anyNetworkChanBridge = profile.SupportsNetworking && ir.ChanBridges.Count > 0;
        var anyQueueOutput = tasks.Any(t => t.OutputType is IRCommType { Kind: IRCommKind.Queue });
        var anyStreamOutput = tasks.Any(t => t.OutputType is IRCommType { Kind: IRCommKind.Stream });
        var anyQueueParam = tasks.Any(t => t.Params.Any(p => p.Type is IRCommType { Kind: IRCommKind.Queue }));
        var anyRecvAny = tasks.Any(t => TaskBodyContainsCall(t.Body, "recv_any"));
        var anyExplicitSendTarget = tasks.Any(TaskHasExplicitSendTarget);
        var anyTraceSendStatement = tracingEnabled && tasks.Any(TaskHasSendStatement);
        var taskNames = tasks.Select(t => t.Name).ToHashSet();
        var anyCrossPlatformWire = ir.Pipelines
            .SelectMany(p => FlattenConnections(p.Connections))
            .OfType<IRUseConn>()
            .Any(c => c.TargetTask != null && taskNames.Contains(c.TargetTask) && !taskNames.Contains(c.SourceTask));
        var anyTcpIngressWire = ir.Pipelines
            .SelectMany(p => FlattenConnections(p.Connections))
            .OfType<IRUseConn>()
            .Any(use =>
            {
                if (use.TargetTask is null || !taskNames.Contains(use.TargetTask) || taskNames.Contains(use.SourceTask))
                    return false;

                var targetTask = tasks.FirstOrDefault(t => t.Name == use.TargetTask);
                if (targetTask is null)
                    return false;

                var argIndex = ir.Pipelines
                    .SelectMany(p => FlattenConnections(p.Connections))
                    .OfType<IRUseConn>()
                    .Where(c => c.TargetTask == use.TargetTask)
                    .TakeWhile(c => !ReferenceEquals(c, use))
                    .Count();

                var targetParam = argIndex < targetTask.Params.Count ? targetTask.Params[argIndex] : null;
                return targetParam?.Type is not IRCommType { Kind: IRCommKind.Stream };
            });
        var externStubs = CollectExternStubs(ir, tasks);
        var anyUserServerExternBinding = externStubs.Any(stub => stub.BindUserPackage);
        var storageQueryBindings = CollectStorageQueryBindings(ir);
        var usedStorageQueryBindings = storageQueryBindings
            .Where(binding => tasks.Any(task => TaskContainsCall(task, binding.CallableName)))
            .ToList();
        var anyStorageQueryCalls = usedStorageQueryBindings.Count > 0;
        var storageMutationBindings = CollectStorageMutationBindings(ir);
        var usedStorageMutationBindings = storageMutationBindings
            .Where(binding => tasks.Any(task => TaskContainsCall(task, binding.CallableName)))
            .ToList();
        var anyStorageMutationCalls = usedStorageMutationBindings.Count > 0;
        var anyStorageCalls = anyStorageQueryCalls || anyStorageMutationCalls;
        var anyStorageTimestampResult = usedStorageQueryBindings
            .SelectMany(binding => binding.ResultFields)
            .Any(field => StorageTypeWithoutNull(field.Type) is IRNamedType { Name: "Time" });
        var anyStorageJsonType = usedStorageQueryBindings.Any(binding =>
            binding.Query.Params.Any(parameter => StorageTypeWithoutNull(parameter.Type) is IRNamedType { Name: "Json" }) ||
            binding.ResultFields.Any(field => StorageTypeWithoutNull(field.Type) is IRNamedType { Name: "Json" })) ||
            usedStorageMutationBindings.Any(binding =>
                binding.Mutation.Params.Any(parameter => StorageTypeWithoutNull(parameter.Type) is IRNamedType { Name: "Json" }));

        var sb = new StringBuilder();
        sb.AppendLine("// Generated by JunexisLang compiler — @server");
        sb.AppendLine($"// Profile: {profile.Platform}/{profile.Variant} | threading={(profile.SupportsThreading ? "on" : "off")} networking={(profile.SupportsNetworking ? "on" : "off")} fileIO={(profile.SupportsFileIO ? "on" : "off")} memory={CodeGenPlatformProfiles.FormatBytes(profile.MaxMemoryBytes)}");
        sb.AppendLine("package main");
        sb.AppendLine();
        sb.AppendLine("import (");
        sb.AppendLine("\t\"context\"");
        if (anyStorageBackedState || anyNetworkQueueBridge || anyNetworkStreamBridge || anyNetworkChanBridge || anyCrossPlatformWire || anyRestBindings || tracingEnabled || anyStorageJsonType)
            sb.AppendLine("\t\"encoding/json\"");
        if (anyNetworkQueueBridge || anyTcpIngressWire || tracingEnabled)
            sb.AppendLine("\t\"io\"");
        sb.AppendLine("\t\"log\"");
        sb.AppendLine("\t\"os\""); // for os.Signal, os.Stdout
        sb.AppendLine("\t\"os/signal\"");
        sb.AppendLine("\t\"syscall\"");
        if (anyNetworkChanBridge)
        {
            sb.AppendLine("\t\"sync/atomic\"");
            sb.AppendLine("\t\"github.com/gorilla/websocket\"");
            sb.AppendLine("\t\"github.com/shamaton/msgpack/v2\"");
        }
        var anyStringInterpolation = tasks.Any(t => TaskHasStringInterpolation(t));
        if (anyTcpIngressWire)
            sb.AppendLine("\t\"net\"");
        if (profile.SupportsNetworking || anyCrossPlatformWire || anyStringInterpolation || anyNetworkQueueBridge || anyNetworkStreamBridge || anyNetworkChanBridge || anyRestBindings || anyTraceSendStatement)
            sb.AppendLine("\t\"fmt\"");
        if (profile.SupportsNetworking || tracingEnabled)
            sb.AppendLine("\t\"net/http\"");
        if (tracingEnabled)
            sb.AppendLine("\t\"crypto/sha256\"");
        if (tracingEnabled)
            sb.AppendLine("\t\"bytes\"");
        if (anyUserServerExternBinding)
            sb.AppendLine("\tjunexisUser \"junexis/server/user\"");
        if (anyStorageQueryCalls)
            sb.AppendLine("\tjunexisQueries \"junexis/server/junexis/storage/queries\"");
        if (anyStorageMutationCalls)
            sb.AppendLine("\tjunexisMutations \"junexis/server/junexis/storage/mutations\"");
        if (anyStorageCalls)
            sb.AppendLine("\tjunexisStorageRuntime \"junexis/server/junexis/storage/runtime\"");
        sb.AppendLine("\t\"sync\"");
        var anyTimeType = ir.Structs.Any(s => s.Fields.Any(f => f.Type is IRNamedType { Name: "Time" or "Duration" }))
            || tasks.Any(t => t.Params.Any(p => p.Type is IRNamedType { Name: "Time" or "Duration" })
                || t.States.Any(st => st.Type is IRNamedType { Name: "Time" or "Duration" }));
        sb.AppendLine("\t\"flag\"");
        sb.AppendLine("\t\"time\""); // for supervised mode and periodic tasks
        if (profile.SupportsThreading)
            sb.AppendLine("\t\"runtime\"");
        if (anyNetworkQueueBridge || anyQueueOutput || anyQueueParam || anyRestBindings || anyExplicitSendTarget)
            sb.AppendLine("\t\"strconv\"");
        if (anyRecvAny)
            sb.AppendLine("\t\"reflect\"");
        if (anyStorageTimestampResult)
            sb.AppendLine("\t\"strings\"");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("// junexisTaskContext coordinates generated task shutdown with the server lifecycle.");
        sb.AppendLine("var junexisTaskContext = context.Background()");
        if (anyStorageCalls)
            sb.AppendLine("var junexisStorageProvider *junexisStorageRuntime.Provider");
        sb.AppendLine();
        if (anyNetworkQueueBridge || anyNetworkStreamBridge || anyNetworkChanBridge)
        {
            sb.AppendLine($"const junexisWireVersion = \"{WireRuntimeContractCatalog.Json.Version}\"");
            sb.AppendLine();
        }
        EmitPlatformHelpers(sb, profile, anyNetworkQueueBridge, anyNetworkStreamBridge, anyNetworkChanBridge, anyRestBindings);
        if (tracingEnabled)
            EmitTraceHelper(sb);
        if (anyQueueOutput || anyQueueParam || anyNetworkQueueBridge || anyExplicitSendTarget)
            EmitQueueType(sb);
        if (anyStreamOutput || anyExplicitSendTarget)
            EmitStreamType(sb);

        var anyTimeImport = tasks.Any(UsesProducerTemplate) || tasks.Any(t => TaskHasEveryStmt(t)) || anyTimeType;
        if (anyTimeImport)
        {
            sb.AppendLine("// now returns the current time (Junexis built-in).");
            sb.AppendLine("func now() time.Time { return time.Now() }");
            sb.AppendLine("func epoch() time.Time { return time.Unix(0, 0).UTC() }");
            sb.AppendLine();
        }

        if (anyExplicitSendTarget)
        {
            sb.AppendLine("// junexis_send dispatches explicit send targets across chan/queue/stream runtime shapes.");
            sb.AppendLine("func junexis_send[T any](target any, value T) {");
            sb.AppendLine("\tswitch t := target.(type) {");
            sb.AppendLine("\tcase chan T:");
            sb.AppendLine("\t\tt <- value");
            sb.AppendLine("\tcase *Queue[T]:");
            sb.AppendLine("\t\tt.Enqueue(value)");
            sb.AppendLine("\tcase *Stream[T]:");
            sb.AppendLine("\t\tt.Publish(value)");
            sb.AppendLine("\tdefault:");
            sb.AppendLine("\t\tpanic(\"target does not implement the Junexis send contract\")");
            sb.AppendLine("\t}");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (anyRecvAny)
        {
            sb.AppendLine("// recv_any receives from the first ready source (channel or stream).");
            sb.AppendLine("// Uses reflect.Select for channels; polls streams with HasLatest.");
            sb.AppendLine("func recv_any(sources ...any) any {");
            sb.AppendLine("\tfor {");
            sb.AppendLine("\t\tfor _, src := range sources {");
            sb.AppendLine("\t\t\tv := reflect.ValueOf(src)");
            sb.AppendLine("\t\t\tif v.Kind() == reflect.Chan {");
            sb.AppendLine("\t\t\t\tcases := []reflect.SelectCase{{Dir: reflect.SelectRecv, Chan: v},");
            sb.AppendLine("\t\t\t\t\t{Dir: reflect.SelectDefault}}");
            sb.AppendLine("\t\t\t\tchosen, val, ok := reflect.Select(cases)");
            sb.AppendLine("\t\t\t\tif chosen == 0 && ok {");
            sb.AppendLine("\t\t\t\t\treturn val.Interface()");
            sb.AppendLine("\t\t\t\t}");
            sb.AppendLine("\t\t\t\tcontinue");
            sb.AppendLine("\t\t\t}");
            sb.AppendLine("\t\t\t// Stream: check HasLatest via reflection");
            sb.AppendLine("\t\t\tif hl := v.MethodByName(\"HasLatest\"); hl.IsValid() {");
            sb.AppendLine("\t\t\t\tif res := hl.Call(nil); len(res) > 0 && res[0].Bool() {");
            sb.AppendLine("\t\t\t\t\tif lat := v.MethodByName(\"Latest\"); lat.IsValid() {");
            sb.AppendLine("\t\t\t\t\t\tif r2 := lat.Call(nil); len(r2) > 0 {");
            sb.AppendLine("\t\t\t\t\t\t\treturn r2[0].Interface()");
            sb.AppendLine("\t\t\t\t\t\t}");
            sb.AppendLine("\t\t\t\t\t}");
            sb.AppendLine("\t\t\t\t}");
            sb.AppendLine("\t\t\t}");
            sb.AppendLine("\t\t}");
            sb.AppendLine("\t\ttime.Sleep(time.Millisecond)");
            sb.AppendLine("\t}");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.AppendLine();

        foreach (var s in ir.Structs)
            EmitStruct(sb, s);

        foreach (var e in ir.Enums)
            EmitEnum(sb, e);

        if (anyStorageQueryCalls)
            EmitStorageQueryBindings(sb, usedStorageQueryBindings, anyStorageTimestampResult);
        if (anyStorageMutationCalls)
            EmitStorageMutationBindings(sb, usedStorageMutationBindings);

        // Build lookup tables used by EmitTask and EmitStatement (must be before task emission)
        _externStubs = [.. externStubs];
        _structFields = ir.Structs.ToDictionary(
            s => s.Name,
            s => s.Fields.ToDictionary(f => f.Name.ToLower(), f => TypeMappings.ToGo(f.Type)));
        _sourceMap = ir.SourceMap.ToDictionary(entry => entry.Path, entry => entry.Source, StringComparer.Ordinal);
        _currentNarrativeId = tracingEnabled ? ir.Narratives.FirstOrDefault()?.Label : null;
        _storageQueryParams = storageQueryBindings.ToDictionary(
            binding => binding.CallableName,
            binding => (IReadOnlyList<string>)binding.Query.Params.Select(parameter => parameter.Name).ToList(),
            StringComparer.Ordinal);
        _storageMutationParams = storageMutationBindings.ToDictionary(
            binding => binding.CallableName,
            binding => (IReadOnlyList<string>)binding.Mutation.Params.Select(parameter => parameter.Name).ToList(),
            StringComparer.Ordinal);

        for (var taskIndex = 0; taskIndex < tasks.Count; taskIndex++)
            EmitTask(sb, ir, tasks[taskIndex], taskIndex);

        if (anyRestBindings)
            EmitRestProtocolBindings(sb, restBindings, tasks);

        sb.AppendLine("func main() {");
        sb.AppendLine("\tctx, cancel := context.WithCancel(context.Background())");
        sb.AppendLine("\tdefer cancel()");
        sb.AppendLine("\tjunexisTaskContext = ctx");
        if (anyStorageCalls)
        {
            sb.AppendLine("\tstorageProvider, storageErr := junexisStorageRuntime.OpenFromEnvironment(ctx)");
            sb.AppendLine("\tif storageErr != nil { log.Fatalf(\"[storage] open failed: %v; cause=%v\", storageErr, storageErr.Cause) }");
            sb.AppendLine("\tjunexisStorageProvider = storageProvider");
            sb.AppendLine("\tdefer func() {");
            sb.AppendLine("\t\tif closeErr := storageProvider.Close(); closeErr != nil { log.Printf(\"[storage] close failed: %v; cause=%v\", closeErr, closeErr.Cause) }");
            sb.AppendLine("\t}()");
        }
        sb.AppendLine("\t// --server-mode: ephemeral (default) | persistent | supervised");
        sb.AppendLine("\t// --idle-timeout: seconds before supervised mode shuts down (default 300)");
        sb.AppendLine("\t// --http-port: HTTP port for @server protocol/queue/stream/chan bridges (default 8080)");
        sb.AppendLine("\t// --health-port: HTTP port for liveness/readiness probes (default 8081)");
        sb.AppendLine("\t// --tcp-port: base port for @embedded→@server TCP bridge listeners (default 9000)");
        sb.AppendLine("\tserverMode := flag.String(\"server-mode\", \"persistent\", \"lifecycle mode: ephemeral|persistent|supervised\")");
        sb.AppendLine("\tidleTimeout := flag.Int(\"idle-timeout\", 300, \"idle timeout seconds for supervised mode\")");
        sb.AppendLine("\thttpPort := flag.Int(\"http-port\", 8080, \"HTTP port for @server bridge/protocol endpoints\")");
        sb.AppendLine("\thealthPort := flag.Int(\"health-port\", 8081, \"HTTP port for liveness/readiness probes\")");
        sb.AppendLine("\ttcpBasePort := flag.Int(\"tcp-port\", 9000, \"base TCP port for @embedded→@server bridge listeners\")");
        sb.AppendLine("\tflag.Parse()");
        sb.AppendLine("\tlog.Printf(\"[server] starting (mode=%s, http-port=%d, health-port=%d, tcp-base-port=%d)\", *serverMode, *httpPort, *healthPort, *tcpBasePort)");
        if (profile.SupportsThreading)
            sb.AppendLine("\tconfigurePlatformThreading()");

        if (profile.SupportsNetworking)
            sb.AppendLine("\tconfigurePlatformNetworking()");

        if (anyRestBindings)
            sb.AppendLine("\tregisterProtocolRoutes()");

        if (anyNetworkQueueBridge || anyNetworkStreamBridge || anyNetworkChanBridge || anyRestBindings)
            sb.AppendLine("\tstartNetworkBridgeServer(*httpPort)");

        if (profile.SupportsNetworking)
            sb.AppendLine("\tstartHealthServer(*healthPort)");

        if (profile.SupportsFileIO)
            sb.AppendLine("\tconfigurePlatformFileIO()");

        sb.AppendLine("\tvar wg sync.WaitGroup");

        // ── Pipeline wire-up ─────────────────────────────────────────────────
        // Build map: taskName → list of wire variable names for its inputs
        var wireArgs = new Dictionary<string, List<string>>();
        foreach (var t in tasks)
            wireArgs[t.Name] = [];

        // Track which tasks were already invoked (as source) in wire-up
        var invokedInWireup = new HashSet<string>();
        // Track output wire var per task (for fan-out reuse)
        var taskOutputWire = new Dictionary<string, string>();
        // Deduplicate connections (same source→target may appear in multiple pipelines)
        var seenConnections = new HashSet<(string, string)>();

        // -- Cycle detection -------------------------------------------
        var cycleEdges = DetectCycleEdges(ir.Pipelines, tasks.Select(t => t.Name));
        // For each cycle-closing back-edge (src->tgt), pre-create the input queue for tgt
        // so it can be passed as wireArg when tgt is invoked before src output is available.
        var cyclePreCreated = new Dictionary<string, (string WireVar, string ElemType)>();
        int wireIdx = 0;
        foreach (var (src, tgt) in cycleEdges)
        {
            if (cyclePreCreated.ContainsKey(tgt)) continue;
            var srcTaskDef = tasks.FirstOrDefault(t => t.Name == src) ?? ir.Tasks.FirstOrDefault(t => t.Name == src);
            var cycleElemType = srcTaskDef?.OutputType switch
            {
                IRCommType { Element: var el } => TypeMappings.ToGo(el),
                IRType t2 => TypeMappings.ToGo(t2),
                _ => "interface{}"
            };
            var preVar = $"__cycleIn{wireIdx++}";
            sb.AppendLine($"\t// Cycle back-edge {src} -> {tgt}: pre-create input queue");
            sb.AppendLine($"\t{preVar} := NewQueue[{cycleElemType}]()");
            cyclePreCreated[tgt] = (preVar, cycleElemType);
            wireArgs[tgt].Add(preVar);
        }

        // cycleOutputPending: srcTask -> (targetTask, elemType)
        var cycleOutputPending = new Dictionary<string, (string TargetTask, string ElemType)>();
        foreach (var (src, tgt) in cycleEdges)
            if (cyclePreCreated.TryGetValue(tgt, out var preInfoPending))
                cycleOutputPending[src] = (tgt, preInfoPending.ElemType);

        foreach (var pipeline in ir.Pipelines)
        {
            foreach (var conn in FlattenConnections(pipeline.Connections))
            {
                if (conn is not IRUseConn use || use.TargetTask is null)
                    continue;
                if (!wireArgs.ContainsKey(use.TargetTask))
                    continue;  // target not on this platform

                // Skip cycle back-edges -- already handled via pre-created queue
                if (cycleEdges.Contains((use.SourceTask, use.TargetTask)))
                    continue;

                // Skip duplicate source->target connections across pipelines
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
                        // Cross-platform source: create placeholder channel or Stream
                        wireVar = $"__wire{wireIdx++}";
                        var srcTask2 = ir.Tasks.FirstOrDefault(t => t.Name == use.SourceTask);
                        var elemType2b = srcTask2?.OutputType switch
                        {
                            IRCommType { Element: var el } => TypeMappings.ToGo(el),
                            IRType t2b => TypeMappings.ToGo(t2b),
                            _ => "interface{}"
                        };
                        // Check if the target param expects *Stream[T]
                        var tgtTask2 = tasks.FirstOrDefault(t => t.Name == use.TargetTask);
                        var argIndex2 = wireArgs[use.TargetTask].Count;
                        var tgtParam2 = argIndex2 < (tgtTask2?.Params.Count ?? 0) ? tgtTask2!.Params[argIndex2] : null;
                        if (tgtParam2?.Type is IRCommType { Kind: IRCommKind.Stream })
                        {
                            sb.AppendLine($"\t// {use.SourceTask} runs on another platform -- placeholder Stream");
                            sb.AppendLine($"\t{wireVar} := NewStream[{elemType2b}]()");
                        }
                        else
                        {
                            // TCP receiver: embedded -> server transport
                            // Protocol: [4-byte length big-endian][JSON payload][newline]
                            var portOffset = wireIdx; // offset from --tcp-port base
                            sb.AppendLine($"\t// {use.SourceTask} runs on another platform -- TCP receiver (port = --tcp-port + {portOffset})");
                            sb.AppendLine($"\t{wireVar} := make(chan {elemType2b}, 64)");
                            sb.AppendLine($"\tgo func() {{");
                            sb.AppendLine($"\t\tport{portOffset} := fmt.Sprintf(\":%d\", *tcpBasePort+{portOffset})");
                            sb.AppendLine($"\t\tln, err := net.Listen(\"tcp\", port{portOffset})");
                            sb.AppendLine($"\t\tif err != nil {{ log.Fatalf(\"[bridge] listen %s: %v\", port{portOffset}, err) }}");
                            sb.AppendLine($"\t\tlog.Printf(\"[bridge] {use.SourceTask} -> {use.TargetTask}: TCP listener on %s\", port{portOffset})");                            sb.AppendLine($"\t\tfor {{");
                            sb.AppendLine($"\t\t\tconn, err := ln.Accept()");
                            sb.AppendLine($"\t\t\tif err != nil {{ log.Printf(\"[bridge] accept error: %v\", err); continue }}");
                            sb.AppendLine($"\t\t\tgo func(c net.Conn) {{");
                            sb.AppendLine($"\t\t\t\tdefer c.Close()");
                            sb.AppendLine($"\t\t\t\tfor {{");
                            sb.AppendLine($"\t\t\t\t\tvar lenBuf [4]byte");
                            sb.AppendLine($"\t\t\t\t\tif _, err := io.ReadFull(c, lenBuf[:]); err != nil {{ return }}");
                            sb.AppendLine($"\t\t\t\t\tframeLen := int(lenBuf[0])<<24 | int(lenBuf[1])<<16 | int(lenBuf[2])<<8 | int(lenBuf[3])");
                            sb.AppendLine($"\t\t\t\t\tpayload := make([]byte, frameLen)");
                            sb.AppendLine($"\t\t\t\t\tif _, err := io.ReadFull(c, payload); err != nil {{ return }}");
                            sb.AppendLine($"\t\t\t\t\tc.Read(make([]byte, 1)) // consume newline delimiter");
                            sb.AppendLine($"\t\t\t\t\tvar msg {elemType2b}");
                            sb.AppendLine($"\t\t\t\t\tif err := json.Unmarshal(payload, &msg); err != nil {{ log.Printf(\"[bridge] unmarshal: %v\", err); continue }}");
                            sb.AppendLine($"\t\t\t\t\t// Latency measurement: sender embeds _ts_us (micros) in every JSON frame");
                            sb.AppendLine($"\t\t\t\t\tvar _tsFrame struct{{ TsUs uint64 `json:\"_ts_us\"` }}");
                            sb.AppendLine($"\t\t\t\t\tif json.Unmarshal(payload, &_tsFrame) == nil && _tsFrame.TsUs > 0 {{");
                            sb.AppendLine($"\t\t\t\t\t\tnowUs := uint64(time.Now().UnixMicro())");
                            sb.AppendLine($"\t\t\t\t\t\tif nowUs > _tsFrame.TsUs {{");
                            sb.AppendLine($"\t\t\t\t\t\t\tlog.Printf(\"[latency] {use.SourceTask}->{use.TargetTask}: %d us\", nowUs-_tsFrame.TsUs)");
                            sb.AppendLine($"\t\t\t\t\t\t}}");
                            sb.AppendLine($"\t\t\t\t\t}}");
                            sb.AppendLine($"\t\t\t\t\tselect {{");
                            sb.AppendLine($"\t\t\t\t\tcase {wireVar} <- msg:");
                            sb.AppendLine($"\t\t\t\t\tdefault:");
                            sb.AppendLine($"\t\t\t\t\t\tlog.Printf(\"[bridge] BACKPRESSURE: {use.SourceTask}->{use.TargetTask} channel full, blocking sender\")");
                            sb.AppendLine($"\t\t\t\t\t\t{wireVar} <- msg // block until consumer catches up");
                            sb.AppendLine($"\t\t\t\t\t}}");
                            sb.AppendLine($"\t\t\t\t}}");
                            sb.AppendLine($"\t\t\t}}(conn)");
                            sb.AppendLine($"\t\t}}");
                            sb.AppendLine($"\t}}()");
                        }
                        taskOutputWire[use.SourceTask] = wireVar;
                    }
                }
                wireArgs[use.TargetTask].Add(wireVar);
            }
        }

        // -- Bridge goroutines for cycle back-edges ----------------------
        // For each cycle source that was invoked, connect its output to the pre-created queue
        foreach (var (bridgeSrc, (bridgeTgt, bridgeElemType)) in cycleOutputPending)
        {
            _ = bridgeElemType;
            if (!taskOutputWire.TryGetValue(bridgeSrc, out var outputWire)) continue;
            if (!cyclePreCreated.TryGetValue(bridgeTgt, out var preInfo)) continue;
            sb.AppendLine($"\t// Bridge: {bridgeSrc} output -> {bridgeTgt} cycle input");
            sb.AppendLine($"\twg.Add(1)");
            sb.AppendLine($"\tgo func(out *Queue[{preInfo.ElemType}], inp *Queue[{preInfo.ElemType}]) {{");
            sb.AppendLine($"\t\tdefer wg.Done()");
            sb.AppendLine($"\t\tfor {{");
            sb.AppendLine($"\t\t\tr, msgId := out.Recv()");
            sb.AppendLine($"\t\t\tif !r.Ok || msgId == nil {{ break }}");
            sb.AppendLine($"\t\t\tinp.Enqueue(r.Value)");
            sb.AppendLine($"\t\t\tout.Ack(*msgId)");
            sb.AppendLine($"\t\t}}");
            sb.AppendLine($"\t}}({outputWire}, {preInfo.WireVar})");
        }
        var protocolEndpointTasks = restBindings.Select(binding => binding.Task).ToHashSet(StringComparer.Ordinal);
        foreach (var t in tasks)
        {
            if (protocolEndpointTasks.Contains(t.Name))
                continue;
            // Skip tasks already invoked in wire-up that return a channel (they started goroutines)
            if (invokedInWireup.Contains(t.Name) && t.OutputType is not null)
                continue;
            // Skip "function tasks" that have params but no wireArgs (called on demand, not from main)
            if (t.Params.Count > 0 && wireArgs[t.Name].Count == 0)
                continue;

            var args = string.Join(", ", wireArgs[t.Name]);
            sb.AppendLine($"\twg.Add(1)");
            sb.AppendLine($"\tgo func() {{ defer wg.Done(); {t.Name}({args}) }}()");
        }
        sb.AppendLine("\t// Wait for shutdown signal (SIGINT/SIGTERM) or all tasks to finish");
        sb.AppendLine("\tquit := make(chan os.Signal, 1)");
        sb.AppendLine("\tsignal.Notify(quit, os.Interrupt, syscall.SIGTERM)");
        sb.AppendLine("\tswitch *serverMode {");
        sb.AppendLine("\tcase \"persistent\":");
        sb.AppendLine("\t\t<-quit");
        sb.AppendLine("\t\tlog.Printf(\"[server] shutdown signal received (persistent mode)\")");
        sb.AppendLine("\t\tcancel()");
        sb.AppendLine("\tcase \"supervised\":");
        sb.AppendLine("\t\tidleCh := make(chan struct{}, 1)");
        sb.AppendLine("\t\t_ = idleCh // TODO: signal from tasks when idle");
        sb.AppendLine("\t\tselect {");
        sb.AppendLine("\t\tcase <-quit:");
        sb.AppendLine("\t\t\tlog.Printf(\"[server] shutdown signal received (supervised mode)\")");
        sb.AppendLine("\t\tcase <-time.After(time.Duration(*idleTimeout) * time.Second):");
        sb.AppendLine("\t\t\tlog.Printf(\"[server] idle timeout reached (%ds), shutting down\", *idleTimeout)");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t\tcancel()");
        sb.AppendLine("\tdefault: // ephemeral");
        sb.AppendLine("\t\tgo func() {");
        sb.AppendLine("\t\t\t<-quit");
        sb.AppendLine("\t\t\tlog.Printf(\"[server] shutdown signal received (ephemeral mode)\")");
        sb.AppendLine("\t\t\tcancel()");
        sb.AppendLine("\t\t}()");
        sb.AppendLine("\t}");
        sb.AppendLine("\twg.Wait()");
        sb.AppendLine("\tlog.Printf(\"[server] all tasks stopped, exiting\")");
        sb.AppendLine("}");

        // Emit injectable extern boundaries for operations implemented by the host application.
        if (externStubs.Count > 0)
        {
            sb.AppendLine("// ── Extern boundaries ─────────────────────────────────────────────────────────");
            sb.AppendLine("// User-owned extern contracts are bound through package user when available.");
            sb.AppendLine("// Unbound externs remain injectable function variables for application bootstrap or tests.");
            sb.AppendLine();
            if (externStubs.Any(stub => stub.BindUserPackage && stub.ReturnType != ""))
                EmitExternConversionHelpers(sb);
            foreach (var stub in externStubs)
            {
                var paramList = string.Join(", ", stub.Params.Select(p => $"{p.GoName} {p.GoType}"));
                var retDecl = stub.ReturnType == "" ? "" : $" {stub.ReturnType}";
                if (stub.BindUserPackage)
                {
                    var argList = string.Join(", ", stub.Params.Select(p => p.GoName));
                    if (stub.ReturnType == "")
                    {
                        sb.AppendLine($"var {stub.Name} = func({paramList}) {{ junexisUser.{stub.Name}({argList}) }}");
                    }
                    else if (stub.ReturnType.StartsWith("[]", StringComparison.Ordinal))
                    {
                        var elementType = stub.ReturnType[2..];
                        sb.AppendLine($"var {stub.Name} = func({paramList}){retDecl} {{ return __junexisSliceAs[{elementType}](junexisUser.{stub.Name}({argList})) }}");
                    }
                    else
                    {
                        sb.AppendLine($"var {stub.Name} = func({paramList}){retDecl} {{ return __junexisAs[{stub.ReturnType}](junexisUser.{stub.Name}({argList})) }}");
                    }
                }
                else
                {
                    sb.AppendLine($"var {stub.Name} = func({paramList}){retDecl} {{ panic(\"Junexis extern is not bound: {stub.Name}\") }}");
                }
                sb.AppendLine();
            }
        }

        var files = new List<GeneratedFile>
        {
            new GeneratedFile("server", "server/main.go", sb.ToString())
        };
        files.Add(new GeneratedFile("server", "server/go.mod", GenerateGoMod(anyNetworkChanBridge)));
        if (anyRestBindings)
            files.Add(new GeneratedFile("server", "server/openapi.json", GenerateOpenApi(restBindings)));
        if (ExternContractManifestGenerator.Generate(ir, Platform) is { } externManifest)
            files.Add(externManifest);
        if (StorageContractManifestGenerator.Generate(ir, Platform) is { } storageManifest)
            files.Add(storageManifest);
        if (StorageRuntimeContractGenerator.Generate(ir) is { } storageRuntimeContract)
            files.Add(storageRuntimeContract);
        if (StorageRuntimeClientGenerator.Generate(ir) is { } storageRuntimeClient)
            files.Add(storageRuntimeClient);
        if (StorageQueryClientGenerator.Generate(ir) is { } storageQueryClient)
            files.Add(storageQueryClient);
        if (StorageMutationClientGenerator.Generate(ir) is { } storageMutationClient)
            files.Add(storageMutationClient);
        return files;
    }

    // ── Extern stub collection ────────────────────────────────────────────────

    /// <summary>Represents a user-defined function that needs a stub.</summary>
    private sealed record ExternStub(
        string Name,
        IReadOnlyList<(string GoType, string GoName)> Params,
        string ReturnType,
        bool BindUserPackage);  // ReturnType "" = void

    private static void EmitExternConversionHelpers(StringBuilder sb)
    {
        sb.AppendLine("func __junexisAs[T any](value any) T {");
        sb.AppendLine("\tif typed, ok := value.(T); ok { return typed }");
        sb.AppendLine("\tvar zero T");
        sb.AppendLine("\tpayload, err := json.Marshal(value)");
        sb.AppendLine("\tif err != nil { return zero }");
        sb.AppendLine("\tif err := json.Unmarshal(payload, &zero); err != nil { return zero }");
        sb.AppendLine("\treturn zero");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("func __junexisSliceAs[T any](value any) []T {");
        sb.AppendLine("\tswitch typed := value.(type) {");
        sb.AppendLine("\tcase []T:");
        sb.AppendLine("\t\treturn typed");
        sb.AppendLine("\tcase []any:");
        sb.AppendLine("\t\tresult := make([]T, 0, len(typed))");
        sb.AppendLine("\t\tfor _, item := range typed {");
        sb.AppendLine("\t\t\tresult = append(result, __junexisAs[T](item))");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t\treturn result");
        sb.AppendLine("\tdefault:");
        sb.AppendLine("\t\tpayload, err := json.Marshal(value)");
        sb.AppendLine("\t\tif err != nil { return nil }");
        sb.AppendLine("\t\tvar result []T");
        sb.AppendLine("\t\tif err := json.Unmarshal(payload, &result); err != nil { return nil }");
        sb.AppendLine("\t\treturn result");
        sb.AppendLine("\t}");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>Built-in function names that should NOT become stubs.</summary>
    private static readonly HashSet<string> s_goBuiltins = new(StringComparer.Ordinal)
    {
        "now", "epoch", "sleep", "println", "print", "log", "len", "append", "make", "close",
        "recv_any", "__array__", "__json__", "fmt", "cap", "delete", "panic", "recover",
        // Polish domain aliases
        "dlugosc", "dodaj",
        // Numeric type cast built-ins
        "f32", "f64", "i8", "i16", "i32", "i64", "u8", "u16", "u32", "u64",
        "float32", "float64", "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64",
    };

    private static IReadOnlyList<ExternStub> CollectExternStubs(IRDocument ir, IReadOnlyList<IRTask> tasks)
    {
        var knownNames = new HashSet<string>(s_goBuiltins, StringComparer.Ordinal);
        knownNames.UnionWith(tasks.Select(t => t.Name));
        knownNames.UnionWith(ir.Structs.Select(s => s.Name));
        foreach (var e in ir.Enums)
        {
            knownNames.Add(e.Name);
            foreach (var v in e.Variants)
                knownNames.Add($"New{e.Name}{v.Name}");
        }

        var stubs = new Dictionary<string, ExternStub>(StringComparer.Ordinal);

        foreach (var task in tasks)
        {
            // Build initial scope from task params
            var scope = task.Params.ToDictionary(p => p.Name, p => (IRType?)p.Type);
            if (task.States.Count > 0)
                foreach (var s in task.States)
                    scope[s.Name] = s.Type;

            foreach (var state in task.States.Where(state => state.Init is not null))
                CollectFromExprDeep(state.Init!, state.Type, knownNames, stubs, scope, ir, inValuePosition: true);

            if (task.Config is not null)
            {
                foreach (var field in task.Config.Fields.Where(field => field.Default is not null))
                    CollectFromExprDeep(field.Default!, field.Type, knownNames, stubs, scope, ir, inValuePosition: true);
            }

            CollectFromStmtList(task.Body, task, scope, knownNames, stubs, ir);
        }

        var declarations = ir.ExternTasks
            .Where(external => external.Platforms.Any(platform => platform.Target == "server"))
            .ToDictionary(external => external.Name, StringComparer.Ordinal);
        var userServerBindings = ir.ExternContracts
            .Where(contract =>
                (contract.Target is null || contract.Target.Equals("server", StringComparison.OrdinalIgnoreCase)) &&
                (contract.Language is null || contract.Language.Equals("go", StringComparison.OrdinalIgnoreCase)) &&
                contract.UserFile is not null &&
                IsUserServerGoFile(contract.UserFile))
            .SelectMany(contract => contract.Functions.Select(function => $"{contract.Name}_{function.Name}"))
            .ToHashSet(StringComparer.Ordinal);

        return stubs.Keys
            .Where(declarations.ContainsKey)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                var declaration = declarations[name];
                var parameters = declaration.Params
                    .Select(parameter => (GoType: TypeMappings.ToGo(parameter.Type), GoName: parameter.Name))
                    .ToList();
                var returnType = declaration.OutputType is null
                    ? string.Empty
                    : TypeMappings.ToGo(declaration.OutputType);
                return new ExternStub(name, parameters, returnType, userServerBindings.Contains(name));
            })
            .ToList();
    }

    private static bool IsUserServerGoFile(string userFile)
    {
        var normalized = userFile.Replace('\\', '/');
        return normalized.StartsWith("user/server/", StringComparison.OrdinalIgnoreCase) &&
            normalized.EndsWith(".go", StringComparison.OrdinalIgnoreCase);
    }

    private static void CollectFromStmtList(
        IReadOnlyList<IRStmt> stmts,
        IRTask task,
        Dictionary<string, IRType?> scope,
        HashSet<string> knownNames,
        Dictionary<string, ExternStub> stubs,
        IRDocument ir)
    {
        foreach (var stmt in stmts)
            CollectFromStmt(stmt, task, scope, knownNames, stubs, ir);
    }

    private static void CollectFromStmt(
        IRStmt stmt,
        IRTask task,
        Dictionary<string, IRType?> scope,
        HashSet<string> knownNames,
        Dictionary<string, ExternStub> stubs,
        IRDocument ir)
    {
        switch (stmt)
        {
            case IRLetStmt letStmt:
                if (letStmt.Init is IRCallExpr letCall)
                    TryRegisterExtern(letCall, letStmt.Type, knownNames, stubs, scope, ir, inValuePosition: true);
                else
                    CollectFromExprDeep(letStmt.Init, letStmt.Type, knownNames, stubs, scope, ir, inValuePosition: true);
                if (letStmt.Type is not null)
                    scope[letStmt.Name] = letStmt.Type;
                break;

            case IRExprStmt { Expr: IRBinaryExpr { Op: "=", Left: IRIdentExpr { Name: var lhs }, Right: var rhs } }:
                var lhsType = scope.TryGetValue(lhs, out var lt) ? lt : null;
                if (rhs is IRCallExpr assignCall)
                    TryRegisterExtern(assignCall, lhsType, knownNames, stubs, scope, ir, inValuePosition: lhsType != null);
                else
                    CollectFromExprDeep(rhs, null, knownNames, stubs, scope, ir);
                break;

            case IRSendStmt sendStmt:
            {
                // Determine expected return type for direct stub calls in send position
                var sendRetType = task.OutputType switch
                {
                    IRCommType { Element: var el } => el,
                    IRType t => t,
                    _ => null
                };
                // Use CollectFromExprDeep to recurse into nested args (e.g., struct literals with embedded calls)
                // Pass sendRetType as contextType for direct calls so they get typed stubs
                if (sendStmt.Value is IRCallExpr { Callee: IRIdentExpr { Name: var cn } } directCall
                    && (cn.Length == 0 || !char.IsUpper(cn[0]) || directCall.Args.Count == 0 || !directCall.Args.All(a => a.Name is not null)))
                {
                    TryRegisterExtern(directCall, sendRetType, knownNames, stubs, scope, ir, inValuePosition: true);
                    foreach (var a in directCall.Args)
                        CollectFromExprDeep(a.Value, null, knownNames, stubs, scope, ir);
                }
                else
                {
                    CollectFromExprDeep(sendStmt.Value, sendRetType, knownNames, stubs, scope, ir, inValuePosition: true);
                }
                break;
            }

            case IRIfStmt ifStmt:
                // condition context implies bool return type for any call in condition
                CollectFromExprDeep(ifStmt.Condition, new IRNamedType("Bool"), knownNames, stubs, scope, ir);
                CollectFromStmtList(ifStmt.Then, task, scope, knownNames, stubs, ir);
                switch (ifStmt.Else)
                {
                    case IRElseIf elseIf:
                        CollectFromStmt(elseIf.IfStmt, task, scope, knownNames, stubs, ir);
                        break;
                    case IRElseBlock elseBlock:
                        CollectFromStmtList(elseBlock.Body, task, scope, knownNames, stubs, ir);
                        break;
                }
                break;

            case IREveryStmt everyStmt:
                CollectFromStmtList(everyStmt.Body, task, scope, knownNames, stubs, ir);
                break;

            case IRLoopStmt loopStmt:
                CollectFromStmtList(loopStmt.Body, task, scope, knownNames, stubs, ir);
                break;

            case IRForStmt forStmt:
                CollectFromStmtList(forStmt.Body, task, scope, knownNames, stubs, ir);
                break;

            case IRExprStmt exprStmt:
                CollectFromExprDeep(exprStmt.Expr, null, knownNames, stubs, scope, ir);
                break;

            case IRReturnStmt { Value: not null } returnStmt:
                CollectFromExprDeep(returnStmt.Value, task.OutputType, knownNames, stubs, scope, ir, inValuePosition: true);
                break;

            case IRDelayStmt delayStmt:
                CollectFromExprDeep(delayStmt.Duration, new IRNamedType("Duration"), knownNames, stubs, scope, ir, inValuePosition: true);
                break;

            case IRRespondStmt respondStmt:
                CollectFromExprDeep(respondStmt.Value, task.OutputType, knownNames, stubs, scope, ir, inValuePosition: true);
                break;

            case IRRecvStmt recvStmt:
                CollectFromExprDeep(recvStmt.Source, null, knownNames, stubs, scope, ir);
                break;

            case IRMatchStmt matchStmt:
                CollectFromExprDeep(matchStmt.Subject, null, knownNames, stubs, scope, ir);
                foreach (var arm in matchStmt.Arms)
                {
                    CollectFromStmtList(arm.Body, task, scope, knownNames, stubs, ir);
                    if (arm.BodyExpr is not null)
                        CollectFromExprDeep(arm.BodyExpr, task.OutputType, knownNames, stubs, scope, ir, inValuePosition: task.OutputType is not null);
                }
                break;

            case IRLogStmt logStmt:
                foreach (var a in logStmt.Args)
                    CollectFromExprDeep(a, null, knownNames, stubs, scope, ir);
                break;
        }
    }

    private static void CollectFromExprDeep(
        IRExpr expr,
        IRType? contextType,
        HashSet<string> knownNames,
        Dictionary<string, ExternStub> stubs,
        Dictionary<string, IRType?> scope,
        IRDocument ir,
        bool inValuePosition = false)
    {
        switch (expr)
        {
            case IRCallExpr call:
                TryRegisterExtern(call, contextType, knownNames, stubs, scope, ir, inValuePosition: inValuePosition || contextType != null);
                // Also recurse into args; struct literal args are in value position
                bool isStructLiteral = call.Args.Count > 0 && call.Callee is IRIdentExpr { Name: var cN } && cN.Length > 0 && char.IsUpper(cN[0])
                    && call.Args.All(a => a.Name is not null);
                foreach (var a in call.Args)
                    CollectFromExprDeep(a.Value, null, knownNames, stubs, scope, ir, inValuePosition: isStructLiteral);
                break;
            case IRUnaryExpr unary:
                // Unary `!` implies the operand is a bool value
                if (unary.Op == "not" && unary.Operand is IRCallExpr boolCall)
                    TryRegisterExtern(boolCall, new IRNamedType("Bool"), knownNames, stubs, scope, ir, inValuePosition: true);
                else
                    CollectFromExprDeep(unary.Operand, contextType, knownNames, stubs, scope, ir);
                break;
            case IRBinaryExpr binary:
                CollectFromExprDeep(binary.Left, null, knownNames, stubs, scope, ir, inValuePosition: inValuePosition);
                CollectFromExprDeep(binary.Right, null, knownNames, stubs, scope, ir, inValuePosition: inValuePosition);
                break;
            case IRTernaryExpr ternary:
                CollectFromExprDeep(ternary.Cond, null, knownNames, stubs, scope, ir);
                CollectFromExprDeep(ternary.Then, contextType, knownNames, stubs, scope, ir);
                CollectFromExprDeep(ternary.Else, contextType, knownNames, stubs, scope, ir);
                break;
        }
    }

    private static void TryRegisterExtern(
        IRCallExpr call,
        IRType? returnType,
        HashSet<string> knownNames,
        Dictionary<string, ExternStub> stubs,
        Dictionary<string, IRType?> scope,
        IRDocument ir,
        bool inValuePosition = false)
    {
        if (call.Callee is not IRIdentExpr { Name: var name })
            return;
        if (knownNames.Contains(name))
            return;
        // Struct literal form (PascalCase + all named args)
        if (call.Args.Count > 0 && name.Length > 0 && char.IsUpper(name[0]) && call.Args.All(a => a.Name is not null))
            return;

        if (stubs.ContainsKey(name))
            return;

        var @params = call.Args.Select((a, i) =>
        {
            var goType = a.Value is IRIdentExpr { Name: var argName } && scope.TryGetValue(argName, out var t) && t is not null
                ? TypeMappings.ToGo(t)
                : "interface{}";
            var goName = a.Name ?? $"arg{i}";
            return (GoType: goType, GoName: goName);
        }).ToList();

        // Check if the callee has an explicit extern task declaration with a return type
        if (returnType is null && inValuePosition)
        {
            var externDecl = ir.ExternTasks.FirstOrDefault(e => e.Name == name);
            if (externDecl?.OutputType is not null)
                returnType = externDecl.OutputType;
        }
        var retGoType = returnType is not null ? TypeMappings.ToGo(returnType)
                        : inValuePosition ? "interface{}"
                        : "";

        stubs[name] = new ExternStub(name, @params, retGoType, BindUserPackage: false);
    }

    /// <summary>
    /// Emits the _emitTrace Go helper function.
    /// Reads JUNEXIS_TRACE_URL at runtime; no-ops when the var is empty.
    /// Matches TraceCollector.Server IngestEventDto contract with string kind values.
    /// </summary>
    private static void EmitTraceHelper(StringBuilder sb)
    {
        sb.AppendLine("// ── Trace emitter ───────────────────────────────────────────────────────────");
        sb.AppendLine($"// Set {TraceRuntimeContract.TraceUrlEnvironmentVariable}=http://host:8080 to enable causality tracing.");
        sb.AppendLine($"var _traceURL = os.Getenv(\"{TraceRuntimeContract.TraceUrlEnvironmentVariable}\")");
        sb.AppendLine($"var _traceBuildID = os.Getenv(\"{TraceRuntimeContract.BuildIdEnvironmentVariable}\")");
        sb.AppendLine($"var _traceBundleID = os.Getenv(\"{TraceRuntimeContract.BundleIdEnvironmentVariable}\")");
        sb.AppendLine();
        sb.AppendLine("func _junexisPayloadHash(value any) string {");
        sb.AppendLine("\tpayload, err := json.Marshal(value)");
        sb.AppendLine("\tif err != nil { return fmt.Sprintf(\"unavailable:%T\", value) }");
        sb.AppendLine("\tsum := sha256.Sum256(payload)");
        sb.AppendLine("\treturn fmt.Sprintf(\"sha256:%x\", sum)");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("func _emitTrace(kind string, taskId uint32, file string, line int, function string, attributes map[string]string) uint64 {");
        sb.AppendLine("\tif _traceURL == \"\" { return 0 }");
        sb.AppendLine("\tif attributes == nil { attributes = map[string]string{} }");
        sb.AppendLine($"\tif _traceBuildID != \"\" {{ attributes[\"{TraceRuntimeContract.BuildIdAttribute}\"] = _traceBuildID }}");
        sb.AppendLine($"\tif _traceBundleID != \"\" {{ attributes[\"{TraceRuntimeContract.BundleIdAttribute}\"] = _traceBundleID }}");
        sb.AppendLine("\tbody := struct {");
        sb.AppendLine("\t\tTaskId uint32 `json:\"taskId\"`");
        sb.AppendLine("\t\tKind string `json:\"kind\"`");
        sb.AppendLine("\t\tLamportClock int64 `json:\"lamportClock\"`");
        sb.AppendLine("\t\tFile string `json:\"file,omitempty\"`");
        sb.AppendLine("\t\tLine int `json:\"line,omitempty\"`");
        sb.AppendLine("\t\tFunction string `json:\"function,omitempty\"`");
        sb.AppendLine("\t\tAttributes map[string]string `json:\"attributes\"`");
        sb.AppendLine("\t}{TaskId: taskId, Kind: kind, LamportClock: 0, File: file, Line: line, Function: function, Attributes: attributes}");
        sb.AppendLine("\tdata, err := json.Marshal(body)");
        sb.AppendLine("\tif err != nil { return 0 }");
        sb.AppendLine("\tbuf := bytes.NewBuffer(data)");
        sb.AppendLine($"\treq, err := http.NewRequest(\"POST\", _traceURL+\"{TraceRuntimeContract.EventsPath}\", buf)");
        sb.AppendLine("\tif err != nil { return 0 }");
        sb.AppendLine("\treq.Header.Set(\"Content-Type\", \"application/json\")");
        sb.AppendLine("\tclient := http.Client{Timeout: 2 * time.Second}");
        sb.AppendLine("\tresp, err := client.Do(req)");
        sb.AppendLine("\tif err != nil { return 0 }");
        sb.AppendLine("\tdefer resp.Body.Close()");
        sb.AppendLine("\tresponseBody, err := io.ReadAll(resp.Body)");
        sb.AppendLine("\tif err != nil { return 0 }");
        sb.AppendLine("\tif resp.StatusCode < 200 || resp.StatusCode >= 300 { return 0 }");
        sb.AppendLine("\tvar envelope struct {");
        sb.AppendLine("\t\tSampled bool `json:\"sampled\"`");
        sb.AppendLine("\t\tEvent struct { EventId uint64 `json:\"eventId\"` } `json:\"event\"`");
        sb.AppendLine("\t}");
        sb.AppendLine("\tif err := json.Unmarshal(responseBody, &envelope); err != nil { return 0 }");
        sb.AppendLine("\t_debugGate(envelope.Event.EventId)");
        sb.AppendLine("\treturn envelope.Event.EventId");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("func _debugGate(eventId uint64) {");
        sb.AppendLine("\tif _traceURL == \"\" || eventId == 0 { return }");
        sb.AppendLine("\tclient := http.Client{Timeout: 2 * time.Second}");
        sb.AppendLine("\tfor {");
        sb.AppendLine($"\t\treq, err := http.NewRequest(\"GET\", _traceURL+\"{TraceRuntimeContract.DebugSessionPath}\", nil)");
        sb.AppendLine("\t\tif err != nil { return }");
        sb.AppendLine("\t\tresp, err := client.Do(req)");
        sb.AppendLine("\t\tif err != nil { return }");
        sb.AppendLine("\t\tbody, readErr := io.ReadAll(resp.Body)");
        sb.AppendLine("\t\t_ = resp.Body.Close()");
        sb.AppendLine("\t\tif readErr != nil || resp.StatusCode < 200 || resp.StatusCode >= 300 { return }");
        sb.AppendLine("\t\tvar session struct { Status string `json:\"status\"` }");
        sb.AppendLine("\t\tif err := json.Unmarshal(body, &session); err != nil { return }");
        sb.AppendLine("\t\tif session.Status != \"Paused\" && session.Status != \"paused\" { return }");
        sb.AppendLine("\t\ttime.Sleep(250 * time.Millisecond)");
        sb.AppendLine("\t}");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitPlatformHelpers(
        StringBuilder sb,
        PlatformCapabilities profile,
        bool anyNetworkQueueBridge,
        bool anyNetworkStreamBridge,
        bool anyNetworkChanBridge,
        bool anyRestBindings)
    {
        if (profile.SupportsThreading)
        {
            sb.AppendLine("func configurePlatformThreading() {");
            sb.AppendLine("\t// Threading bootstrap enabled by platform profile.");
            sb.AppendLine("\t_ = runtime.NumCPU()");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (profile.SupportsNetworking)
        {
            sb.AppendLine("func configurePlatformNetworking() {");
            sb.AppendLine("\t// Networking bootstrap enabled by platform profile.");
            sb.AppendLine("\t_ = http.MethodGet");
            sb.AppendLine("}");
            sb.AppendLine();

            if (anyNetworkQueueBridge)
            {
                sb.AppendLine("type QueueBridgeEnvelope[T any] struct {");
                sb.AppendLine("\tVersion string `json:\"version\"`");
                sb.AppendLine("\tMessageId string `json:\"messageId\"`");
                sb.AppendLine("\tPayload T `json:\"payload\"`");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("type QueueAckFrame struct {");
                sb.AppendLine("\tKind string `json:\"kind\"`");
                sb.AppendLine("\tMessageId string `json:\"messageId\"`");
                sb.AppendLine("\tReason string `json:\"reason,omitempty\"`");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("func parseQueueAckFrame(data []byte) (QueueAckFrame, error) {");
                sb.AppendLine("\tvar frame QueueAckFrame");
                sb.AppendLine("\terr := json.Unmarshal(data, &frame)");
                sb.AppendLine("\treturn frame, err");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("func encodeQueueBridgeEnvelope[T any](messageId string, payload T) ([]byte, error) {");
                sb.AppendLine("\treturn json.Marshal(QueueBridgeEnvelope[T]{Version: junexisWireVersion, MessageId: messageId, Payload: payload})");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("func applyQueueAckFrame[T any](queue *Queue[T], frame QueueAckFrame) {");
                sb.AppendLine("\tif frame.Kind == \"nack\" {");
                sb.AppendLine("\t\tqueue.Nack(frame.MessageId, frame.Reason)");
                sb.AppendLine("\t\treturn");
                sb.AppendLine("\t}");
                sb.AppendLine("\tqueue.Ack(frame.MessageId)");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("func registerQueueBridgeDataEndpoint[T any](path string, queue *Queue[T]) {");
                sb.AppendLine("\thttp.HandleFunc(path, func(w http.ResponseWriter, r *http.Request) {");
                sb.AppendLine("\t\tif r.Method != http.MethodGet {");
                sb.AppendLine("\t\t\tw.WriteHeader(http.StatusMethodNotAllowed)");
                sb.AppendLine("\t\t\treturn");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\trecvResult, messageId := queue.Recv()");
                sb.AppendLine("\t\tif !recvResult.Ok || messageId == nil {");
                sb.AppendLine("\t\t\tw.WriteHeader(http.StatusNoContent)");
                sb.AppendLine("\t\t\treturn");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tdata, err := encodeQueueBridgeEnvelope(*messageId, recvResult.Value)");
                sb.AppendLine("\t\tif err != nil {");
                sb.AppendLine("\t\t\tw.WriteHeader(http.StatusInternalServerError)");
                sb.AppendLine("\t\t\treturn");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tw.Header().Set(\"Content-Type\", \"application/json\")");
                sb.AppendLine("\t\tw.Header().Set(\"X-Junexis-Wire-Version\", junexisWireVersion)");
                sb.AppendLine("\t\t_, _ = w.Write(data)");
                sb.AppendLine("\t})");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("func registerQueueBridgeAckEndpoint[T any](path string, queue *Queue[T]) {");
                sb.AppendLine("\thttp.HandleFunc(path, func(w http.ResponseWriter, r *http.Request) {");
                sb.AppendLine("\t\tif r.Method != http.MethodPost {");
                sb.AppendLine("\t\t\tw.WriteHeader(http.StatusMethodNotAllowed)");
                sb.AppendLine("\t\t\treturn");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tdefer r.Body.Close()");
                sb.AppendLine("\t\tdata, err := io.ReadAll(r.Body)");
                sb.AppendLine("\t\tif err != nil {");
                sb.AppendLine("\t\t\tw.WriteHeader(http.StatusBadRequest)");
                sb.AppendLine("\t\t\treturn");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tframe, err := parseQueueAckFrame(data)");
                sb.AppendLine("\t\tif err != nil {");
                sb.AppendLine("\t\t\tw.WriteHeader(http.StatusBadRequest)");
                sb.AppendLine("\t\t\treturn");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tapplyQueueAckFrame(queue, frame)");
                sb.AppendLine("\t\tw.WriteHeader(http.StatusNoContent)");
                sb.AppendLine("\t})");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            if (anyNetworkStreamBridge)
            {
                sb.AppendLine("type StreamBridgeEnvelope[T any] struct {");
                sb.AppendLine("\tVersion string `json:\"version\"`");
                sb.AppendLine("\tPayload T `json:\"payload\"`");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("func encodeStreamBridgeEnvelope[T any](payload T) ([]byte, error) {");
                sb.AppendLine("\treturn json.Marshal(StreamBridgeEnvelope[T]{Version: junexisWireVersion, Payload: payload})");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("func registerStreamBridgeDataEndpoint[T any](path string, latest func() (T, bool)) {");
                sb.AppendLine("\thttp.HandleFunc(path, func(w http.ResponseWriter, r *http.Request) {");
                sb.AppendLine("\t\tif r.Method != http.MethodGet {");
                sb.AppendLine("\t\t\tw.WriteHeader(http.StatusMethodNotAllowed)");
                sb.AppendLine("\t\t\treturn");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tpayload, ok := latest()");
                sb.AppendLine("\t\tif !ok {");
                sb.AppendLine("\t\t\tw.WriteHeader(http.StatusNoContent)");
                sb.AppendLine("\t\t\treturn");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tdata, err := encodeStreamBridgeEnvelope(payload)");
                sb.AppendLine("\t\tif err != nil {");
                sb.AppendLine("\t\t\tw.WriteHeader(http.StatusInternalServerError)");
                sb.AppendLine("\t\t\treturn");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tw.Header().Set(\"Content-Type\", \"application/json\")");
                sb.AppendLine("\t\tw.Header().Set(\"X-Junexis-Wire-Version\", junexisWireVersion)");
                sb.AppendLine("\t\t_, _ = w.Write(data)");
                sb.AppendLine("\t})");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            if (anyNetworkChanBridge)
            {
                sb.AppendLine("type ChanBridgeEnvelope[T any] struct {");
                sb.AppendLine("\tPayload T `json:\"payload\"`");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("func encodeChanBridgeEnvelope[T any](payload T) ([]byte, error) {");
                sb.AppendLine("\treturn json.Marshal(ChanBridgeEnvelope[T]{Payload: payload})");
                sb.AppendLine("}");
                sb.AppendLine();
                // WebSocket broadcast hub
                sb.AppendLine("type wsChanHub struct {");
                sb.AppendLine("\tmu      sync.Mutex");
                sb.AppendLine("\tclients map[*websocket.Conn]struct{}");
                sb.AppendLine("\tcount   atomic.Int64");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("func newWsChanHub() *wsChanHub {");
                sb.AppendLine("\treturn &wsChanHub{clients: make(map[*websocket.Conn]struct{})}");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("func (h *wsChanHub) add(conn *websocket.Conn) {");
                sb.AppendLine("\th.mu.Lock(); h.clients[conn] = struct{}{}; h.count.Add(1); h.mu.Unlock()");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("func (h *wsChanHub) remove(conn *websocket.Conn) {");
                sb.AppendLine("\th.mu.Lock(); delete(h.clients, conn); h.count.Add(-1); h.mu.Unlock()");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("func (h *wsChanHub) broadcast(data []byte) {");
                sb.AppendLine("\th.mu.Lock()");
                sb.AppendLine("\tconns := make([]*websocket.Conn, 0, len(h.clients))");
                sb.AppendLine("\tfor conn := range h.clients {");
                sb.AppendLine("\t\tconns = append(conns, conn)");
                sb.AppendLine("\t}");
                sb.AppendLine("\th.mu.Unlock()");
                sb.AppendLine("\tvar wg sync.WaitGroup");
                sb.AppendLine("\tfor _, conn := range conns {");
                sb.AppendLine("\t\twg.Add(1)");
                sb.AppendLine("\t\tgo func(c *websocket.Conn) {");
                sb.AppendLine("\t\t\tdefer wg.Done()");
                sb.AppendLine("\t\t\tif err := c.WriteMessage(websocket.BinaryMessage, data); err != nil {");
                sb.AppendLine("\t\t\t\th.mu.Lock(); delete(h.clients, c); h.mu.Unlock()");
                sb.AppendLine("\t\t\t\t_ = c.Close()");
                sb.AppendLine("\t\t\t}");
                sb.AppendLine("\t\t}(conn)");
                sb.AppendLine("\t}");
                sb.AppendLine("\twg.Wait()");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("var wsUpgrader = websocket.Upgrader{CheckOrigin: func(r *http.Request) bool { return true }}");
                sb.AppendLine();
                sb.AppendLine("// registerChanBridgeWebSocketEndpoint broadcasts MessagePack-encoded messages");
                sb.AppendLine("// from a Go channel to all connected WebSocket clients on path+\"/ws\".");
                sb.AppendLine("func registerChanBridgeWebSocketEndpoint[T any](path string, channel <-chan T) {");
                sb.AppendLine("\thub := newWsChanHub()");
                sb.AppendLine("\tgo func() {");
                sb.AppendLine("\t\tfor msg := range channel {");
                sb.AppendLine("\t\t\tdata, err := msgpack.Marshal(msg)");
                sb.AppendLine("\t\t\tif err != nil {");
                sb.AppendLine("\t\t\t\tlog.Printf(\"[ws] msgpack encode error: %v\", err)");
                sb.AppendLine("\t\t\t\tcontinue");
                sb.AppendLine("\t\t\t}");
                sb.AppendLine("\t\t\thub.broadcast(data)");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t}()");
                sb.AppendLine("\thttp.HandleFunc(path+\"/ws\", func(w http.ResponseWriter, r *http.Request) {");
                sb.AppendLine("\t\tconn, err := wsUpgrader.Upgrade(w, r, nil)");
                sb.AppendLine("\t\tif err != nil {");
                sb.AppendLine("\t\t\tlog.Printf(\"[ws] upgrade error: %v\", err)");
                sb.AppendLine("\t\t\treturn");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\thub.add(conn)");
                sb.AppendLine("\t\tdefer hub.remove(conn)");
                sb.AppendLine("\t\tfor {");
                sb.AppendLine("\t\t\tif _, _, err := conn.ReadMessage(); err != nil { break }");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t})");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            if (anyNetworkQueueBridge || anyNetworkStreamBridge || anyNetworkChanBridge || anyRestBindings)
            {
                sb.AppendLine("func startNetworkBridgeServer(port int) {");
                sb.AppendLine("\tgo func() {");
                sb.AppendLine("\t\taddr := fmt.Sprintf(\":%d\", port)");
                sb.AppendLine("\t\tlog.Printf(\"[server] HTTP bridge listening on %s\", addr)");
                sb.AppendLine("\t\tif err := http.ListenAndServe(addr, nil); err != nil && err != http.ErrServerClosed {");
                sb.AppendLine("\t\t\tlog.Printf(\"network bridge server stopped: %v\", err)");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t}()");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            if (profile.SupportsNetworking)
            {
                sb.AppendLine("// startHealthServer starts /health/live and /health/ready endpoints.");
                sb.AppendLine("func startHealthServer(port int) {");
                sb.AppendLine("\tmux := http.NewServeMux()");
                sb.AppendLine("\tmux.HandleFunc(\"/health/live\", func(w http.ResponseWriter, r *http.Request) {");
                sb.AppendLine("\t\tw.Header().Set(\"Content-Type\", \"application/json\")");
                sb.AppendLine("\t\tw.WriteHeader(http.StatusOK)");
                sb.AppendLine("\t\t_, _ = w.Write([]byte(`{\"status\":\"healthy\",\"probe\":\"liveness\"}`))");
                sb.AppendLine("\t})");
                sb.AppendLine("\tmux.HandleFunc(\"/health/ready\", func(w http.ResponseWriter, r *http.Request) {");
                sb.AppendLine("\t\tw.Header().Set(\"Content-Type\", \"application/json\")");
                sb.AppendLine("\t\tw.WriteHeader(http.StatusOK)");
                sb.AppendLine("\t\t_, _ = w.Write([]byte(`{\"status\":\"ready\",\"probe\":\"readiness\"}`))");
                sb.AppendLine("\t})");
                sb.AppendLine("\tgo func() {");
                sb.AppendLine("\t\taddr := fmt.Sprintf(\":%d\", port)");
                sb.AppendLine("\t\tlog.Printf(\"[health] listening on %s\", addr)");
                sb.AppendLine("\t\tif err := http.ListenAndServe(addr, mux); err != nil && err != http.ErrServerClosed {");
                sb.AppendLine("\t\t\tlog.Printf(\"[health] server stopped: %v\", err)");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t}()");
                sb.AppendLine("}");
                sb.AppendLine();
            }
        }

        if (profile.SupportsFileIO)
        {
            sb.AppendLine("func configurePlatformFileIO() {");
            sb.AppendLine("\t// File I/O bootstrap enabled by platform profile.");
            sb.AppendLine("\t_ = os.Stdout");
            sb.AppendLine("}");
        }
    }

    private static void EmitStruct(StringBuilder sb, IRStruct s)
    {
        sb.AppendLine($"// {s.Name} — JunexisLang struct");
        sb.AppendLine($"type {s.Name} struct {{");
        foreach (var f in s.Fields)
            sb.AppendLine($"\t{PascalCase(f.Name)} {TypeMappings.ToGo(f.Type)} `json:\"{f.Name.ToLower()}\" msgpack:\"{f.Name.ToLower()}\"`");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitEnum(StringBuilder sb, IREnum e)
    {
        sb.AppendLine($"// {e.Name} — JunexisLang enum");
        sb.AppendLine($"type {e.Name} interface {{");
        sb.AppendLine($"\tis{e.Name}()");
        sb.AppendLine("}");
        sb.AppendLine();

        foreach (var variant in e.Variants)
        {
            var variantTypeName = $"{e.Name}{variant.Name}";
            sb.AppendLine($"type {variantTypeName} struct {{");

            if (variant.Payload.Count == 1)
            {
                sb.AppendLine($"\tValue {TypeMappings.ToGo(variant.Payload[0])}");
            }
            else
            {
                for (var index = 0; index < variant.Payload.Count; index++)
                    sb.AppendLine($"\tValue{index + 1} {TypeMappings.ToGo(variant.Payload[index])}");
            }

            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine($"func ({variantTypeName}) is{e.Name}() {{}}");

            var constructorName = $"New{variantTypeName}";
            var constructorParams = string.Join(", ", variant.Payload.Select((type, index) =>
                $"value{index + 1} {TypeMappings.ToGo(type)}"));

            if (variant.Payload.Count == 0)
            {
                sb.AppendLine($"func {constructorName}() {e.Name} {{");
                sb.AppendLine($"\treturn {variantTypeName}{{}}");
                sb.AppendLine("}");
            }
            else if (variant.Payload.Count == 1)
            {
                sb.AppendLine($"func {constructorName}(value {TypeMappings.ToGo(variant.Payload[0])}) {e.Name} {{");
                sb.AppendLine($"\treturn {variantTypeName}{{Value: value}}");
                sb.AppendLine("}");
            }
            else
            {
                sb.AppendLine($"func {constructorName}({constructorParams}) {e.Name} {{");
                var assignments = string.Join(", ", variant.Payload.Select((_, index) =>
                    $"Value{index + 1}: value{index + 1}"));
                sb.AppendLine($"\treturn {variantTypeName}{{{assignments}}}");
                sb.AppendLine("}");
            }

            sb.AppendLine();
        }
    }

    private sealed record StorageQueryBinding(
        string ModelName,
        IRStorageQuery Query,
        string CallableName,
        IReadOnlyList<IRStorageField> ResultFields);

    private static IReadOnlyList<StorageQueryBinding> CollectStorageQueryBindings(IRDocument ir)
    {
        var bindings = new List<StorageQueryBinding>();
        foreach (var declaration in ir.StorageDeclarations)
        {
            var entities = declaration.Entities
                .GroupBy(entity => entity.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            foreach (var query in declaration.Queries)
            {
                if (!entities.TryGetValue(query.SourceEntity, out var entity))
                    continue;
                IReadOnlyList<IRStorageField> fields = entity.Fields;
                foreach (var project in query.Steps.OfType<IRStorageProjectStep>())
                {
                    fields = project.Projections.Select(projection =>
                    {
                        var source = fields.Single(field => field.Name == projection.Field);
                        return new IRStorageField(projection.Alias ?? projection.Field, source.Type);
                    }).ToList();
                }
                bindings.Add(new StorageQueryBinding(
                    declaration.Name,
                    query,
                    StorageQueryNames.Callable(declaration.Name, query.Name),
                    fields));
            }
        }
        return bindings;
    }

    private sealed record StorageMutationBinding(
        string ModelName,
        IRStorageMutation Mutation,
        string CallableName);

    private static IReadOnlyList<StorageMutationBinding> CollectStorageMutationBindings(IRDocument ir) =>
        ir.StorageDeclarations
            .SelectMany(declaration => declaration.Mutations.Select(mutation =>
                new StorageMutationBinding(
                    declaration.Name,
                    mutation,
                    StorageMutationNames.Callable(declaration.Name, mutation.Name))))
            .ToList();

    private static void EmitStorageQueryBindings(
        StringBuilder sb,
        IReadOnlyList<StorageQueryBinding> bindings,
        bool emitTimestampHelpers)
    {
        if (emitTimestampHelpers)
        {
            sb.AppendLine("func __junexisStorageTime(value string) time.Time {");
            sb.AppendLine("\tnormalized := strings.ReplaceAll(value, `\\u002B`, \"+\")");
            sb.AppendLine("\tparsed, err := time.Parse(time.RFC3339Nano, normalized)");
            sb.AppendLine("\tif err != nil { log.Panicf(\"[storage] invalid timestamp result: %v\", err) }");
            sb.AppendLine("\treturn parsed");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("func __junexisStorageNullableTime(value *string) *time.Time {");
            sb.AppendLine("\tif value == nil { return nil }");
            sb.AppendLine("\tparsed := __junexisStorageTime(*value)");
            sb.AppendLine("\treturn &parsed");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        foreach (var binding in bindings)
        {
            var rowName = StorageQueryNames.Row(binding.ModelName, binding.Query.Name);
            sb.AppendLine($"// {rowName} is the Junexis task-facing result of {binding.ModelName}.{binding.Query.Name}.");
            sb.AppendLine($"type {rowName} struct {{");
            foreach (var field in binding.ResultFields)
                sb.AppendLine($"\t{PascalCase(field.Name)} {StorageGoType(field.Type)} `json:\"{EscapeGoString(field.Name)}\"`");
            sb.AppendLine("}");
            sb.AppendLine();

            var parameters = string.Join(", ", binding.Query.Params.Select(parameter =>
                $"{parameter.Name} {StorageGoType(parameter.Type)}"));
            sb.AppendLine($"// {binding.CallableName} executes a named query through the deployment-selected provider.");
            sb.AppendLine($"func {binding.CallableName}({parameters}) []{rowName} {{");
            sb.AppendLine("\tif junexisStorageProvider == nil { log.Panic(\"[storage] provider is not initialized\") }");
            sb.AppendLine($"\trawRows, storageErr := junexisQueries.{binding.CallableName}(");
            sb.AppendLine("\t\tjunexisTaskContext,");
            sb.AppendLine("\t\tjunexisStorageProvider,");
            sb.AppendLine($"\t\tjunexisQueries.{binding.CallableName}Params{{");
            foreach (var parameter in binding.Query.Params)
                sb.AppendLine($"\t\t\t{PascalCase(parameter.Name)}: {parameter.Name},");
            sb.AppendLine("\t\t},");
            sb.AppendLine("\t)");
            sb.AppendLine($"\tif storageErr != nil {{ log.Panicf(\"[storage] query {binding.ModelName}.{binding.Query.Name} failed: %v; cause=%v\", storageErr, storageErr.Cause) }}");
            sb.AppendLine($"\trows := make([]{rowName}, 0, len(rawRows))");
            sb.AppendLine("\tfor _, raw := range rawRows {");
            sb.AppendLine($"\t\trows = append(rows, {rowName}{{");
            foreach (var field in binding.ResultFields)
                sb.AppendLine($"\t\t\t{PascalCase(field.Name)}: {StorageResultConversion(field.Type, $"raw.{PascalCase(field.Name)}")},");
            sb.AppendLine("\t\t})");
            sb.AppendLine("\t}");
            sb.AppendLine("\treturn rows");
            sb.AppendLine("}");
            sb.AppendLine();
        }
    }

    private static void EmitStorageMutationBindings(
        StringBuilder sb,
        IReadOnlyList<StorageMutationBinding> bindings)
    {
        foreach (var binding in bindings)
        {
            var resultName = StorageMutationNames.Result(binding.ModelName, binding.Mutation.Name);
            sb.AppendLine($"// {resultName} is the confirmed Junexis task-facing result of {binding.ModelName}.{binding.Mutation.Name}.");
            sb.AppendLine($"type {resultName} struct {{");
            sb.AppendLine("\tOutcome string");
            sb.AppendLine("\tAffectedRows int64");
            sb.AppendLine("}");
            sb.AppendLine();

            var parameters = string.Join(", ", binding.Mutation.Params.Select(parameter =>
                $"{parameter.Name} {StorageGoType(parameter.Type)}"));
            sb.AppendLine($"// {binding.CallableName} executes a named mutation through the deployment-selected provider.");
            sb.AppendLine($"func {binding.CallableName}({parameters}) {resultName} {{");
            sb.AppendLine("\tif junexisStorageProvider == nil { log.Panic(\"[storage] provider is not initialized\") }");
            sb.AppendLine($"\traw, storageErr := junexisMutations.{binding.CallableName}(");
            sb.AppendLine("\t\tjunexisTaskContext,");
            sb.AppendLine("\t\tjunexisStorageProvider,");
            sb.AppendLine($"\t\tjunexisMutations.{binding.CallableName}Params{{");
            foreach (var parameter in binding.Mutation.Params)
                sb.AppendLine($"\t\t\t{PascalCase(parameter.Name)}: {parameter.Name},");
            sb.AppendLine("\t\t},");
            sb.AppendLine("\t)");
            sb.AppendLine($"\tif storageErr != nil {{ log.Panicf(\"[storage] mutation {binding.ModelName}.{binding.Mutation.Name} failed: %v; cause=%v\", storageErr, storageErr.Cause) }}");
            sb.AppendLine($"\treturn {resultName}{{Outcome: raw.Outcome, AffectedRows: raw.AffectedRows}}");
            sb.AppendLine("}");
            sb.AppendLine();
        }
    }

    private static string StorageGoType(IRType type)
    {
        var nullable = type is IRUnionType union && union.Variants.Any(item => item is IRNamedType { Name: "Null" });
        var concrete = StorageTypeWithoutNull(type);
        var goType = concrete switch
        {
            IRNamedType { Name: "Int" or "i8" or "i16" or "i32" or "i64" or "u8" or "u16" or "u32" } => "int64",
            IRNamedType { Name: "Float" or "f32" or "f64" } => "float64",
            IRNamedType { Name: "Bool" } => "bool",
            IRNamedType { Name: "Text" or "Decimal" or "Uuid" } => "string",
            IRNamedType { Name: "Bytes" } => "[]byte",
            IRNamedType { Name: "Time" } => "time.Time",
            IRNamedType { Name: "Json" } => "json.RawMessage",
            _ => TypeMappings.ToGo(concrete)
        };
        return nullable ? "*" + goType : goType;
    }

    private static IRType StorageTypeWithoutNull(IRType type) => type switch
    {
        IRUnionType union => union.Variants.Single(item => item is not IRNamedType { Name: "Null" }),
        _ => type
    };

    private static string StorageResultConversion(IRType type, string expression)
    {
        var nullable = type is IRUnionType union && union.Variants.Any(item => item is IRNamedType { Name: "Null" });
        return StorageTypeWithoutNull(type) is IRNamedType { Name: "Time" }
            ? nullable ? $"__junexisStorageNullableTime({expression})" : $"__junexisStorageTime({expression})"
            : expression;
    }

    // Tracked state variable names for the task currently being emitted.
    // Set by EmitTask; used by EmitExpr to redirect IRIdentExpr → state.Field.
    [ThreadStatic]
    private static HashSet<string>? _stateVarNames;
    [ThreadStatic] private static Dictionary<string, IRType>? _stateVarTypes;
    // Struct field type lookup: structName → (fieldName → goType) — built once per Generate()
    private static Dictionary<string, Dictionary<string, string>>? _structFields;
    // Local variables that have interface{} type (assigned from stubs without explicit type)
    [ThreadStatic] private static HashSet<string>? _interfaceVars;
    // Extern stubs registered for this document (set in Generate())
    [ThreadStatic] private static List<ExternStub>? _externStubs;
    // Config field names for current task (used to redirect bare identifiers in string interpolation)
    [ThreadStatic] private static HashSet<string>? _configVarNames;
    [ThreadStatic] private static Dictionary<string, string>? _localVarGoTypes;
    [ThreadStatic] private static Dictionary<string, QueueAckBinding>? _queueAckBindings;
    [ThreadStatic] private static IReadOnlyDictionary<string, IRSourceSpan>? _sourceMap;
    [ThreadStatic] private static uint _currentTaskId;
    [ThreadStatic] private static string? _currentTaskName;
    [ThreadStatic] private static string? _currentNarrativeId;
    [ThreadStatic] private static Dictionary<string, IReadOnlyList<string>>? _storageQueryParams;
    [ThreadStatic] private static Dictionary<string, IReadOnlyList<string>>? _storageMutationParams;

    private sealed record QueueAckBinding(string QueueExpr, string MessageIdVar);

    private static void EmitTask(StringBuilder sb, IRDocument ir, IRTask task, int taskIndex)
    {
        var globalTaskIndex = ir.Tasks
            .Select((candidate, index) => new { candidate, index })
            .FirstOrDefault(item => ReferenceEquals(item.candidate, task) || item.candidate.Name == task.Name)
            ?.index ?? -1;
        var taskSourcePath = globalTaskIndex >= 0 ? $"/Tasks[{globalTaskIndex}]" : null;
        _currentTaskId = (uint)(globalTaskIndex >= 0 ? globalTaskIndex + 1 : taskIndex + 1);
        _currentTaskName = task.Name;

        var profile = CodeGenPlatformProfiles.Resolve(task.Platforms.First(p => p.Target == "server"));
        var paramList = string.Join(", ", task.Params
            .Select(p => $"{p.Name} {TypeMappings.ToGo(p.Type)}"));

        var retType = task.OutputType is not null
            ? TypeMappings.ToGo(task.OutputType) : "";

        var sig = retType == ""
            ? $"func {task.Name}({paramList})"
            : $"func {task.Name}({paramList}) {retType}";

        sb.AppendLine($"// {task.Name} — JunexisLang @server task");
    sb.AppendLine($"const {task.Name}PlatformVariant = \"{profile.Variant}\"");
    sb.AppendLine($"const {task.Name}SupportsThreading = {(profile.SupportsThreading ? "true" : "false")}");
    sb.AppendLine($"const {task.Name}SupportsNetworking = {(profile.SupportsNetworking ? "true" : "false")}");
    sb.AppendLine($"const {task.Name}SupportsFileIO = {(profile.SupportsFileIO ? "true" : "false")}");
    sb.AppendLine($"const {task.Name}HasState = {(task.States.Count > 0 ? "true" : "false")}");
    sb.AppendLine($"const {task.Name}MaxMemoryBytes int64 = {profile.MaxMemoryBytes}");
        sb.AppendLine($"{sig} {{");
        sb.AppendLine($"\t_ = log.Println");
        if (!string.IsNullOrWhiteSpace(_currentNarrativeId))
        {
            var narrativeId = _currentNarrativeId;
            var taskIdx = _currentTaskId;
            var taskSource = taskSourcePath is not null ? SourceForPath(taskSourcePath) : null;
            var taskFile = taskSource?.File ?? "";
            var taskLine = taskSource?.Line ?? 0;
            var boundaryIndex = globalTaskIndex >= 0 ? globalTaskIndex : taskIndex;
            if (boundaryIndex == 0)
            {
                sb.AppendLine($"\t_emitTrace(\"{TraceRuntimeContract.CommunicationSend}\", {taskIdx}, \"{EscapeString(taskFile)}\", {taskLine}, \"{EscapeString(task.Name)}\", map[string]string{{\"{TraceRuntimeContract.NarrativeIdAttribute}\":\"{EscapeString(narrativeId)}\",\"{TraceRuntimeContract.PayloadHashAttribute}\":\"startup-0\",\"{TraceRuntimeContract.PayloadTypeAttribute}\":\"task-start\"}}) // COMM_SEND on first task start");
            }
            else
            {
                sb.AppendLine($"\t_emitTrace(\"{TraceRuntimeContract.CommunicationReceive}\", {taskIdx}, \"{EscapeString(taskFile)}\", {taskLine}, \"{EscapeString(task.Name)}\", map[string]string{{\"{TraceRuntimeContract.NarrativeIdAttribute}\":\"{EscapeString(narrativeId)}\",\"{TraceRuntimeContract.PayloadHashAttribute}\":\"startup-{boundaryIndex - 1}\",\"{TraceRuntimeContract.PayloadTypeAttribute}\":\"task-start\"}}) // COMM_RECV correlated with previous task startup");
                sb.AppendLine($"\t_emitTrace(\"{TraceRuntimeContract.CommunicationSend}\", {taskIdx}, \"{EscapeString(taskFile)}\", {taskLine}, \"{EscapeString(task.Name)}\", map[string]string{{\"{TraceRuntimeContract.NarrativeIdAttribute}\":\"{EscapeString(narrativeId)}\",\"{TraceRuntimeContract.PayloadHashAttribute}\":\"startup-{boundaryIndex}\",\"{TraceRuntimeContract.PayloadTypeAttribute}\":\"task-start\"}}) // COMM_SEND on task start");
            }
        }

        if (task.OutputType is IRCommType { Kind: IRCommKind.Chan, Element: var channelElement })
        {
            var chanContract = ChanRuntimeContracts.ForGo(channelElement);
            sb.AppendLine($"\tresult := {chanContract.FactoryExpression}");
            var descriptors = ir.ChanBridges
                .Where(descriptor => descriptor.SourceTask == task.Name)
                .ToList();
            if (profile.SupportsNetworking)
            {
                foreach (var descriptor in descriptors)
                    sb.AppendLine($"\tregisterChanBridgeWebSocketEndpoint(\"{descriptor.ChannelRoute}\", result)");
            }
            if (UsesProducerTemplate(task))
            {
                sb.AppendLine("\tgo func() {");
                sb.AppendLine("\t\tticker := time.NewTicker(time.Second)");
                sb.AppendLine("\t\tdefer ticker.Stop()");
                sb.AppendLine("\t\tfor range ticker.C {");
                sb.AppendLine($"\t\t\t{chanContract.RenderSendInvocation("result", $"emit{task.Name}Value()")}");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t}() // Auto-generated producer template for empty chan task");
            }
        }
        else if (task.OutputType is IRCommType { Kind: IRCommKind.Queue })
        {
            var queueContract = QueueRuntimeContracts.ForGo(((IRCommType)task.OutputType).Element);
            sb.AppendLine($"\tresult := {queueContract.FactoryExpression}");
            var descriptors = ir.QueueBridges
                .Where(descriptor => descriptor.SourceTask == task.Name)
                .ToList();
            if (profile.SupportsNetworking)
            {
                foreach (var descriptor in descriptors)
                    sb.AppendLine($"\tregisterQueueBridgeDataEndpoint(\"{descriptor.PollRoute}\", result)");
                foreach (var descriptor in descriptors)
                    sb.AppendLine($"\tregisterQueueBridgeAckEndpoint(\"{descriptor.AckRoute}\", result)");
            }
        }
        else if (task.OutputType is IRCommType { Kind: IRCommKind.Stream })
        {
            var streamContract = StreamRuntimeContracts.ForGo(((IRCommType)task.OutputType).Element);
            sb.AppendLine($"\tresult := {streamContract.FactoryExpression}");
            var descriptors = ir.StreamBridges
                .Where(descriptor => descriptor.SourceTask == task.Name)
                .ToList();
            var streamElementType = TypeMappings.ToGo(((IRCommType)task.OutputType).Element);
            sb.AppendLine($"\tvar __latestResult {streamElementType}");
            sb.AppendLine("\tvar __hasLatestResult bool");
            sb.AppendLine($"\tpublishResult := func(value {streamElementType}) {{");
            sb.AppendLine("\t\t__latestResult = value");
            sb.AppendLine("\t\t__hasLatestResult = true");
            sb.AppendLine($"\t\t{streamContract.RenderPublishInvocation("result", "value")}");
            sb.AppendLine("\t}");
            sb.AppendLine("\t_ = publishResult // suppress unused-variable if body never calls publishResult");
            sb.AppendLine("\t_ = __latestResult // standalone stream tasks may have no network bridge");
            sb.AppendLine("\t_ = __hasLatestResult");
            if (profile.SupportsNetworking)
            {
                foreach (var descriptor in descriptors)
                    sb.AppendLine($"\tregisterStreamBridgeDataEndpoint(\"{descriptor.StreamRoute}\", func() ({streamElementType}, bool) {{ return __latestResult, __hasLatestResult }})");
            }
        }
        else if (retType != "" && !ContainsReturn(task.Body))
        {
            sb.AppendLine($"\tvar result {retType}");
        }

        if (UsesChanConsumerTemplate(task))
        {
            var inputName = task.Params[0].Name;
            sb.AppendLine($"\tfor item := range {inputName} {{");
            sb.AppendLine($"\t\tconsume{task.Name}Item(item)");
            sb.AppendLine("\t} // Auto-generated consumer template for single chan input");
        }
        else if (UsesQueueConsumerTemplate(task) && task.Params[0].Type is IRCommType { Element: var queueElement })
        {
            var inputName = task.Params[0].Name;
            var queueContract = QueueRuntimeContracts.ForGo(queueElement);
            sb.AppendLine("\tfor {");
            sb.AppendLine($"\t\trecvResult, messageId := {queueContract.RenderReceiveInvocation(inputName)}");
            sb.AppendLine("\t\tif !recvResult.Ok || messageId == nil {");
            sb.AppendLine("\t\t\tbreak");
            sb.AppendLine("\t\t}");
            sb.AppendLine($"\t\tif err := consume{task.Name}Item(recvResult.Value); err != nil {{");
            sb.AppendLine("\t\t\t// Runtime queue policy decides retry vs DLQ after Nack.");
            sb.AppendLine($"\t\t\t{queueContract.RenderNackInvocation(inputName, "*messageId", "err.Error()")}");
            sb.AppendLine("\t\t\tcontinue");
            sb.AppendLine("\t\t}");
            sb.AppendLine($"\t\t{queueContract.RenderAckInvocation(inputName, "*messageId")}");
            sb.AppendLine("\t} // Auto-generated consumer template for single queue input");
        }

        var taskScope = task.Params.ToDictionary(p => p.Name, p => p.Type);

        // For communication-output tasks with a body (not using auto-templates): wrap in goroutine
        // so the task function returns the channel/queue/stream handle immediately without blocking.
        bool wrapInGoroutine = task.OutputType is IRCommType
            && task.Body.Count > 0
            && !UsesProducerTemplate(task)
            && !UsesChanConsumerTemplate(task)
            && !UsesQueueConsumerTemplate(task);

        if (wrapInGoroutine)
        {
            sb.AppendLine("\tgo func() {");
            if (task.OutputType is IRCommType { Kind: IRCommKind.Chan })
                sb.AppendLine("\t\tdefer close(result)");
        }

        int bodyIndent = wrapInGoroutine ? 2 : 1;

        if (task.States.Count > 0)
        {
            _stateVarNames = new HashSet<string>(task.States.Select(s => s.Name), StringComparer.Ordinal);
                _stateVarTypes = task.States.ToDictionary(s => s.Name, s => s.Type);
            _interfaceVars = null; // reset per task
            var si = new string('\t', bodyIndent);
            sb.AppendLine($"{si}state := load{task.Name}State()");
            sb.AppendLine($"{si}defer func() {{ save{task.Name}State(state) }}()");
        }
        else
        {
            _stateVarNames = null;
                _stateVarTypes = null;
        }

        _configVarNames = task.Config is { Fields.Count: > 0 } cfgFields
            ? new HashSet<string>(cfgFields.Fields.Select(f => f.Name), StringComparer.Ordinal)
            : null;

        if (task.Config is { Fields.Count: > 0 } cfg)
        {
            var si = new string('\t', bodyIndent);
            sb.Append($"{si}config := struct {{");
            foreach (var field in cfg.Fields)
                sb.Append($" {PascalCase(field.Name)} {TypeMappings.ToGo(field.Type)};");
            sb.AppendLine(" }{");
            foreach (var field in cfg.Fields)
            {
                string defVal;
                if (field.Default is not null)
                {
                    if (field.Type is IRNamedType { Name: "Duration" })
                        defVal = EmitDuration(field.Default);
                    else
                        defVal = EmitExpr(field.Default);
                }
                else
                    defVal = EmitZeroValue(field.Type);
                sb.AppendLine($"{si}\t{PascalCase(field.Name)}: {defVal},");
            }
            sb.AppendLine($"{si}}}");
            sb.AppendLine($"{si}_ = config");
        }

        EmitStatements(sb, task.Body, bodyIndent, task.OutputType, taskScope, taskSourcePath is not null ? $"{taskSourcePath}/Body" : null);

        _stateVarNames = null;
        _stateVarTypes = null;
        _interfaceVars = null;
        _configVarNames = null;
        _localVarGoTypes = null;
        _queueAckBindings = null;
        _currentTaskId = 0;
        _currentTaskName = null;

        if (wrapInGoroutine)
            sb.AppendLine("\t}()");

        if (retType != "" && !ContainsReturn(task.Body))
            sb.AppendLine($"\treturn result");

        sb.AppendLine("}");
        sb.AppendLine();

        if (task.States.Count > 0)
        {
            var stateCacheName = $"{ToLocalIdentifier(task.Name)}StateCache";
            var stateFileName = $"{ToLocalIdentifier(task.Name)}StateFile";
            sb.AppendLine($"type {task.Name}State struct {{");
            foreach (var state in task.States)
                sb.AppendLine($"\t{PascalCase(state.Name)} {TypeMappings.ToGo(state.Type)} `json:\"{EscapeString(state.Name)}\"`");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine($"var {stateCacheName} *{task.Name}State");
            if (profile.SupportsFileIO)
                sb.AppendLine($"const {stateFileName} = \"{EscapeString(task.Name)}.state.json\"");
            sb.AppendLine();

            sb.AppendLine($"func createDefault{task.Name}State() {task.Name}State {{");
            sb.AppendLine($"\treturn {task.Name}State{{");
            foreach (var state in task.States)
                sb.AppendLine($"\t\t{PascalCase(state.Name)}: {(state.Init is not null ? EmitTypedInitialValue(state.Init, state.Type) : EmitZeroValue(state.Type))},");
            sb.AppendLine("\t}");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine($"func load{task.Name}State() {task.Name}State {{");
            sb.AppendLine($"\tif {stateCacheName} != nil {{");
            sb.AppendLine($"\t\treturn *{stateCacheName}");
            sb.AppendLine("\t}");
            if (profile.SupportsFileIO)
            {
                sb.AppendLine($"\tif data, err := os.ReadFile({stateFileName}); err == nil {{");
                sb.AppendLine($"\t\tvar state {task.Name}State");
                sb.AppendLine("\t\tif json.Unmarshal(data, &state) == nil {");
                sb.AppendLine($"\t\t\t{stateCacheName} = &state");
                sb.AppendLine("\t\t\treturn state");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t}");
            }
            sb.AppendLine($"\tstate := createDefault{task.Name}State()");
            sb.AppendLine($"\t{stateCacheName} = &state");
            sb.AppendLine("\treturn state");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine($"func save{task.Name}State(state {task.Name}State) {{");
            sb.AppendLine($"\t{stateCacheName} = &state");
            if (profile.SupportsFileIO)
            {
                sb.AppendLine("\tif data, err := json.Marshal(state); err == nil {");
                sb.AppendLine($"\t\t_ = os.WriteFile({stateFileName}, data, 0600)");
                sb.AppendLine("\t}");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (UsesProducerTemplate(task) && task.OutputType is IRCommType { Kind: IRCommKind.Chan, Element: var producerElement })
        {
            sb.AppendLine($"func emit{task.Name}Value() {TypeMappings.ToGo(producerElement)} {{");
            sb.AppendLine($"\t_ = {task.Name}SupportsThreading");
            sb.AppendLine($"\t_ = {task.Name}SupportsNetworking");
            if (task.States.Count > 0)
            {
                sb.AppendLine($"\tif {task.Name}HasState {{");
                sb.AppendLine($"\t\tstate := load{task.Name}State()");
                sb.AppendLine($"\t\treturn read{task.Name}StateValue(state)");
                sb.AppendLine("\t}");
            }
            sb.AppendLine($"\treturn {EmitZeroValue(producerElement)}");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (UsesChanConsumerTemplate(task) && task.Params[0].Type is IRCommType { Element: var consumerElement })
        {
            sb.AppendLine($"func consume{task.Name}Item(item {TypeMappings.ToGo(consumerElement)}) {{");
            sb.AppendLine($"\t_ = {task.Name}SupportsThreading");
            sb.AppendLine($"\t_ = {task.Name}SupportsNetworking");
            if (task.States.Count > 0)
            {
                sb.AppendLine($"\tif {task.Name}HasState {{");
                sb.AppendLine($"\t\tstate := load{task.Name}State()");
                sb.AppendLine($"\t\twrite{task.Name}StateValue(&state, item)");
                sb.AppendLine($"\t\tsave{task.Name}State(state)");
                sb.AppendLine("\t}");
            }
            sb.AppendLine("\tlog.Println(item)");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (UsesQueueConsumerTemplate(task) && task.Params[0].Type is IRCommType { Element: var queueConsumerElement })
        {
            sb.AppendLine($"func consume{task.Name}Item(item {TypeMappings.ToGo(queueConsumerElement)}) error {{");
            sb.AppendLine($"\t_ = {task.Name}SupportsThreading");
            sb.AppendLine($"\t_ = {task.Name}SupportsNetworking");
            if (task.States.Count > 0)
            {
                sb.AppendLine($"\tif {task.Name}HasState {{");
                sb.AppendLine($"\t\tstate := load{task.Name}State()");
                sb.AppendLine($"\t\twrite{task.Name}StateValue(&state, item)");
                sb.AppendLine($"\t\tsave{task.Name}State(state)");
                sb.AppendLine("\t}");
            }
            sb.AppendLine("\tlog.Println(item)");
            sb.AppendLine("\treturn nil");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (task.States.Count > 0 && UsesProducerTemplate(task) && task.OutputType is IRCommType { Kind: IRCommKind.Chan, Element: var readElement })
        {
            var matchingStates = StateAccessPlanner.GetReadPreferredCompatibleStates(task, readElement, TypeMappings.ToGo);
            sb.AppendLine($"func read{task.Name}StateValue(state {task.Name}State) {TypeMappings.ToGo(readElement)} {{");
            if (matchingStates.Count > 0)
            {
                if (matchingStates.Count > 1)
                    sb.AppendLine($"\t// Candidate state fields: {string.Join(", ", matchingStates.Select(state => state.Name))}");
                sb.AppendLine($"\treturn state.{PascalCase(matchingStates[0].Name)}");
            }
            else
            {
                sb.AppendLine("\t_ = state");
                sb.AppendLine($"\treturn {EmitZeroValue(readElement)}");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (task.States.Count > 0 && (UsesChanConsumerTemplate(task) || UsesQueueConsumerTemplate(task)) && task.Params[0].Type is IRCommType { Element: var writeElement })
        {
            var matchingStates = StateAccessPlanner.GetWritePreferredCompatibleStates(task, writeElement, TypeMappings.ToGo);
            sb.AppendLine($"func write{task.Name}StateValue(state *{task.Name}State, item {TypeMappings.ToGo(writeElement)}) {{");
            if (matchingStates.Count > 0)
            {
                if (matchingStates.Count > 1)
                    sb.AppendLine($"\t// Candidate state fields: {string.Join(", ", matchingStates.Select(state => state.Name))}");
                foreach (var matchingState in matchingStates)
                    sb.AppendLine($"\tstate.{PascalCase(matchingState.Name)} = item");
                sb.AppendLine($"\t_ = state.{PascalCase(matchingStates[0].Name)}");
            }
            else
            {
                sb.AppendLine("\t_ = state");
                sb.AppendLine("\t_ = item");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }
    }

    private static void EmitStatements(StringBuilder sb, IReadOnlyList<IRStmt> statements, int indentLevel, IRType? outputType, IReadOnlyDictionary<string, IRType>? scope = null, string? sourcePathPrefix = null)
    {
        for (var i = 0; i < statements.Count; i++)
            EmitStatement(sb, statements[i], indentLevel, outputType, scope, sourcePathPrefix is not null ? $"{sourcePathPrefix}[{i}]" : null);
    }

    private static bool UsesProducerTemplate(IRTask task) =>
        task.Params.Count == 0 &&
        task.Body.Count == 0 &&
        task.OutputType is IRCommType { Kind: IRCommKind.Chan };

    private static bool UsesChanConsumerTemplate(IRTask task) =>
        task.Params.Count == 1 &&
        task.Body.Count == 0 &&
        task.OutputType is null &&
        task.Params[0].Type is IRCommType { Kind: IRCommKind.Chan };

    private static bool UsesQueueConsumerTemplate(IRTask task) =>
        task.Params.Count == 1 &&
        task.Body.Count == 0 &&
        task.OutputType is null &&
        task.Params[0].Type is IRCommType { Kind: IRCommKind.Queue };

    private static void EmitStatement(StringBuilder sb, IRStmt statement, int indentLevel, IRType? outputType, IReadOnlyDictionary<string, IRType>? scope = null, string? sourcePath = null)
    {
        var indent = new string('\t', indentLevel);
        if (sourcePath is not null && SourceForPath(sourcePath) is not null)
            sb.AppendLine($"{indent}// junexis:source {sourcePath}");

        switch (statement)
        {
            case IRLetStmt letStmt:
                if (TryEmitQueueReceiveLet(sb, indent, letStmt, scope))
                    break;

                if (letStmt.Type is not null)
                {
                    var goT = TypeMappings.ToGo(letStmt.Type);
                    string initExpr;
                    if (letStmt.Init is IRTernaryExpr tern)
                        initExpr = $"func() {goT} {{ if {EmitExpr(tern.Cond)} {{ return {EmitExpr(tern.Then)} }}; return {EmitExpr(tern.Else)} }}()";
                    else
                        initExpr = EmitExpr(letStmt.Init);
                    sb.AppendLine($"{indent}var {letStmt.Name} {goT} = {initExpr}");
                    // Track local var type for use in assignment assertions
                    _localVarGoTypes ??= new Dictionary<string, string>();
                    _localVarGoTypes[letStmt.Name] = goT;
                }
                else
                {
                    var emittedInit = EmitExpr(letStmt.Init);
                    // Track if init is an extern stub call (returns interface{})
                    if (letStmt.Init is IRCallExpr { Callee: IRIdentExpr { Name: var calleeName } } &&
                        !s_goBuiltins.Contains(calleeName) &&
                        (_externStubs?.Any(s => s.Name == calleeName && s.ReturnType == "interface{}") ?? false))
                    {
                        _interfaceVars ??= new HashSet<string>();
                        _interfaceVars.Add(letStmt.Name);
                    }
                    // Also track if init is binary expr with interface{} operand
                    // Note: when arith op, result is concrete int64 (due to EmitInterfaceArithOperand); track in _localVarGoTypes not _interfaceVars
                    else if (letStmt.Init is IRBinaryExpr binaryInitIface)
                    {
                        bool anyIfaceOp =
                            HasInterfaceOperand(binaryInitIface.Left) || HasInterfaceOperand(binaryInitIface.Right);
                        if (anyIfaceOp && binaryInitIface.Op is "/" or "*" or "+" or "-")
                        {
                            // Result becomes int64 (concrete after assertion)
                            _localVarGoTypes ??= new Dictionary<string, string>();
                            _localVarGoTypes[letStmt.Name] = "int64";
                        }
                        else if (anyIfaceOp && _interfaceVars is not null)
                        {
                            _interfaceVars.Add(letStmt.Name);
                        }
                    }
                    sb.AppendLine($"{indent}{letStmt.Name} := {emittedInit}");
                }
                break;

            case IRLogStmt logStmt:
                sb.AppendLine($"{indent}log.Println({string.Join(", ", logStmt.Args.Select(EmitExpr))})");
                break;

            case IRExprStmt exprStmt:
                if (exprStmt.Expr is IRBinaryExpr binaryExpr && IsAssignmentOperator(binaryExpr.Op))
                {
                    string rhsExpr;
                    // Special case: resetting a state var list to empty — use typed empty slice
                    if (binaryExpr.Right is IRCallExpr { Callee: IRIdentExpr { Name: "__array__" }, Args.Count: 0 } &&
                        binaryExpr.Left is IRIdentExpr { Name: var lhsN } &&
                        _stateVarTypes != null && _stateVarTypes.TryGetValue(lhsN, out var lhsType) &&
                        lhsType is IRArrayType { Element: var elemType })
                    {
                        rhsExpr = $"[]{TypeMappings.ToGo(elemType)}{{}}";
                    }
                    // Special case: assigning interface{} var to typed var -> emit type assertion
                    else if (binaryExpr.Right is IRIdentExpr { Name: var rhsN } &&
                             _interfaceVars != null && _interfaceVars.Contains(rhsN) &&
                             binaryExpr.Left is IRIdentExpr { Name: var lhsN3 } &&
                             _localVarGoTypes != null && _localVarGoTypes.TryGetValue(lhsN3, out var lhsGoType))
                    {
                        rhsExpr = $"{EmitExpr(binaryExpr.Right)}.({lhsGoType})";
                    }
                    else
                    {
                        rhsExpr = EmitExpr(binaryExpr.Right);
                    }
                    sb.AppendLine($"{indent}{EmitExpr(binaryExpr.Left)} {binaryExpr.Op} {rhsExpr}");
                }
                else
                {
                    sb.AppendLine($"{indent}{EmitExpr(exprStmt.Expr)}");
                }
                break;

            case IRReturnStmt returnStmt:
                sb.AppendLine(returnStmt.Value is not null
                    ? $"{indent}return {EmitExpr(returnStmt.Value)}"
                    : $"{indent}return");
                break;

            case IRIfStmt ifStmt:
                EmitIfStatement(sb, ifStmt, indentLevel, outputType, scope, sourcePath);
                break;

            case IREveryStmt everyStmt:
                sb.AppendLine($"{indent}ticker := time.NewTicker({EmitDuration(everyStmt.Interval)})");
                sb.AppendLine($"{indent}defer ticker.Stop()");
                sb.AppendLine($"{indent}for {{");
                sb.AppendLine($"{indent}\tselect {{");
                sb.AppendLine($"{indent}\tcase <-junexisTaskContext.Done():");
                sb.AppendLine($"{indent}\t\treturn");
                sb.AppendLine($"{indent}\tcase <-ticker.C:");
                EmitStatements(sb, everyStmt.Body, indentLevel + 2, outputType, scope, sourcePath is not null ? $"{sourcePath}/Body" : null);
                if (_stateVarNames is not null && _currentTaskName is not null)
                    sb.AppendLine($"{indent}\t\tsave{_currentTaskName}State(state)");
                sb.AppendLine($"{indent}\t}}");
                sb.AppendLine($"{indent}}}");
                break;

            case IRLoopStmt loopStmt:
                sb.AppendLine($"{indent}for {{");
                sb.AppendLine($"{indent}\tselect {{");
                sb.AppendLine($"{indent}\tcase <-junexisTaskContext.Done():");
                sb.AppendLine($"{indent}\t\treturn");
                sb.AppendLine($"{indent}\tdefault:");
                sb.AppendLine($"{indent}\t}}");
                EmitStatements(sb, loopStmt.Body, indentLevel + 1, outputType, scope, sourcePath is not null ? $"{sourcePath}/Body" : null);
                sb.AppendLine($"{indent}}}");
                break;

            case IRForStmt forStmt:
            {
                var iterExpr = EmitExpr(forStmt.Iter);
                IRType? iterType = null;
                if (forStmt.Iter is IRIdentExpr iterIdent2 && scope is not null)
                    scope.TryGetValue(iterIdent2.Name, out iterType);

                if (iterType is IRCommType { Kind: IRCommKind.Chan })
                {
                    sb.AppendLine($"{indent}for {{");
                    sb.AppendLine($"{indent}\tselect {{");
                    sb.AppendLine($"{indent}\tcase <-junexisTaskContext.Done():");
                    sb.AppendLine($"{indent}\t\treturn");
                    sb.AppendLine($"{indent}\tcase {forStmt.Var}, ok := <-{iterExpr}:");
                    sb.AppendLine($"{indent}\t\tif !ok {{ return }}");
                    EmitReceiveTrace(sb, indent + "\t\t", forStmt.Var, ((IRCommType)iterType).Element, sourcePath);
                    EmitStatements(sb, forStmt.Body, indentLevel + 2, outputType, scope, sourcePath is not null ? $"{sourcePath}/Body" : null);
                    if (_stateVarNames is not null && _currentTaskName is not null)
                        sb.AppendLine($"{indent}\t\tsave{_currentTaskName}State(state)");
                    sb.AppendLine($"{indent}\t}}");
                    sb.AppendLine($"{indent}}}");
                }
                else if (iterType is IRCommType { Kind: IRCommKind.Queue })
                {
                    // Queue: Recv/Ack loop
                    sb.AppendLine($"{indent}for {{");
                    sb.AppendLine($"{indent}\t__qResult, __qMsgId := {iterExpr}.Recv()");
                    sb.AppendLine($"{indent}\tif !__qResult.Ok || __qMsgId == nil {{ break }}");
                    sb.AppendLine($"{indent}\t{forStmt.Var} := __qResult.Value");
                    var bodyScope = scope is not null ? new Dictionary<string, IRType>(scope) : new Dictionary<string, IRType>();
                    if (iterType is IRCommType { Element: var qElem })
                        bodyScope[forStmt.Var] = qElem;
                    EmitStatements(sb, forStmt.Body, indentLevel + 1, outputType, bodyScope, sourcePath is not null ? $"{sourcePath}/Body" : null);
                    sb.AppendLine($"{indent}\t{iterExpr}.Ack(*__qMsgId)");
                    sb.AppendLine($"{indent}}}");
                }
                else if (iterType is IRCommType { Kind: IRCommKind.Stream })
                {
                    // Stream: poll latest value in a loop
                    sb.AppendLine($"{indent}for {{");
                    sb.AppendLine($"{indent}\tif !{iterExpr}.HasLatest() {{ time.Sleep(10 * time.Millisecond); continue }}");
                    sb.AppendLine($"{indent}\t{forStmt.Var} := {iterExpr}.Latest()");
                    var bodyScope2 = scope is not null ? new Dictionary<string, IRType>(scope) : new Dictionary<string, IRType>();
                    if (iterType is IRCommType { Element: var sElem })
                        bodyScope2[forStmt.Var] = sElem;
                    EmitStatements(sb, forStmt.Body, indentLevel + 1, outputType, bodyScope2, sourcePath is not null ? $"{sourcePath}/Body" : null);
                    sb.AppendLine($"{indent}}}");
                }
                else
                {
                    // Default: slice/array range
                    sb.AppendLine($"{indent}for _, {forStmt.Var} := range {iterExpr} {{");
                    EmitStatements(sb, forStmt.Body, indentLevel + 1, outputType, scope, sourcePath is not null ? $"{sourcePath}/Body" : null);
                    sb.AppendLine($"{indent}}}");
                }
                break;
            }

            case IRMatchStmt matchStmt:
                EmitMatchStatement(sb, matchStmt, indentLevel, outputType, scope, sourcePath);
                break;

            case IRRecvStmt recvStmt:
                // recv <source> jako statement — odrzuca odebraną wartość
                sb.AppendLine($"{indent}_ = <-{EmitExpr(recvStmt.Source)}");
                break;

            case IRAckStmt ackStmt:
                EmitQueueAckNack(sb, indent, "Ack", ackStmt.Message, null);
                break;

            case IRNackStmt nackStmt:
                EmitQueueAckNack(sb, indent, "Nack", nackStmt.Message, nackStmt.Reason);
                break;

            case IRSendStmt sendStmt:
            {
                var sendValExpr = EmitExpr(sendStmt.Value);
                // If sending an interface{} variable, add type assertion based on output type
                if (sendStmt.Value is IRIdentExpr { Name: var svN } &&
                    _interfaceVars != null && _interfaceVars.Contains(svN) &&
                    outputType is not null)
                {
                    var elemGoType2 = outputType switch
                    {
                        IRCommType { Element: var el } => TypeMappings.ToGo(el),
                        IRType t => TypeMappings.ToGo(t)
                    };
                    if (elemGoType2 != "interface{}")
                    {
                        sendValExpr = $"{sendValExpr}.({elemGoType2})";
                    }
                }
                var traceSend = !string.IsNullOrWhiteSpace(_currentNarrativeId) && _currentTaskId != 0;
                var sendIndent = indent;
                if (traceSend)
                {
                    sb.AppendLine($"{indent}{{");
                    sendIndent += "\t";
                    sb.AppendLine($"{sendIndent}__junexisPayload := {sendValExpr}");
                    sendValExpr = "__junexisPayload";
                }
                if (sendStmt.Target is not null)
                {
                    RepeatSourceMarkerAfterTrace(sb, sendIndent, sourcePath, EmitSendTrace(sb, sendIndent, sendValExpr, outputType, sourcePath));
                    sb.AppendLine($"{sendIndent}junexis_send({EmitExpr(sendStmt.Target)}, {sendValExpr})");
                }
                else if (outputType is IRCommType { Kind: IRCommKind.Chan })
                {
                    var chanContract = ChanRuntimeContracts.ForGo(((IRCommType)outputType).Element);
                    RepeatSourceMarkerAfterTrace(sb, sendIndent, sourcePath, EmitSendTrace(sb, sendIndent, sendValExpr, outputType, sourcePath));
                    sb.AppendLine($"{sendIndent}{chanContract.RenderSendInvocation("result", sendValExpr)}");
                }
                else if (outputType is IRCommType { Kind: IRCommKind.Queue })
                {
                    RepeatSourceMarkerAfterTrace(sb, sendIndent, sourcePath, EmitSendTrace(sb, sendIndent, sendValExpr, outputType, sourcePath));
                    sb.AppendLine($"{sendIndent}result.Enqueue({sendValExpr})");
                }
                else if (outputType is IRCommType { Kind: IRCommKind.Stream })
                {
                    RepeatSourceMarkerAfterTrace(sb, sendIndent, sourcePath, EmitSendTrace(sb, sendIndent, sendValExpr, outputType, sourcePath));
                    sb.AppendLine($"{sendIndent}publishResult({sendValExpr})");
                }
                else if (outputType is not null)
                {
                    sb.AppendLine($"{sendIndent}result = {sendValExpr}");
                }
                else
                {
                    sb.AppendLine($"{sendIndent}// send: no output type declared for this task");
                }
                if (traceSend)
                    sb.AppendLine($"{indent}}}");
                break;
            }

            default:
                throw new NotSupportedException($"Go codegen does not support statement {statement.GetType().Name}.");
        }
    }

    private static bool TryEmitQueueReceiveLet(
        StringBuilder sb,
        string indent,
        IRLetStmt letStmt,
        IReadOnlyDictionary<string, IRType>? scope)
    {
        if (letStmt.Init is not IRRecvExpr { Source: IRIdentExpr { Name: var sourceName } })
            return false;

        if (scope is null ||
            !scope.TryGetValue(sourceName, out var sourceType) ||
            sourceType is not IRCommType { Kind: IRCommKind.Queue })
            return false;

        var queueExpr = EmitExpr(((IRRecvExpr)letStmt.Init).Source);
        var recvVar = $"__{letStmt.Name}Recv";
        var msgIdVar = $"__{letStmt.Name}MsgId";
        sb.AppendLine($"{indent}{recvVar}, {msgIdVar} := {queueExpr}.Recv()");
        sb.AppendLine($"{indent}if !{recvVar}.Ok || {msgIdVar} == nil {{");
        sb.AppendLine($"{indent}\treturn");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine($"{indent}{letStmt.Name} := {recvVar}.Value");

        _queueAckBindings ??= new Dictionary<string, QueueAckBinding>(StringComparer.Ordinal);
        _queueAckBindings[letStmt.Name] = new QueueAckBinding(queueExpr, msgIdVar);
        return true;
    }

    private static void EmitQueueAckNack(
        StringBuilder sb,
        string indent,
        string method,
        IRExpr message,
        IRExpr? reason)
    {
        if (message is IRIdentExpr { Name: var name } &&
            _queueAckBindings is not null &&
            _queueAckBindings.TryGetValue(name, out var binding))
        {
            if (method == "Ack")
            {
                sb.AppendLine($"{indent}{binding.QueueExpr}.Ack(*{binding.MessageIdVar})");
            }
            else
            {
                var reasonExpr = reason is not null ? EmitExpr(reason) : "\"nack\"";
                sb.AppendLine($"{indent}{binding.QueueExpr}.Nack(*{binding.MessageIdVar}, {reasonExpr})");
            }
            return;
        }

        sb.AppendLine($"{indent}// {method.ToLowerInvariant()}: target is not bound to a queue receive in codegen");
    }

    private static bool EmitSendTrace(StringBuilder sb, string indent, string payloadExpression, IRType? outputType, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(_currentNarrativeId) || _currentTaskId == 0)
            return false;

        var source = sourcePath is not null ? SourceForPath(sourcePath) : null;
        var file = EscapeString(source?.File ?? "");
        var line = source?.Line ?? 0;
        var function = EscapeString(_currentTaskName ?? "");
        var narrativeId = EscapeString(_currentNarrativeId);
        var type = EscapeString(TracePayloadType(outputType));

        sb.AppendLine($"{indent}_emitTrace(\"{TraceRuntimeContract.CommunicationSend}\", {_currentTaskId}, \"{file}\", {line}, \"{function}\", map[string]string{{\"{TraceRuntimeContract.NarrativeIdAttribute}\":\"{narrativeId}\",\"{TraceRuntimeContract.PayloadHashAttribute}\":_junexisPayloadHash({payloadExpression}),\"{TraceRuntimeContract.PayloadTypeAttribute}\":\"{type}\"}})");
        return true;
    }

    private static void EmitReceiveTrace(StringBuilder sb, string indent, string payloadExpression, IRType payloadType, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(_currentNarrativeId) || _currentTaskId == 0)
            return;

        var source = sourcePath is not null ? SourceForPath(sourcePath) : null;
        var file = EscapeString(source?.File ?? "");
        var line = source?.Line ?? 0;
        var function = EscapeString(_currentTaskName ?? "");
        var narrativeId = EscapeString(_currentNarrativeId);
        var type = EscapeString(TracePayloadType(payloadType));

        sb.AppendLine($"{indent}_emitTrace(\"{TraceRuntimeContract.CommunicationReceive}\", {_currentTaskId}, \"{file}\", {line}, \"{function}\", map[string]string{{\"{TraceRuntimeContract.NarrativeIdAttribute}\":\"{narrativeId}\",\"{TraceRuntimeContract.PayloadHashAttribute}\":_junexisPayloadHash({payloadExpression}),\"{TraceRuntimeContract.PayloadTypeAttribute}\":\"{type}\"}})");
    }

    private static void RepeatSourceMarkerAfterTrace(
        StringBuilder sb,
        string indent,
        string? sourcePath,
        bool traceEmitted)
    {
        if (traceEmitted && sourcePath is not null)
            sb.AppendLine($"{indent}// junexis:source {sourcePath}");
    }

    private static IRSourceSpan? SourceForPath(string path)
    {
        if (_sourceMap is null)
            return null;

        if (_sourceMap.TryGetValue(path, out var exact))
            return exact;

        return _sourceMap
            .Where(entry => entry.Key.StartsWith(path, StringComparison.Ordinal))
            .OrderBy(entry => entry.Key.Length)
            .Select(entry => entry.Value)
            .FirstOrDefault();
    }

    private static string TracePayloadType(IRType? outputType) => outputType switch
    {
        IRCommType { Element: IRNamedType named } => named.Name,
        IRCommType { Element: var element } => TypeMappings.ToGo(element),
        IRNamedType named => named.Name,
        null => "unknown",
        _ => TypeMappings.ToGo(outputType)
    };

    private static string EmitExpr(IRExpr expr) => expr switch
    {
        IRLiteralExpr literalExpr => EmitValue(literalExpr.Value),
        IRIdentExpr { Name: var identName } when _stateVarNames != null && _stateVarNames.Contains(identName)
            => $"state.{PascalCase(identName)}",
        IRIdentExpr identExpr => identExpr.Name,
        IRConfigAccessExpr configAccessExpr => configAccessExpr.Field is not null ? $"config.{char.ToLower(configAccessExpr.Field[0])}{configAccessExpr.Field[1..]}" : "config",
        IRBinaryExpr { Op: "as", Right: IRIdentExpr targetType } castExpr => EmitCast(castExpr.Left, targetType.Name),
        IRBinaryExpr { Op: "-" } subExpr when IsTimeExpr(subExpr.Left) || IsTimeExpr(subExpr.Right)
            => $"{EmitExpr(subExpr.Left)}.Sub({EmitExpr(subExpr.Right)})",
        // len()/dlugosc() returns int; when mixed with uint in comparisons/arithmetic, cast to uint32
        IRBinaryExpr { Left: IRCallExpr { Callee: IRIdentExpr { Name: "len" or "dlugosc" } } lenCall } arithExpr
            when arithExpr.Op is ">=" or "<=" or ">" or "<" or "==" or "!=" or "/" or "*" or "+" or "-"
            => $"(uint32(len({string.Join(", ", lenCall.Args.Select(a => EmitExpr(a.Value)))})) {MapBinaryOperator(arithExpr.Op)} {EmitExpr(arithExpr.Right)})",
        // State var arithmetic: cast to dominant float type when mixing float and uint
        IRBinaryExpr { Op: "/" or "*" or "+" or "-" } mixedExpr
            when NeedsFloatCast(mixedExpr.Left, mixedExpr.Right)
            => $"({EmitExpr(mixedExpr.Left)} {MapBinaryOperator(mixedExpr.Op)} float32({EmitExpr(mixedExpr.Right)}))",
        // Arithmetic with interface{} vars/stubs: add int64 type assertions on operands
        IRBinaryExpr { Op: "/" or "*" or "+" or "-" } ifaceArith
            when HasInterfaceOperand(ifaceArith.Left) || HasInterfaceOperand(ifaceArith.Right)
            => $"({EmitInterfaceArithOperand(ifaceArith.Left)} {MapBinaryOperator(ifaceArith.Op)} {EmitInterfaceArithOperand(ifaceArith.Right)})",
        IRBinaryExpr binaryExpr => $"({EmitExpr(binaryExpr.Left)} {MapBinaryOperator(binaryExpr.Op)} {EmitExpr(binaryExpr.Right)})",
        IRUnaryExpr unaryExpr => $"({MapUnaryOperator(unaryExpr.Op)}{EmitExpr(unaryExpr.Operand)})",
        IRCallExpr callExpr => EmitCall(callExpr),
        IRFieldAccessExpr fieldAccessExpr => $"{EmitExpr(fieldAccessExpr.Object)}.{PascalCase(fieldAccessExpr.Field)}",
        IRIndexExpr indexExpr => $"{EmitExpr(indexExpr.Object)}[{EmitExpr(indexExpr.Index)}]",
        IREnumAccessExpr enumAccessExpr => $"New{enumAccessExpr.TypeName}{enumAccessExpr.Variant}()",
        IRTernaryExpr ternaryExpr => EmitTernary(ternaryExpr),
        IRRecvExpr recvExpr => $"<-{EmitExpr(recvExpr.Source)}",
        IRSendExpr sendExpr => sendExpr.Target is not null
            ? $"func() interface{{}} {{ junexis_send({EmitExpr(sendExpr.Target)}, {EmitExpr(sendExpr.Value)}); return nil }}()"
            : EmitExpr(sendExpr.Value),
        IRAwaitExpr awaitExpr => EmitExpr(awaitExpr.Inner),
        _ => "nil"
    };

    private static string EmitZeroValue(IRType type) => type switch
    {
        IRNamedType { Name: "Int" } => "int64(0)",
        IRNamedType { Name: "Float" } => "float64(0)",
        IRNamedType { Name: "Bool" } => "false",
        IRNamedType { Name: "Text" } => "\"\"",
        IRNamedType { Name: "Time" } => "time.Time{}",
        IRNamedType { Name: "Duration" } => "time.Duration(0)",
        // Sized numeric primitives — use cast form for zero value
        IRNamedType { Name: "f32" or "f64" or "i8" or "i16" or "i32" or "i64"
                              or "u8" or "u16" or "u32" or "u64" } sized
            => $"{TypeMappings.ToGo(sized)}(0)",
        IRNamedType namedType => $"{namedType.Name}{{}}",
        IRArrayType => "nil",
        _ => "nil"
    };

    private static string EmitTypedInitialValue(IRExpr expression, IRType expectedType)
    {
        if (expression is IRCallExpr { Callee: IRIdentExpr { Name: "__array__" } } array &&
            expectedType is IRArrayType { Element: var elementType })
        {
            return $"[]{TypeMappings.ToGo(elementType)}{{{string.Join(", ", array.Args.Select(argument => EmitExpr(argument.Value)))}}}";
        }

        return EmitExpr(expression);
    }

    private static string MapBinaryOperator(string op) => op switch
    {
        "and" => "&&",
        "or" => "||",
        _ => op,
    };

    private static bool HasInterfaceOperand(IRExpr expr) =>
        (expr is IRIdentExpr { Name: var n } && (_interfaceVars?.Contains(n) ?? false)) ||
        (expr is IRCallExpr { Callee: IRIdentExpr { Name: var cn } } &&
            !s_goBuiltins.Contains(cn) &&
            (_externStubs?.Any(s => s.Name == cn && s.ReturnType == "interface{}") ?? false));

    private static string EmitInterfaceArithOperand(IRExpr expr) =>
        HasInterfaceOperand(expr) ? $"{EmitExpr(expr)}.(int64)" : EmitExpr(expr);

    private static string MapUnaryOperator(string op) => op switch
    {
        "not" => "!",
        _ => op,
    };

    private static bool IsAssignmentOperator(string op) => op is "=" or "+=" or "-=" or "*=" or "/=" or "%=";

    private static string EmitCast(IRExpr value, string targetType)
    {
        var emittedValue = EmitExpr(value);
        var goType = targetType switch
        {
            "Int" => "int64",
            "Float" => "float64",
            "Bool" => "bool",
            "Text" => "string",
            // Sized primitives
            "f32" => "float32", "f64" => "float64",
            "i8" => "int8", "i16" => "int16", "i32" => "int32", "i64" => "int64",
            "u8" => "uint8", "u16" => "uint16", "u32" => "uint32", "u64" => "uint64",
            _ => targetType
        };
        return $"{goType}({emittedValue})";
    }

    private static void EmitIfStatement(StringBuilder sb, IRIfStmt ifStmt, int indentLevel, IRType? outputType, IReadOnlyDictionary<string, IRType>? scope = null, string? sourcePath = null)
    {
        var indent = new string('\t', indentLevel);
        var condExpr = EmitExpr(ifStmt.Condition);
        // If condition is an interface{} var, assert to bool
        if (ifStmt.Condition is IRIdentExpr { Name: var condName } &&
            _interfaceVars != null && _interfaceVars.Contains(condName))
        {
            condExpr = $"{condName}.(bool)";
        }
        sb.AppendLine($"{indent}if {condExpr} {{");
        EmitStatements(sb, ifStmt.Then, indentLevel + 1, outputType, scope, sourcePath is not null ? $"{sourcePath}/Then" : null);

        switch (ifStmt.Else)
        {
            case IRElseIf elseIf:
                EmitElseIfClause(sb, elseIf.IfStmt, indentLevel, outputType, scope, sourcePath is not null ? $"{sourcePath}/Else" : null);
                return;

            case IRElseBlock elseBlock:
                sb.AppendLine($"{indent}}} else {{");
                EmitStatements(sb, elseBlock.Body, indentLevel + 1, outputType, scope, sourcePath is not null ? $"{sourcePath}/Else/Body" : null);
                break;
        }

        sb.AppendLine($"{indent}}}");
    }

    private static void EmitElseIfClause(StringBuilder sb, IRIfStmt ifStmt, int indentLevel, IRType? outputType, IReadOnlyDictionary<string, IRType>? scope = null, string? sourcePath = null)
    {
        var indent = new string('\t', indentLevel);
        sb.AppendLine($"{indent}}} else if {EmitExpr(ifStmt.Condition)} {{");
        EmitStatements(sb, ifStmt.Then, indentLevel + 1, outputType, scope, sourcePath is not null ? $"{sourcePath}/Then" : null);

        switch (ifStmt.Else)
        {
            case IRElseIf nestedElseIf:
                EmitElseIfClause(sb, nestedElseIf.IfStmt, indentLevel, outputType, scope, sourcePath is not null ? $"{sourcePath}/Else" : null);
                return;

            case IRElseBlock elseBlock:
                sb.AppendLine($"{indent}}} else {{");
                EmitStatements(sb, elseBlock.Body, indentLevel + 1, outputType, scope, sourcePath is not null ? $"{sourcePath}/Else/Body" : null);
                break;
        }

        sb.AppendLine($"{indent}}}");
    }

    private static void EmitMatchStatement(StringBuilder sb, IRMatchStmt matchStmt, int indentLevel, IRType? outputType, IReadOnlyDictionary<string, IRType>? scope = null, string? sourcePath = null)
    {
        var indent = new string('\t', indentLevel);
        sb.AppendLine($"{indent}switch __match := {EmitExpr(matchStmt.Subject)}; __match {{");

        for (var armIndex = 0; armIndex < matchStmt.Arms.Count; armIndex++)
        {
            var arm = matchStmt.Arms[armIndex];
            switch (arm.Pattern)
            {
                case IRLiteralPattern literalPattern:
                    sb.AppendLine($"{indent}case {EmitValue(literalPattern.Literal)}:");
                    break;

                case IRWildcardPattern:
                    sb.AppendLine($"{indent}default:");
                    break;

                case IRBindingPattern bindingPattern:
                    sb.AppendLine($"{indent}default:");
                    // If outputType gives us a concrete element type, assert __match to that type
                    if (outputType is IRCommType { Element: var matchElemType })
                    {
                        var matchGoType = TypeMappings.ToGo(matchElemType);
                        sb.AppendLine($"{indent}\t{bindingPattern.Name} := __match.({matchGoType})");
                    }
                    else
                    {
                        sb.AppendLine($"{indent}\t{bindingPattern.Name} := __match");
                    }
                    break;

                default:
                    sb.AppendLine($"{indent}// Unsupported match pattern: {arm.Pattern.GetType().Name}");
                    continue;
            }

            EmitStatements(sb, arm.Body, indentLevel + 1, outputType, scope, sourcePath is not null ? $"{sourcePath}/Arms[{armIndex}]/Body" : null);
            if (arm.BodyExpr is not null)
                sb.AppendLine($"{indent}\t{EmitExpr(arm.BodyExpr)}");
        }

        sb.AppendLine($"{indent}}}");
    }

    private static string EmitCall(IRCallExpr callExpr)
    {
        if (callExpr.Callee is IRIdentExpr { Name: "__array__" })
            return $"[]interface{{}}{{{string.Join(", ", callExpr.Args.Select(arg => EmitExpr(arg.Value)))}}}";

        if (callExpr.Callee is IREnumAccessExpr enumAccessExpr)
        {
            var constructorArgs = string.Join(", ", callExpr.Args.Select(arg => EmitExpr(arg.Value)));
            return $"New{enumAccessExpr.TypeName}{enumAccessExpr.Variant}({constructorArgs})";
        }

        if (callExpr.Callee is IRIdentExpr { Name: "__json__" })
        {
            var fields = string.Join(", ", callExpr.Args.Select(arg =>
                $"\"{arg.Name ?? "field"}\": {EmitExpr(arg.Value)}"));
            return $"map[string]interface{{}}{{{fields}}}";
        }

        if (callExpr.Callee is IRIdentExpr { Name: var storageQueryName } &&
            _storageQueryParams is not null &&
            _storageQueryParams.TryGetValue(storageQueryName, out var storageParameters))
        {
            IReadOnlyList<IRCallArg> ordered = callExpr.Args.All(argument => argument.Name is not null)
                ? storageParameters.Select(parameter =>
                    callExpr.Args.Single(argument => argument.Name == parameter)).ToList()
                : callExpr.Args;
            return $"{storageQueryName}({string.Join(", ", ordered.Select(argument => EmitExpr(argument.Value)))})";
        }

        if (callExpr.Callee is IRIdentExpr { Name: var storageMutationName } &&
            _storageMutationParams is not null &&
            _storageMutationParams.TryGetValue(storageMutationName, out var mutationParameters))
        {
            IReadOnlyList<IRCallArg> ordered = callExpr.Args.All(argument => argument.Name is not null)
                ? mutationParameters.Select(parameter =>
                    callExpr.Args.Single(argument => argument.Name == parameter)).ToList()
                : callExpr.Args;
            return $"{storageMutationName}({string.Join(", ", ordered.Select(argument => EmitExpr(argument.Value)))})";
        }

        // Built-in function mappings (checked BEFORE struct literal to avoid i32/u32/etc. being misclassified)
        if (callExpr.Callee is IRIdentExpr { Name: var builtinName })
        {
            var bArgs = string.Join(", ", callExpr.Args.Select(arg => EmitExpr(arg.Value)));
            switch (builtinName)
            {
                case "recv_any":  return bArgs == "" ? "nil" : $"recv_any({bArgs})";
                case "latest" when callExpr.Args.Count == 1:
                    return $"{EmitExpr(callExpr.Args[0].Value)}.Latest()";
                case "batch" when callExpr.Args.Count == 2:
                    return $"{EmitExpr(callExpr.Args[0].Value)}.Batch({EmitExpr(callExpr.Args[1].Value)})";
                case "sleep":     return $"time.Sleep({bArgs})";
                case "println":   return $"log.Println({bArgs})";
                case "print":     return $"fmt.Print({bArgs})";
                case "len":       return $"len({bArgs})";
                case "append":    return $"append({bArgs})";
                // Polish domain aliases → Go built-ins
                case "dlugosc":   return $"len({bArgs})";
                case "dodaj":     return $"append({bArgs})";
                // Numeric type casts
                case "f32":  case "float32": return $"float32({bArgs})";
                case "f64":  case "float64": return $"float64({bArgs})";
                case "i32":  case "int32":   return $"int32({bArgs})";
                case "i64":  case "int64":   return $"int64({bArgs})";
                case "u32":  case "uint32":  return $"uint32({bArgs})";
                case "u64":  case "uint64":  return $"uint64({bArgs})";
                case "i8":   case "int8":    return $"int8({bArgs})";
                case "u8":   case "uint8":   return $"uint8({bArgs})";
                case "i16":  case "int16":   return $"int16({bArgs})";
                case "u16":  case "uint16":  return $"uint16({bArgs})";
            }
        }

        // Struct literal: callee jest PascalCase i wszystkie argumenty są nazwane
        if (callExpr.Callee is IRIdentExpr { Name: var sName } &&
            sName.Length > 0 && char.IsUpper(sName[0]) &&
            callExpr.Args.Count > 0 &&
            callExpr.Args.All(a => a.Name is not null))
        {
            // Try to look up field types to emit assertions for interface{} vars
            Dictionary<string, string>? fieldTypes = null;
            _structFields?.TryGetValue(sName, out fieldTypes);
            var fields = string.Join(", ", callExpr.Args.Select(arg =>
            {
                var valExpr = EmitExpr(arg.Value);
                string? expectedGoType = null;
                fieldTypes?.TryGetValue(arg.Name!.ToLower(), out expectedGoType);
                // If this field value is an interface{} variable, assert to expected type
                if (_interfaceVars != null && arg.Value is IRIdentExpr { Name: var varN } && _interfaceVars.Contains(varN))
                {
                    if (expectedGoType != null)
                        valExpr = $"{valExpr}.({expectedGoType})";
                }
                // If field value is a direct extern stub call returning interface{}, assert to expected type
                else if (expectedGoType != null &&
                         arg.Value is IRCallExpr { Callee: IRIdentExpr { Name: var stubN } } &&
                         (_externStubs?.Any(s => s.Name == stubN && s.ReturnType == "interface{}") ?? false))
                {
                    valExpr = $"{valExpr}.({expectedGoType})";
                }
                // If field expects uint* and value is len()/dlugosc() call, wrap with uint*(...)
                else if (arg.Value is IRCallExpr { Callee: IRIdentExpr { Name: "len" or "dlugosc" } })
                {
                    if (expectedGoType is "uint32" or "uint64" or "uint8" or "uint16")
                        valExpr = $"{expectedGoType}({valExpr})";
                }
                return $"{PascalCase(arg.Name!)}: {valExpr}";
            }));
            return $"{sName}{{{fields}}}";
        }

        var args = string.Join(", ", callExpr.Args.Select(arg => EmitExpr(arg.Value)));
        return $"{EmitExpr(callExpr.Callee)}({args})";
    }

    private static string EmitTernary(IRTernaryExpr t)
    {
        // Try to infer a concrete return type from the branch expressions
        var goType = InferGoTypeFromExpr(t.Then) ?? InferGoTypeFromExpr(t.Else);
        if (goType is not null)
            return $"func() {goType} {{ if {EmitExpr(t.Cond)} {{ return {EmitExpr(t.Then)} }}; return {EmitExpr(t.Else)} }}()";
        return $"func() interface{{}} {{ if {EmitExpr(t.Cond)} {{ return {EmitExpr(t.Then)} }}; return {EmitExpr(t.Else)} }}()";
    }

    private static string? InferGoTypeFromExpr(IRExpr expr) => expr switch
    {
        IRLiteralExpr { Value: IRTextValue }  => "string",
        IRLiteralExpr { Value: IRBoolValue }  => "bool",
        // Don't infer numeric types from literals: float/int literals may need to match
        // float32, uint32, etc. — we'd need context type to pick correctly.
        _ => null
    };

    private static bool NeedsFloatCast(IRExpr left, IRExpr right)
    {
        if (_stateVarTypes is null) return false;
        static IRType? TypeOf(IRExpr e, Dictionary<string, IRType> types)
            => e is IRIdentExpr { Name: var n } && types.TryGetValue(n, out var t) ? t : null;
        var leftType = TypeOf(left, _stateVarTypes);
        var rightType = TypeOf(right, _stateVarTypes);
        if (leftType is null || rightType is null) return false;
        var leftIsFloat = leftType is IRNamedType { Name: "Float" or "f32" or "f64" };
        var rightIsUint = rightType is IRNamedType { Name: "u32" or "u64" or "u8" or "u16" or "Int" };
        return leftIsFloat && rightIsUint;
    }

    private static bool IsTimeExpr(IRExpr expr) =>
        expr is IRCallExpr { Callee: IRIdentExpr { Name: "now" } };

    private static string EmitValue(IRValue value) => value switch
    {
        IRIntValue intValue => intValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        IRFloatValue floatValue => floatValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        IRBoolValue boolValue => boolValue.Value ? "true" : "false",
        IRTextValue textValue => EmitInterpolatedString(textValue.Value),
        IRUnitValue unitValue => EmitUnitValue(unitValue),
        IRNullValue => "nil",
        _ => "nil"
    };

    private static string EmitDuration(IRExpr expr)
    {
        if (expr is IRLiteralExpr { Value: IRUnitValue unitValue })
            return EmitDurationLiteral(unitValue);
        // Duration literal parsed as text (e.g. "1ms" from parser fallback path)
        if (expr is IRLiteralExpr { Value: IRTextValue { Value: var raw } })
        {
            var unitIdx = 0;
            while (unitIdx < raw.Length && (char.IsDigit(raw[unitIdx]) || raw[unitIdx] == '.'))
                unitIdx++;
            if (unitIdx > 0 && unitIdx < raw.Length
                && double.TryParse(raw[..unitIdx], System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var mag))
                return EmitDurationLiteral(new IRUnitValue(mag, raw[unitIdx..]));
        }
        return EmitExpr(expr);
    }

    private static string EmitUnitValue(IRUnitValue unitValue)
        => unitValue.Unit switch
        {
            "ms" or "s" or "m" or "h" => EmitDurationLiteral(unitValue),
            _ => unitValue.Magnitude.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

    private static string EmitDurationLiteral(IRUnitValue unitValue)
    {
        var magnitude = unitValue.Magnitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return unitValue.Unit switch
        {
            "ms" => $"{magnitude} * time.Millisecond",
            "s" => $"{magnitude} * time.Second",
            "m" => $"{magnitude} * time.Minute",
            "h" => $"{magnitude} * time.Hour",
            _ => magnitude
        };
    }

    private static string EscapeString(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    // ─── String interpolation ────────────────────────────────────────────────

    /// <summary>
    /// Regex dopasowuje wzorce interpolacji: {expr} lub {expr:format_spec}.
    /// Przykłady: {level}, {r.celsius:.1f}, {count:d}
    /// </summary>
    private static readonly Regex InterpolationRegex =
        new(@"\{([^{}:]+)(?::([^{}]*))?\}", RegexOptions.Compiled);

    /// <summary>
    /// Emituje wartość tekstową. Jeśli zawiera wzorce {expr} lub {expr:fmt},
    /// generuje wywołanie fmt.Sprintf. W przeciwnym razie: zwykły string literal.
    /// </summary>
    private static string EmitInterpolatedString(string raw)
    {
        var matches = InterpolationRegex.Matches(raw);
        if (matches.Count == 0)
            return $"\"{EscapeString(raw)}\"";

        var formatSb = new StringBuilder();
        var args = new List<string>();
        int lastPos = 0;

        foreach (Match m in matches)
        {
            // Dodaj fragment literalny przed interpolacją
            formatSb.Append(EscapeFormatString(raw[lastPos..m.Index]));

            var exprPart  = m.Groups[1].Value.Trim();
            var fmtSpec   = m.Groups[2].Success ? m.Groups[2].Value : null;

            formatSb.Append(ConvertFormatSpec(fmtSpec));
            args.Add(ConvertInterpolationExpr(exprPart));

            lastPos = m.Index + m.Length;
        }

        // Fragment literalny po ostatniej interpolacji
        formatSb.Append(EscapeFormatString(raw[lastPos..]));

        var argsStr = string.Join(", ", args);
        return $"fmt.Sprintf(\"{formatSb}\", {argsStr})";
    }

    /// <summary>Escape dla stringu formatu fmt.Sprintf: \, ", %, \n, \t.</summary>
    private static string EscapeFormatString(string s)
        => s.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal)
             .Replace("%",  "%%",    StringComparison.Ordinal)
             .Replace("\n", "\\n",   StringComparison.Ordinal)
             .Replace("\r", "\\r",   StringComparison.Ordinal)
             .Replace("\t", "\\t",   StringComparison.Ordinal);

    /// <summary>
    /// Konwertuje specyfikator formatu JunexisLang na Go: "" → "%v", ".1f" → "%.1f", "d" → "%d".
    /// </summary>
    private static string ConvertFormatSpec(string? spec)
    {
        if (string.IsNullOrEmpty(spec)) return "%v";
        return spec.StartsWith('.') ? $"%.{spec[1..]}" : $"%{spec}";
    }

    /// <summary>
    /// Konwertuje wyrażenie interpolacji do Go. Pola obiektów (x.field) dostają PascalCase.
    /// Przykłady: "level" → "level", "r.celsius" → "r.Celsius".
    /// </summary>
    private static string ConvertInterpolationExpr(string expr)
    {
        var dotIdx = expr.IndexOf('.', StringComparison.Ordinal);
        if (dotIdx >= 0)
            return expr[..dotIdx] + "." + PascalCase(expr[(dotIdx + 1)..]);
        // If the identifier is a config field, redirect to config.Xxx
        if (_configVarNames != null && _configVarNames.Contains(expr))
            return $"config.{PascalCase(expr)}";
        return expr;
    }

    /// <summary>Sprawdza, czy task zawiera stringi z interpolacją {expr}.</summary>
    private static bool TaskHasStringInterpolation(IRTask task)
        => StmtListHasInterpolation(task.Body) ||
           task.Hooks.Any(h => StmtListHasInterpolation(h.Body));

    private static bool StmtListHasInterpolation(IReadOnlyList<IRStmt> stmts)
        => stmts.Any(StmtHasInterpolation);

    private static bool StmtHasInterpolation(IRStmt stmt) => stmt switch
    {
        IRLetStmt s  => ExprHasInterpolation(s.Init),
        IRLogStmt s  => s.Args.Any(ExprHasInterpolation),
        IRExprStmt s => ExprHasInterpolation(s.Expr),
        IRReturnStmt s => s.Value is not null && ExprHasInterpolation(s.Value),
        IRSendStmt s => ExprHasInterpolation(s.Value) || (s.Target is not null && ExprHasInterpolation(s.Target)),
        IRIfStmt s   => ExprHasInterpolation(s.Condition) || StmtListHasInterpolation(s.Then) || ElseHasInterpolation(s.Else),
        IRLoopStmt s => StmtListHasInterpolation(s.Body),
        IRForStmt s  => ExprHasInterpolation(s.Iter) || StmtListHasInterpolation(s.Body),
        IREveryStmt s => StmtListHasInterpolation(s.Body),
        IRMatchStmt s => ExprHasInterpolation(s.Subject) || s.Arms.Any(a => StmtListHasInterpolation(a.Body)),
        _ => false
    };

    private static bool ElseHasInterpolation(IRElse? e) => e switch
    {
        IRElseIf ei => StmtHasInterpolation(ei.IfStmt) || ElseHasInterpolation(ei.IfStmt.Else),
        IRElseBlock eb => StmtListHasInterpolation(eb.Body),
        _ => false
    };

    private static bool ExprHasInterpolation(IRExpr expr) => expr switch
    {
        IRLiteralExpr { Value: IRTextValue tv } => InterpolationRegex.IsMatch(tv.Value),
        IRBinaryExpr e  => ExprHasInterpolation(e.Left) || ExprHasInterpolation(e.Right),
        IRUnaryExpr e   => ExprHasInterpolation(e.Operand),
        IRTernaryExpr e => ExprHasInterpolation(e.Cond) || ExprHasInterpolation(e.Then) || ExprHasInterpolation(e.Else),
        IRCallExpr e    => e.Args.Any(a => ExprHasInterpolation(a.Value)),
        IRFieldAccessExpr e => ExprHasInterpolation(e.Object),
        IRIndexExpr e   => ExprHasInterpolation(e.Object) || ExprHasInterpolation(e.Index),
        _ => false
    };

    private static bool ContainsReturn(IReadOnlyList<IRStmt> statements)
        => statements.Any(statement => statement switch
        {
            IRReturnStmt => true,
            IRIfStmt ifStmt => ContainsReturn(ifStmt.Then) || ContainsReturn(ifStmt.Else),
            IREveryStmt everyStmt => ContainsReturn(everyStmt.Body),
            IRLoopStmt loopStmt => ContainsReturn(loopStmt.Body),
            IRForStmt forStmt => ContainsReturn(forStmt.Body),
            IRMatchStmt matchStmt => matchStmt.Arms.Any(arm => ContainsReturn(arm.Body)),
            _ => false,
        });

    private static bool ContainsReturn(IRElse? elseBranch)
        => elseBranch switch
        {
            IRElseIf elseIf => ContainsReturn(elseIf.IfStmt.Then) || ContainsReturn(elseIf.IfStmt.Else),
            IRElseBlock elseBlock => ContainsReturn(elseBlock.Body),
            _ => false,
        };

    /// <summary>
    /// Detects back-edges (cycle-closing edges) in the pipeline connection graph using DFS.
    /// Returns the set of (Source, Target) pairs that form back-edges.
    /// </summary>
    private static HashSet<(string Source, string Target)> DetectCycleEdges(
        IReadOnlyList<IRPipeline> pipelines,
        IEnumerable<string> platformTaskNames)
    {
        var taskSet = new HashSet<string>(platformTaskNames);
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
        var color = new Dictionary<string, int>(); // 0=white, 1=gray (in stack), 2=black

        void Dfs(string node)
        {
            color[node] = 1;
            if (adj.TryGetValue(node, out var neighbors))
            {
                foreach (var next in neighbors)
                {
                    if (!color.TryGetValue(next, out var c) || c == 0)
                    {
                        if (!color.ContainsKey(next)) color[next] = 0;
                        Dfs(next);
                    }
                    else if (c == 1)
                    {
                        backEdges.Add((node, next));
                    }
                }
            }
            color[node] = 2;
        }

        foreach (var node in taskSet)
        {
            if (!color.TryGetValue(node, out var c) || c == 0)
                Dfs(node);
        }

        return backEdges;
    }

    private static IEnumerable<IRPipelineConn> FlattenConnections(IReadOnlyList<IRPipelineConn> conns)
    {
        foreach (var conn in conns)
        {
            if (conn is IRParallelBlock parallel)
                foreach (var inner in FlattenConnections(parallel.Connections))
                    yield return inner;
            else
                yield return conn;
        }
    }

    private static void EmitRestProtocolBindings(
        StringBuilder sb,
        IReadOnlyList<ProtocolBinding> bindings,
        IReadOnlyList<IRTask> tasks)
    {
        sb.AppendLine("// registerProtocolRoutes binds protocol declarations to domain tasks.");
        sb.AppendLine("func registerProtocolRoutes() {");
        sb.AppendLine("\t_ = strconv.IntSize // REST scalar decoder capability");
        foreach (var binding in bindings)
        {
            if (binding.Method is null || binding.Path is null)
                continue;
            var task = tasks.Single(candidate => candidate.Name == binding.Task);
            var path = NormalizeRestPath(binding.Path);
            sb.AppendLine($"\thttp.HandleFunc(\"{binding.Method} {EscapeGoString(path)}\", func(w http.ResponseWriter, r *http.Request) {{");
            foreach (var parameter in task.Params)
                EmitRestParameterBinding(sb, parameter, binding, path);

            var arguments = string.Join(", ", task.Params.Select(parameter => parameter.Name));
            if (task.OutputType is null)
            {
                sb.AppendLine($"\t\t{task.Name}({arguments})");
                sb.AppendLine("\t\tw.WriteHeader(http.StatusNoContent)");
            }
            else
            {
                sb.AppendLine($"\t\tresult := {task.Name}({arguments})");
                sb.AppendLine("\t\tw.Header().Set(\"Content-Type\", \"application/json\")");
                sb.AppendLine("\t\tif err := json.NewEncoder(w).Encode(result); err != nil {");
                sb.AppendLine("\t\t\thttp.Error(w, err.Error(), http.StatusInternalServerError)");
                sb.AppendLine("\t\t}");
            }
            sb.AppendLine("\t})");
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitRestParameterBinding(
        StringBuilder sb,
        IRParam parameter,
        ProtocolBinding binding,
        string normalizedPath)
    {
        var goType = TypeMappings.ToGo(parameter.Type);
        var source = binding.ParameterSources.TryGetValue(parameter.Name, out var explicitSource)
            ? explicitSource
            : normalizedPath.Contains($"{{{parameter.Name}}}", StringComparison.Ordinal) ? "path" : "query";
        if (source == "body" || parameter.Type is not IRNamedType)
        {
            sb.AppendLine($"\t\tvar {parameter.Name} {goType}");
            sb.AppendLine($"\t\tif err := json.NewDecoder(r.Body).Decode(&{parameter.Name}); err != nil {{");
            sb.AppendLine("\t\t\thttp.Error(w, err.Error(), http.StatusBadRequest)");
            sb.AppendLine("\t\t\treturn");
            sb.AppendLine("\t\t}");
            return;
        }

        var raw = source switch
        {
            "path" => $"r.PathValue(\"{parameter.Name}\")",
            "header" => $"r.Header.Get(\"{parameter.Name}\")",
            _ => $"r.URL.Query().Get(\"{parameter.Name}\")"
        };
        var typeName = ((IRNamedType)parameter.Type).Name;
        switch (typeName)
        {
            case "Text" or "Symbol":
                sb.AppendLine($"\t\t{parameter.Name} := {raw}");
                break;
            case "Bool":
                EmitParsedRestParameter(sb, parameter.Name, $"strconv.ParseBool({raw})", null);
                break;
            case "Float" or "f64":
                EmitParsedRestParameter(sb, parameter.Name, $"strconv.ParseFloat({raw}, 64)", null);
                break;
            case "f32":
                EmitParsedRestParameter(sb, parameter.Name, $"strconv.ParseFloat({raw}, 32)", "float32");
                break;
            case "u8" or "u16" or "u32" or "u64":
                EmitParsedRestParameter(sb, parameter.Name, $"strconv.ParseUint({raw}, 10, {typeName[1..]})", TypeMappings.ToGo(parameter.Type));
                break;
            case "i8" or "i16" or "i32" or "i64" or "Int":
                var bits = typeName == "Int" ? "64" : typeName[1..];
                EmitParsedRestParameter(sb, parameter.Name, $"strconv.ParseInt({raw}, 10, {bits})", TypeMappings.ToGo(parameter.Type));
                break;
            default:
                sb.AppendLine($"\t\tvar {parameter.Name} {goType}");
                sb.AppendLine($"\t	if err := json.Unmarshal([]byte({raw}), &{parameter.Name}); err != nil {{");
                sb.AppendLine("\t\t\thttp.Error(w, err.Error(), http.StatusBadRequest)");
                sb.AppendLine("\t\t\treturn");
                sb.AppendLine("\t\t}");
                break;
        }
    }

    private static void EmitParsedRestParameter(StringBuilder sb, string name, string expression, string? cast)
    {
        sb.AppendLine($"\t\t{name}Value, {name}Err := {expression}");
        sb.AppendLine($"\t\tif {name}Err != nil {{");
        sb.AppendLine($"\t\t\thttp.Error(w, {name}Err.Error(), http.StatusBadRequest)");
        sb.AppendLine("\t\t\treturn");
        sb.AppendLine("\t\t}");
        sb.AppendLine(cast is null
            ? $"\t\t{name} := {name}Value"
            : $"\t\t{name} := {cast}({name}Value)");
    }

    private static string GenerateOpenApi(IReadOnlyList<ProtocolBinding> bindings)
    {
        var paths = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        foreach (var binding in bindings.Where(binding => binding.Method is not null && binding.Path is not null))
        {
            var path = NormalizeRestPath(binding.Path!);
            if (!paths.TryGetValue(path, out var operations))
                paths[path] = operations = new Dictionary<string, object>(StringComparer.Ordinal);
            operations[binding.Method!.ToLowerInvariant()] = new
            {
                operationId = binding.Task,
                responses = new Dictionary<string, object> { ["200"] = new { description = "Junexis task response" } }
            };
        }
        var document = new
        {
            openapi = "3.0.3",
            info = new { title = "Generated Junexis API", version = "0.2.0" },
            paths
        };
        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string NormalizeRestPath(string path) => Regex.Replace(
        path,
        @"\{([A-Za-z_][A-Za-z0-9_]*):[^}]+\}",
        "{$1}").Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string EscapeGoString(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>
    /// Generates a <c>go.mod</c> file for the server package with only the dependencies
    /// required by enabled transports and storage providers.
    /// </summary>
    private static string GenerateGoMod(bool includeWebSocketBridgeDependencies)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// go.mod — generated by JunexisLang compiler");
        sb.AppendLine("module junexis/server");
        sb.AppendLine();
        sb.AppendLine("go 1.22");
        if (includeWebSocketBridgeDependencies)
        {
            sb.AppendLine();
            sb.AppendLine("require (");
            if (includeWebSocketBridgeDependencies)
            {
                sb.AppendLine("\tgithub.com/gorilla/websocket v1.5.3");
                sb.AppendLine("\tgithub.com/shamaton/msgpack/v2 v2.2.2");
            }
            sb.AppendLine(")");
        }
        return sb.ToString();
    }

    private static void EmitQueueType(StringBuilder sb)
    {
        sb.AppendLine("// RecvResult carries the outcome of a Queue.Recv call.");
        sb.AppendLine("type RecvResult[T any] struct {");
        sb.AppendLine("\tOk    bool");
        sb.AppendLine("\tValue T");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("type queueItem[T any] struct {");
        sb.AppendLine("\tid    string");
        sb.AppendLine("\tvalue T");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// Queue is a runtime-managed message queue with at-least-once delivery semantics.");
        sb.AppendLine("type Queue[T any] struct {");
        sb.AppendLine("\tmu     sync.Mutex");
        sb.AppendLine("\titems  []queueItem[T]");
        sb.AppendLine("\tnextId uint64");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// NewQueue creates an empty Queue.");
        sb.AppendLine("func NewQueue[T any]() *Queue[T] { return &Queue[T]{} }");
        sb.AppendLine();
        sb.AppendLine("// Enqueue adds a value to the queue.");
        sb.AppendLine("func (q *Queue[T]) Enqueue(v T) {");
        sb.AppendLine("\tq.mu.Lock()");
        sb.AppendLine("\tdefer q.mu.Unlock()");
        sb.AppendLine("\tq.nextId++");
        sb.AppendLine("\tq.items = append(q.items, queueItem[T]{id: strconv.FormatUint(q.nextId, 10), value: v})");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// Recv dequeues the head item without removing it until Ack is called.");
        sb.AppendLine("func (q *Queue[T]) Recv() (RecvResult[T], *string) {");
        sb.AppendLine("\tq.mu.Lock()");
        sb.AppendLine("\tdefer q.mu.Unlock()");
        sb.AppendLine("\tif len(q.items) == 0 {");
        sb.AppendLine("\t\treturn RecvResult[T]{Ok: false}, nil");
        sb.AppendLine("\t}");
        sb.AppendLine("\titem := q.items[0]");
        sb.AppendLine("\treturn RecvResult[T]{Ok: true, Value: item.value}, &item.id");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// Ack removes the acknowledged item.");
        sb.AppendLine("func (q *Queue[T]) Ack(messageId string) {");
        sb.AppendLine("\tq.mu.Lock()");
        sb.AppendLine("\tdefer q.mu.Unlock()");
        sb.AppendLine("\tfor i, item := range q.items {");
        sb.AppendLine("\t\tif item.id == messageId {");
        sb.AppendLine("\t\t\tq.items = append(q.items[:i], q.items[i+1:]...)");
        sb.AppendLine("\t\t\treturn");
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// Nack signals delivery failure; the item remains in the queue.");
        sb.AppendLine("func (q *Queue[T]) Nack(_ string, _ string) {}");
        sb.AppendLine();
        sb.AppendLine("// Batch returns a snapshot of up to limit queued values without ACKing them.");
        sb.AppendLine("func (q *Queue[T]) Batch(limit int64) []T {");
        sb.AppendLine("\tq.mu.Lock()");
        sb.AppendLine("\tdefer q.mu.Unlock()");
        sb.AppendLine("\tif limit <= 0 || len(q.items) == 0 {");
        sb.AppendLine("\t\treturn []T{}");
        sb.AppendLine("\t}");
        sb.AppendLine("\tcount := int(limit)");
        sb.AppendLine("\tif count > len(q.items) {");
        sb.AppendLine("\t\tcount = len(q.items)");
        sb.AppendLine("\t}");
        sb.AppendLine("\tvalues := make([]T, 0, count)");
        sb.AppendLine("\tfor _, item := range q.items[:count] {");
        sb.AppendLine("\t\tvalues = append(values, item.value)");
        sb.AppendLine("\t}");
        sb.AppendLine("\treturn values");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitStreamType(StringBuilder sb)
    {
        sb.AppendLine("// Stream is a runtime-managed broadcast stream.");
        sb.AppendLine("type Stream[T any] struct {");
        sb.AppendLine("\tmu        sync.Mutex");
        sb.AppendLine("\tlatest    T");
        sb.AppendLine("\thasLatest bool");
        sb.AppendLine("\tversion   uint64");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// StreamSubscription tracks an independent latest-wins observation cursor.");
        sb.AppendLine("type StreamSubscription[T any] struct {");
        sb.AppendLine("\tstream *Stream[T]");
        sb.AppendLine("\tseen   uint64");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// NewStream creates an empty Stream.");
        sb.AppendLine("func NewStream[T any]() *Stream[T] { return &Stream[T]{} }");
        sb.AppendLine();
        sb.AppendLine("// Publish broadcasts a new value to the stream.");
        sb.AppendLine("func (s *Stream[T]) Publish(v T) {");
        sb.AppendLine("\ts.mu.Lock()");
        sb.AppendLine("\tdefer s.mu.Unlock()");
        sb.AppendLine("\ts.latest = v");
        sb.AppendLine("\ts.hasLatest = true");
        sb.AppendLine("\ts.version++");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// HasLatest returns true if at least one value has been published.");
        sb.AppendLine("func (s *Stream[T]) HasLatest() bool {");
        sb.AppendLine("\ts.mu.Lock()");
        sb.AppendLine("\tdefer s.mu.Unlock()");
        sb.AppendLine("\treturn s.hasLatest");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// Latest returns the most recently published value.");
        sb.AppendLine("func (s *Stream[T]) Latest() T {");
        sb.AppendLine("\ts.mu.Lock()");
        sb.AppendLine("\tdefer s.mu.Unlock()");
        sb.AppendLine("\treturn s.latest");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// Subscribe creates a cursor that does not replay values published before subscription.");
        sb.AppendLine("func (s *Stream[T]) Subscribe() *StreamSubscription[T] {");
        sb.AppendLine("\ts.mu.Lock()");
        sb.AppendLine("\tdefer s.mu.Unlock()");
        sb.AppendLine("\treturn &StreamSubscription[T]{stream: s, seen: s.version}");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// Poll returns the newest value once for this subscriber, skipping overwritten history.");
        sb.AppendLine("func (subscription *StreamSubscription[T]) Poll() (T, bool) {");
        sb.AppendLine("\tstream := subscription.stream");
        sb.AppendLine("\tstream.mu.Lock()");
        sb.AppendLine("\tdefer stream.mu.Unlock()");
        sb.AppendLine("\tif !stream.hasLatest || stream.version <= subscription.seen {");
        sb.AppendLine("\t\tvar zero T");
        sb.AppendLine("\t\treturn zero, false");
        sb.AppendLine("\t}");
        sb.AppendLine("\tsubscription.seen = stream.version");
        sb.AppendLine("\treturn stream.latest, true");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static bool TaskHasEveryStmt(IRTask task)        => StmtListHasEveryStmt(task.Body) || task.Hooks.Any(h => StmtListHasEveryStmt(h.Body));

    private static bool TaskHasExplicitSendTarget(IRTask task)
        => StmtListHasExplicitSendTarget(task.Body) || task.Hooks.Any(h => StmtListHasExplicitSendTarget(h.Body));

    private static bool TaskHasSendStatement(IRTask task)
        => StmtListHasSendStatement(task.Body) || task.Hooks.Any(hook => StmtListHasSendStatement(hook.Body));

    private static bool StmtListHasSendStatement(IReadOnlyList<IRStmt> statements)
        => statements.Any(StmtHasSendStatement);

    private static bool StmtHasSendStatement(IRStmt statement) => statement switch
    {
        IRSendStmt => true,
        IRIfStmt conditional => StmtListHasSendStatement(conditional.Then)
            || (conditional.Else is IRElseIf elseIf && StmtHasSendStatement(elseIf.IfStmt))
            || (conditional.Else is IRElseBlock elseBlock && StmtListHasSendStatement(elseBlock.Body)),
        IREveryStmt every => StmtListHasSendStatement(every.Body),
        IRLoopStmt loop => StmtListHasSendStatement(loop.Body),
        IRForStmt loop => StmtListHasSendStatement(loop.Body),
        IRMatchStmt match => match.Arms.Any(arm => StmtListHasSendStatement(arm.Body)),
        _ => false,
    };

    private static bool StmtListHasExplicitSendTarget(IReadOnlyList<IRStmt> stmts)
        => stmts.Any(StmtHasExplicitSendTarget);

    private static bool StmtHasExplicitSendTarget(IRStmt stmt) => stmt switch
    {
        IRSendStmt { Target: not null } => true,
        IRLetStmt s => ExprHasExplicitSendTarget(s.Init),
        IRExprStmt s => ExprHasExplicitSendTarget(s.Expr),
        IRIfStmt s => StmtListHasExplicitSendTarget(s.Then)
            || (s.Else is IRElseIf ei && StmtHasExplicitSendTarget(ei.IfStmt))
            || (s.Else is IRElseBlock eb && StmtListHasExplicitSendTarget(eb.Body)),
        IREveryStmt s => StmtListHasExplicitSendTarget(s.Body),
        IRLoopStmt s => StmtListHasExplicitSendTarget(s.Body),
        IRForStmt s => StmtListHasExplicitSendTarget(s.Body),
        IRMatchStmt s => s.Arms.Any(a => StmtListHasExplicitSendTarget(a.Body) || (a.BodyExpr is not null && ExprHasExplicitSendTarget(a.BodyExpr))),
        _ => false
    };

    private static bool ExprHasExplicitSendTarget(IRExpr expr) => expr switch
    {
        IRSendExpr { Target: not null } => true,
        IRCallExpr c => c.Args.Any(a => ExprHasExplicitSendTarget(a.Value)),
        IRBinaryExpr b => ExprHasExplicitSendTarget(b.Left) || ExprHasExplicitSendTarget(b.Right),
        IRUnaryExpr u => ExprHasExplicitSendTarget(u.Operand),
        IRTernaryExpr t => ExprHasExplicitSendTarget(t.Cond) || ExprHasExplicitSendTarget(t.Then) || ExprHasExplicitSendTarget(t.Else),
        IRFieldAccessExpr f => ExprHasExplicitSendTarget(f.Object),
        IRIndexExpr i => ExprHasExplicitSendTarget(i.Object) || ExprHasExplicitSendTarget(i.Index),
        IRAwaitExpr a => ExprHasExplicitSendTarget(a.Inner),
        _ => false
    };

    private static bool TaskBodyContainsCall(IReadOnlyList<IRStmt> stmts, string callee)
        => stmts.Any(s => StmtContainsCall(s, callee));

    private static bool TaskContainsCall(IRTask task, string callee)
        => TaskBodyContainsCall(task.Body, callee)
            || task.Hooks.Any(hook => TaskBodyContainsCall(hook.Body, callee));

    private static bool StmtContainsCall(IRStmt stmt, string callee) => stmt switch
    {
        IRLetStmt s => ExprContainsCall(s.Init, callee),
        IRExprStmt s => ExprContainsCall(s.Expr, callee),
        IRReturnStmt { Value: not null } s => ExprContainsCall(s.Value, callee),
        IRLogStmt s => s.Args.Any(argument => ExprContainsCall(argument, callee)),
        IRSendStmt s => ExprContainsCall(s.Value, callee),
        IRRecvStmt s => ExprContainsCall(s.Source, callee),
        IRRespondStmt s => ExprContainsCall(s.Value, callee),
        IRIfStmt s => ExprContainsCall(s.Condition, callee) || TaskBodyContainsCall(s.Then, callee)
            || (s.Else is IRElseIf ei && StmtContainsCall(ei.IfStmt, callee))
            || (s.Else is IRElseBlock eb && TaskBodyContainsCall(eb.Body, callee)),
        IREveryStmt s => TaskBodyContainsCall(s.Body, callee),
        IRLoopStmt s => TaskBodyContainsCall(s.Body, callee),
        IRForStmt s => TaskBodyContainsCall(s.Body, callee),
        IRMatchStmt s => ExprContainsCall(s.Subject, callee)
            || s.Arms.Any(a => TaskBodyContainsCall(a.Body, callee)),
        _ => false
    };

    private static bool ExprContainsCall(IRExpr expr, string callee) => expr switch
    {
        IRCallExpr c => (c.Callee is IRIdentExpr { Name: var n } && n == callee)
            || c.Args.Any(a => ExprContainsCall(a.Value, callee)),
        IRBinaryExpr b => ExprContainsCall(b.Left, callee) || ExprContainsCall(b.Right, callee),
        IRUnaryExpr u => ExprContainsCall(u.Operand, callee),
        IRTernaryExpr t => ExprContainsCall(t.Cond, callee) || ExprContainsCall(t.Then, callee) || ExprContainsCall(t.Else, callee),
        _ => false
    };

    private static bool StmtListHasEveryStmt(IReadOnlyList<IRStmt> stmts)
        => stmts.Any(s => s is IREveryStmt || (s is IRIfStmt ifs && (StmtListHasEveryStmt(ifs.Then) || ElseHasEveryStmt(ifs.Else))));

    private static bool ElseHasEveryStmt(IRElse? e) => e switch
    {
        IRElseIf ei => StmtListHasEveryStmt([ei.IfStmt]) || ElseHasEveryStmt(ei.IfStmt.Else),
        IRElseBlock eb => StmtListHasEveryStmt(eb.Body),
        _ => false,
    };

    private static string PascalCase(string s)
        => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string ToLocalIdentifier(string s)
        => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
