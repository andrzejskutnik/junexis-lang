using JunexisLang.Compiler.Core.Ast;

namespace JunexisLang.Compiler.Tests.Parser;

/// <summary>Smoke-testy parsowania deklaracji i podstawowych konstrukcji AST.</summary>
public sealed partial class ParserTests
{
    [Fact]
    public async Task EmptyProgram_ParsesOk()
    {
        var ast = await Parse("");
        Assert.Empty(ast.Declarations);
    }

    [Fact]
    public async Task Import_Simple_ParsesOk()
    {
        var ast = await Parse("import stdlib");
        var imp = Assert.IsType<ImportNode>(ast.Imports.Single());
        Assert.Equal("stdlib", imp.ModulePath);
        Assert.Null(imp.Alias);
        Assert.Empty(imp.Symbols);
    }

    [Fact]
    public async Task Import_WithAlias_ParsesOk()
    {
        var ast = await Parse("import stdlib as std");
        var imp = ast.Imports.Single();
        Assert.Equal("std", imp.Alias);
    }

    [Fact]
    public async Task Import_WithSymbols_ParsesOk()
    {
        var ast = await Parse("import stdlib { Int, Float as F }");
        var imp = ast.Imports.Single();
        Assert.Equal(2, imp.Symbols.Count);
        Assert.Equal("Int", imp.Symbols[0].Name);
        Assert.Equal("Float", imp.Symbols[1].Name);
        Assert.Equal("F", imp.Symbols[1].Alias);
    }

    [Fact]
    public async Task Task_MinimalEmpty_ParsesOk()
    {
        var ast = await Parse("task Foo() { }");
        var task = Assert.IsType<TaskDeclarationNode>(ast.Declarations.Single());
        Assert.Equal("Foo", task.Name);
        Assert.Empty(task.Params);
        Assert.Empty(task.Platforms);
        Assert.Null(task.OutputType);
        Assert.Empty(task.Body);
    }

    [Fact]
    public async Task Task_WithPlatformAndOutput_ParsesOk()
    {
        const string src = "task Sensor() @embedded -> chan<Float> { }";
        var ast = await Parse(src);
        var task = Assert.IsType<TaskDeclarationNode>(ast.Declarations.Single());
        Assert.Single(task.Platforms);
        Assert.Equal("embedded", task.Platforms[0].Target);

        var outType = Assert.IsType<CommTypeNode>(task.OutputType);
        Assert.Equal(CommKind.Chan, outType.Kind);
        Assert.Equal("Float", Assert.IsType<NamedTypeNode>(outType.Element).Name);
    }

    [Fact]
    public async Task Task_WithParams_ParsesOk()
    {
        var ast = await Parse("task Add(a: Int, b: Int) -> Int { }");
        var task = Assert.IsType<TaskDeclarationNode>(ast.Declarations.Single());
        Assert.Equal(2, task.Params.Count);
        Assert.Equal("a", task.Params[0].Name);
        Assert.Equal("Int", Assert.IsType<NamedTypeNode>(task.Params[0].Type).Name);
    }

    [Fact]
    public async Task Struct_Fields_ParsesOk()
    {
        const string src = "struct Point { x: Float; y: Float }";
        var ast = await Parse(src);
        var node = Assert.IsType<StructDeclarationNode>(ast.Declarations.Single());
        Assert.Equal("Point", node.Name);
        Assert.Equal(2, node.Fields.Count);
        Assert.Equal("x", node.Fields[0].Name);
    }

    [Fact]
    public async Task Enum_BasicVariants_ParsesOk()
    {
        var ast = await Parse("enum Color { Red, Green, Blue }");
        var node = Assert.IsType<EnumDeclarationNode>(ast.Declarations.Single());
        Assert.Equal("Color", node.Name);
        Assert.Equal(3, node.Variants.Count);
    }

    [Fact]
    public async Task Fn_WithReturnType_ParsesOk()
    {
        var ast = await Parse("fn double(n: Int) -> Int { return n * 2 }");
        var node = Assert.IsType<FnDeclarationNode>(ast.Declarations.Single());
        Assert.Equal("double", node.Name);
        Assert.Single(node.Params);
        Assert.Equal("Int", Assert.IsType<NamedTypeNode>(node.ReturnType!).Name);
    }

    [Fact]
    public async Task ExternContract_Storage_ParsesOk()
    {
        const string src = """
            struct TempReading { celsius: Float }

            extern storage TemperatureStore {
                target server
                language go
                file "user/server/storage.go"

                fn save(reading: TempReading)
                fn recent(limit: Int) -> List<TempReading>
            }
            """;

        var ast = await Parse(src);
        var node = Assert.IsType<ExternContractNode>(ast.Declarations[1]);

        Assert.Equal("storage", node.Role);
        Assert.Equal("TemperatureStore", node.Name);
        Assert.Equal("server", node.Target);
        Assert.Equal("go", node.Language);
        Assert.Equal("user/server/storage.go", node.UserFile);
        Assert.Equal(2, node.Functions.Count);
        Assert.Equal("save", node.Functions[0].Name);
        Assert.Null(node.Functions[0].ReturnType);
        Assert.Equal("recent", node.Functions[1].Name);
        Assert.Equal("List", Assert.IsType<GenericTypeNode>(node.Functions[1].ReturnType!).Name);
    }

    [Fact]
    public async Task ExternContract_CustomRoleMotion_ParsesOk()
    {
        const string src = """
            struct MotionCommand { x: Float }
            struct MachinePosition { x: Float }

            extern motion MotionDriver {
                target embedded
                language cpp
                file "user/embedded/motion_driver.cpp"

                fn enable()
                fn move_linear(cmd: MotionCommand)
                fn read_position() -> MachinePosition
            }
            """;

        var ast = await Parse(src);
        var node = Assert.IsType<ExternContractNode>(ast.Declarations[2]);

        Assert.Equal("motion", node.Role);
        Assert.Equal("MotionDriver", node.Name);
        Assert.Equal("embedded", node.Target);
        Assert.Equal("cpp", node.Language);
        Assert.Equal("user/embedded/motion_driver.cpp", node.UserFile);
        Assert.Equal(3, node.Functions.Count);
        Assert.Equal("enable", node.Functions[0].Name);
        Assert.Null(node.Functions[0].ReturnType);
        Assert.Equal("move_linear", node.Functions[1].Name);
        Assert.Equal("read_position", node.Functions[2].Name);
        Assert.Equal("MachinePosition", Assert.IsType<NamedTypeNode>(node.Functions[2].ReturnType!).Name);
    }

    [Fact]
    public async Task Device_MinimalParsesOk()
    {
        const string src = "device sensor @embedded(esp32c3) { }";
        var ast = await Parse(src);
        var device = Assert.IsType<DeviceDeclarationNode>(ast.Declarations.Single());
        Assert.Equal("sensor", device.Name);
        Assert.Equal("embedded", device.Platform.Target);
    }

    [Fact]
    public async Task Pipeline_UseChain_ParsesOk()
    {
        const string src = """
            pipeline Proc {
                use A -> B
                use B -> C
            }
            """;
        var ast = await Parse(src);
        var pipe = Assert.IsType<PipelineDeclarationNode>(ast.Declarations.Single());
        Assert.Equal("Proc", pipe.Name);
        Assert.Equal(2, pipe.Connections.Count);

        var conn = Assert.IsType<UseConnectionNode>(pipe.Connections[0]);
        Assert.Equal("A", conn.SourceTask);
        Assert.Equal("B", conn.TargetTask);
    }
}
