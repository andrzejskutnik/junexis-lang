using System.Collections;
using System.Reflection;
using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.IR;

internal static class IRSourceMapBuilder
{
    internal static IReadOnlyList<IRSourceMapEntry> Build(ProgramNode program)
    {
        var entries = new List<IRSourceMapEntry>();
        Add(entries, "/", program);

        for (var i = 0; i < program.Imports.Count; i++)
            AddTree(entries, $"/Imports[{i}]", program.Imports[i]);

        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var declaration in program.Declarations)
        {
            var collection = CollectionName(declaration);
            counters.TryGetValue(collection, out var index);
            counters[collection] = index + 1;
            AddDeclarationTree(entries, $"/{collection}[{index}]", declaration);
        }

        return entries;
    }

    private static void AddDeclarationTree(List<IRSourceMapEntry> entries, string path, AstNode declaration)
    {
        switch (declaration)
        {
            case TaskDeclarationNode task:
                Add(entries, path, task);
                AddSequence(entries, $"{path}/Params", task.Params);
                AddSequence(entries, $"{path}/Platforms", task.Platforms);
                if (task.OutputType is not null)
                    AddTree(entries, $"{path}/OutputType", task.OutputType);
                AddSequence(entries, $"{path}/States", task.Body.OfType<StateDeclarationNode>());
                AddSequence(entries, $"{path}/Hooks", task.Body.OfType<LifecycleHookNode>());
                AddSequence(entries, $"{path}/Body", task.Body.OfType<StmtNode>());
                var taskConfig = task.Body.OfType<ConfigBlockNode>().FirstOrDefault();
                if (taskConfig is not null)
                    AddTree(entries, $"{path}/Config", taskConfig);
                var taskNative = task.Body.OfType<NativeSectionNode>().FirstOrDefault();
                if (taskNative is not null)
                    AddTree(entries, $"{path}/Native", taskNative);
                return;

            case DeviceDeclarationNode device:
                Add(entries, path, device);
                AddTree(entries, $"{path}/Platform", device.Platform);
                AddSequence(entries, $"{path}/Blocks", device.Members.OfType<DeviceBlockNode>());
                var deviceConfig = device.Members.OfType<ConfigBlockNode>().FirstOrDefault();
                if (deviceConfig is not null)
                    AddTree(entries, $"{path}/Config", deviceConfig);
                var deviceNative = device.Members.OfType<NativeSectionNode>().FirstOrDefault();
                if (deviceNative is not null)
                    AddTree(entries, $"{path}/Native", deviceNative);
                return;

            default:
                AddTree(entries, path, declaration);
                return;
        }
    }

    private static void AddSequence<T>(List<IRSourceMapEntry> entries, string path, IEnumerable<T> nodes)
        where T : AstNode
    {
        var index = 0;
        foreach (var node in nodes)
            AddTree(entries, $"{path}[{index++}]", node);
    }

    private static void AddTree(List<IRSourceMapEntry> entries, string path, AstNode node)
    {
        Add(entries, path, node);

        foreach (var property in node.GetType()
                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            if (property.Name is nameof(AstNode.File) or nameof(AstNode.Line) or nameof(AstNode.Column))
                continue;

            var value = property.GetValue(node);
            if (value is AstNode child)
            {
                AddTree(entries, $"{path}/{property.Name}", child);
                continue;
            }

            if (value is not IEnumerable sequence || value is string)
                continue;

            var childIndex = 0;
            foreach (var item in sequence)
            {
                if (item is AstNode childNode)
                    AddTree(entries, $"{path}/{property.Name}[{childIndex}]", childNode);
                childIndex++;
            }
        }
    }

    private static void Add(List<IRSourceMapEntry> entries, string path, AstNode node) =>
        entries.Add(new IRSourceMapEntry(path, new IRSourceSpan(node.File, node.Line, node.Column)));

    private static string CollectionName(AstNode declaration) => declaration switch
    {
        StructDeclarationNode => "Structs",
        EnumDeclarationNode => "Enums",
        FnDeclarationNode => "Functions",
        TaskDeclarationNode => "Tasks",
        PipelineDeclarationNode => "Pipelines",
        DeviceDeclarationNode => "Devices",
        NarrativeDeclarationNode => "Narratives",
        DeploymentDeclarationNode => "Deployments",
        SupervisorDeclarationNode => "Supervisors",
        QuirksDeclarationNode => "Quirks",
        CircuitBreakerDeclarationNode => "CircuitBreakers",
        ProtocolDeclarationNode => "Protocols",
        ExternTaskNode => "ExternTasks",
        ExternTypeNode => "ExternTypes",
        ExternContractNode => "ExternContracts",
        StorageDeclarationNode => "StorageDeclarations",
        _ => "UnsupportedDeclarations"
    };
}
