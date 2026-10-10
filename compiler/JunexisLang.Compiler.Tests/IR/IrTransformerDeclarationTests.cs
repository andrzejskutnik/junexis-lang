using JunexisLang.Compiler.IR;

namespace JunexisLang.Compiler.Tests.IR;

/// <summary>Testy transformacji deklaracji i podstawowych typow do IR.</summary>
public sealed partial class IrTransformerTests
{
    [Fact]
    public async Task EmptyProgram_TransformsToEmptyDocument()
    {
        var ir = await TransformAsync("");
        Assert.Empty(ir.Tasks);
        Assert.Empty(ir.Structs);
        Assert.Empty(ir.Pipelines);
        Assert.Empty(ir.Functions);
        Assert.Empty(ir.Enums);
    }

    [Fact]
    public async Task Struct_TransformsToIRStruct_WithCorrectFields()
    {
        var ir = await TransformAsync("struct Point { x: Float; y: Float }");
        Assert.Single(ir.Structs);
        var structure = ir.Structs[0];
        Assert.Equal("Point", structure.Name);
        Assert.Equal(2, structure.Fields.Count);
        Assert.Equal("x", structure.Fields[0].Name);
        Assert.Equal("Float", ((IRNamedType)structure.Fields[0].Type).Name);
        Assert.Equal("y", structure.Fields[1].Name);
    }

    [Fact]
    public async Task Enum_TransformsToIREnum_WithVariants()
    {
        var ir = await TransformAsync("enum Color { Red, Green, Blue }");
        Assert.Single(ir.Enums);
        var enumType = ir.Enums[0];
        Assert.Equal("Color", enumType.Name);
        Assert.Equal(3, enumType.Variants.Count);
        Assert.Equal("Red", enumType.Variants[0].Name);
        Assert.Equal("Green", enumType.Variants[1].Name);
        Assert.Equal("Blue", enumType.Variants[2].Name);
    }

    [Fact]
    public async Task Task_WithEmbeddedPlatform_TransformsToIRTask()
    {
        var ir = await TransformAsync("task Sensor() @embedded -> chan<Float> { }");
        Assert.Single(ir.Tasks);
        var task = ir.Tasks[0];
        Assert.Equal("Sensor", task.Name);
        Assert.Single(task.Platforms);
        Assert.Equal("embedded", task.Platforms[0].Target);
        var outType = Assert.IsType<IRCommType>(task.OutputType);
        Assert.Equal(IRCommKind.Chan, outType.Kind);
        Assert.Equal("Float", ((IRNamedType)outType.Element).Name);
    }

    [Fact]
    public async Task Fn_WithParams_TransformsToIRFn()
    {
        var ir = await TransformAsync("fn add(a: Int, b: Int) -> Int { return a }");
        Assert.Single(ir.Functions);
        var function = ir.Functions[0];
        Assert.Equal("add", function.Name);
        Assert.Equal(2, function.Params.Count);
        Assert.Equal("a", function.Params[0].Name);
        Assert.Equal("Int", ((IRNamedType)function.Params[0].Type).Name);
        Assert.Equal("Int", ((IRNamedType)function.ReturnType!).Name);
    }

    [Fact]
    public async Task Narrative_TransformsToIRNarrative_WithOpaqueBody()
    {
        var ir = await TransformAsync("""narrative "Monitoring" { intent "observe sensors" }""");
        Assert.Single(ir.Narratives);
        var narrative = ir.Narratives[0];
        Assert.Equal("Monitoring", narrative.Label);
        Assert.Contains("intent", narrative.RawBody);
    }

    [Fact]
    public async Task Import_TransformsToIRImport()
    {
        var ir = await TransformAsync("import stdlib");
        Assert.Single(ir.Imports);
        Assert.Equal("stdlib", ir.Imports[0].ModulePath);
        Assert.Null(ir.Imports[0].Alias);
    }

    [Fact]
    public async Task CommunicationTypes_ChanQueueStream_MappedCorrectly()
    {
        var ir = await TransformAsync("""
            task A() -> chan<Int>   { }
            task B() -> queue<Text> { }
            task C() -> stream<Float> { }
            """);
        Assert.Equal(IRCommKind.Chan, ((IRCommType)ir.Tasks[0].OutputType!).Kind);
        Assert.Equal(IRCommKind.Queue, ((IRCommType)ir.Tasks[1].OutputType!).Kind);
        Assert.Equal(IRCommKind.Stream, ((IRCommType)ir.Tasks[2].OutputType!).Kind);
    }

    [Fact]
    public async Task UnionType_InStructField_MappedToIRUnionType()
    {
        var ir = await TransformAsync("struct Result { value: Int | Text }");
        var field = ir.Structs[0].Fields[0];
        Assert.IsType<IRUnionType>(field.Type);
    }

    [Fact]
    public async Task Import_WithAlias_TransformsCorrectly()
    {
        var ir = await TransformAsync("import stdlib as std");
        Assert.Single(ir.Imports);
        Assert.Equal("std", ir.Imports[0].Alias);
    }

    [Fact]
    public async Task ExternContract_Storage_TransformsToIR()
    {
        var ir = await TransformAsync("""
            struct TempReading { celsius: Float }

            extern storage TemperatureStore {
                target server
                language go
                file "user/server/storage.go"

                fn save(reading: TempReading)
                fn recent(limit: Int) -> List<TempReading>
            }
            """);

        var contract = Assert.Single(ir.ExternContracts);

        Assert.Equal("storage", contract.Role);
        Assert.Equal("TemperatureStore", contract.Name);
        Assert.Equal("server", contract.Target);
        Assert.Equal("go", contract.Language);
        Assert.Equal("user/server/storage.go", contract.UserFile);
        Assert.Equal(2, contract.Functions.Count);
        Assert.Equal("save", contract.Functions[0].Name);
        Assert.Null(contract.Functions[0].ReturnType);
        var returnType = Assert.IsType<IRGenericType>(contract.Functions[1].ReturnType);
        Assert.Equal("List", returnType.Name);
    }

    [Fact]
    public async Task ExternContract_CustomRoleMotion_TransformsToIR()
    {
        var ir = await TransformAsync("""
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
            """);

        var contract = Assert.Single(ir.ExternContracts);

        Assert.Equal("motion", contract.Role);
        Assert.Equal("MotionDriver", contract.Name);
        Assert.Equal("embedded", contract.Target);
        Assert.Equal("cpp", contract.Language);
        Assert.Equal("user/embedded/motion_driver.cpp", contract.UserFile);
        Assert.Equal(3, contract.Functions.Count);
        Assert.Equal("enable", contract.Functions[0].Name);
        Assert.Null(contract.Functions[0].ReturnType);
        Assert.Equal("move_linear", contract.Functions[1].Name);
        Assert.Equal("read_position", contract.Functions[2].Name);
        Assert.Equal("MachinePosition", Assert.IsType<IRNamedType>(contract.Functions[2].ReturnType).Name);
    }
}
