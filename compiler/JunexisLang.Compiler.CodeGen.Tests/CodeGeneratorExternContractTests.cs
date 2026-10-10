namespace JunexisLang.Compiler.CodeGen.Tests;

using System.Text.Json;

/// <summary>Tests for generated extern contract manifests.</summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task Go_ZeroArgumentPascalCaseExtern_IsNotMistakenForStructLiteralAsync()
    {
        const string source = """
            extern task HomeLab_state() @server -> Text
            task ReadState() @server -> Text { return HomeLab_state() }
            """;

        var files = new GoCodeGenerator().Generate(await TransformToIrAsync(source), Opts);
        var main = files.Single(file => file.FilePath == "server/main.go").Content;

        Assert.Contains("var HomeLab_state = func() string", main);
    }

    [Fact]
    public async Task Go_ExternStorageContract_EmitsExternManifest()
    {
        var ir = await TransformToIrAsync("""
            struct TempReading { celsius: Float }

            task Api() @server { }

            extern storage TemperatureStore {
                target server
                language go
                file "user/server/storage.go"

                fn save(reading: TempReading)
                fn recent(limit: Int) -> List<TempReading>
            }
            """);

        var files = new GoCodeGenerator().Generate(ir, Opts);
        var manifest = files.Single(file => file.FilePath == "server/externs/junexis.externs.toml");

        Assert.Contains("kind = \"junexis.externs\"", manifest.Content);
        Assert.Contains("role = \"storage\"", manifest.Content);
        Assert.Contains("name = \"TemperatureStore\"", manifest.Content);
        Assert.Contains("file = \"user/server/storage.go\"", manifest.Content);
        Assert.Contains("name = \"save\"", manifest.Content);
        Assert.Contains("returns = \"List<TempReading>\"", manifest.Content);
    }

    [Fact]
    public async Task Go_ExternStorageContract_EmitsProviderNeutralStorageContract()
    {
        var ir = await TransformToIrAsync("""
            struct TempReading { celsius: Float }

            task Api() @server { }

            extern storage TemperatureStore {
                target server
                language go
                file "user/server/storage.go"

                fn open()
                fn save(reading: TempReading)
                fn recent(limit: Int) -> List<TempReading>
                fn begin_transaction()
                fn commit()
                fn rollback()
                fn migrate()
                fn health_check() -> Bool
                fn compact()
                fn close()
            }
            """);

        var files = new GoCodeGenerator().Generate(ir, Opts);
        var generated = files.Single(file => file.FilePath == "server/storage/junexis.storage.contracts.json");
        using var manifest = JsonDocument.Parse(generated.Content);
        var root = manifest.RootElement;

        Assert.Equal("junexis.storage/0.1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("closed", root.GetProperty("lifecycle").GetProperty("initialState").GetString());
        Assert.Equal("unknown", root.GetProperty("transaction").GetProperty("outcomes")[2].GetString());
        Assert.Contains(root.GetProperty("errors").EnumerateArray(), error =>
            error.GetProperty("code").GetString() == "unknown-outcome");
        Assert.Contains(root.GetProperty("guarantees").EnumerateArray(), guarantee =>
            guarantee.GetString() == "parameterized-values");
        var runtimeProviders = root.GetProperty("runtimeProviders").EnumerateArray()
            .ToDictionary(provider => provider.GetProperty("id").GetString()!, provider => provider);
        Assert.Equal("persistent", runtimeProviders["sqlite"].GetProperty("durability").GetString());
        Assert.Equal("volatile", runtimeProviders["memory"].GetProperty("durability").GetString());
        Assert.Equal("remote-persistent", runtimeProviders["postgresql"].GetProperty("durability").GetString());
        Assert.Equal("junexis.storage.Store", runtimeProviders["memory"].GetProperty("domainContract").GetString());

        var operations = root.GetProperty("providers")[0].GetProperty("operations").EnumerateArray()
            .ToDictionary(operation => operation.GetProperty("name").GetString()!, operation => operation);
        Assert.Equal("write", operations["save"].GetProperty("effect").GetString());
        Assert.Equal("read", operations["recent"].GetProperty("kind").GetString());
        Assert.Equal("begin", operations["begin_transaction"].GetProperty("kind").GetString());
        Assert.Equal("transaction", operations["commit"].GetProperty("effect").GetString());
        Assert.Equal("migration", operations["migrate"].GetProperty("effect").GetString());
        Assert.Equal("health", operations["health_check"].GetProperty("effect").GetString());
        Assert.Equal("custom", operations["compact"].GetProperty("effect").GetString());

        var contractRuntime = files.Single(file => file.FilePath == "server/junexis/storage/contract.go").Content;
        var runtimeClient = files.Single(file => file.FilePath == "server/junexis/storage/runtime/client.go").Content;
        var goModule = files.Single(file => file.FilePath == "server/go.mod").Content;
        Assert.Contains("type Store interface", contractRuntime);
        Assert.Contains("Transact(context.Context, TransactionWork) *Error", contractRuntime);
        Assert.Contains("package runtime", runtimeClient);
        Assert.Contains("const abiVersion = \"junexis.storage.runtime/0.1\"", runtimeClient);
        Assert.Contains("func OpenFromEnvironment(ctx context.Context)", runtimeClient);
        Assert.Contains("var _ junexisStorage.Store = (*Provider)(nil)", runtimeClient);
        Assert.Contains("JUNEXIS_STORAGE_PROVIDER_ENTRYPOINT", runtimeClient);
        Assert.Contains("JUNEXIS_STORAGE_CONFIG_JSON", runtimeClient);
        Assert.Contains("JUNEXIS_STORAGE_BINDING", runtimeClient);
        Assert.DoesNotContain(files, file => file.FilePath.EndsWith("junexis/storage/sqlite/provider.go", StringComparison.Ordinal));
        Assert.DoesNotContain(files, file => file.FilePath.EndsWith("junexis/storage/memory/provider.go", StringComparison.Ordinal));
        Assert.DoesNotContain(files, file => file.FilePath.EndsWith("junexis/storage/postgresql/provider.go", StringComparison.Ordinal));
        Assert.DoesNotContain("modernc.org/sqlite", goModule);
        Assert.DoesNotContain("github.com/jackc/pgx/v5", goModule);
        Assert.Contains("go 1.22", goModule);
    }

    [Fact]
    public async Task Go_NonStorageExtern_DoesNotEmitStorageContract()
    {
        var ir = await TransformToIrAsync("""
            task Api() @server { }
            extern telemetry Metrics {
                target server
                language go
                fn emit(value: Int)
            }
            """);

        var files = new GoCodeGenerator().Generate(ir, Opts);

        Assert.DoesNotContain(files, file => file.FilePath.EndsWith("junexis.storage.contracts.json", StringComparison.Ordinal));
        Assert.DoesNotContain(files, file => file.FilePath.EndsWith("junexis/storage/sqlite/provider.go", StringComparison.Ordinal));
        Assert.DoesNotContain(files, file => file.FilePath.EndsWith("junexis/storage/memory/provider.go", StringComparison.Ordinal));
        Assert.DoesNotContain(files, file => file.FilePath.EndsWith("junexis/storage/postgresql/provider.go", StringComparison.Ordinal));
        Assert.DoesNotContain(files, file => file.FilePath.EndsWith("junexis/storage/runtime/client.go", StringComparison.Ordinal));
        Assert.DoesNotContain("modernc.org/sqlite", files.Single(file => file.FilePath == "server/go.mod").Content);
        Assert.DoesNotContain("github.com/jackc/pgx/v5", files.Single(file => file.FilePath == "server/go.mod").Content);
    }

    [Fact]
    public async Task Go_EmbeddedStorageContract_DoesNotIncludeServerSqliteProvider()
    {
        var ir = await TransformToIrAsync("""
            task Api() @server { }
            extern storage DeviceStore {
                target embedded
                language cpp
                fn save(value: Int)
            }
            """);

        var files = new GoCodeGenerator().Generate(ir, Opts);

        Assert.DoesNotContain(files, file => file.FilePath.EndsWith("junexis/storage/sqlite/provider.go", StringComparison.Ordinal));
        Assert.DoesNotContain(files, file => file.FilePath.EndsWith("junexis/storage/memory/provider.go", StringComparison.Ordinal));
        Assert.DoesNotContain(files, file => file.FilePath.EndsWith("junexis/storage/postgresql/provider.go", StringComparison.Ordinal));
        Assert.DoesNotContain(files, file => file.FilePath.EndsWith("junexis/storage/runtime/client.go", StringComparison.Ordinal));
        Assert.DoesNotContain("modernc.org/sqlite", files.Single(file => file.FilePath == "server/go.mod").Content);
        Assert.DoesNotContain("github.com/jackc/pgx/v5", files.Single(file => file.FilePath == "server/go.mod").Content);
    }

    [Fact]
    public async Task Go_ExternStorageContract_BindsUserServerPackage()
    {
        var ir = await TransformToIrAsync("""
            struct TempReading { celsius: Float }

            extern storage TemperatureStore {
                target server
                language go
                file "user/server/storage.go"

                fn save(reading: TempReading)
                fn recent(limit: Int) -> List<TempReading>
            }

            extern task TemperatureStore_save(reading: TempReading) @server
            extern task TemperatureStore_recent(limit: Int) @server -> List<TempReading>

            task Ingest(reading: TempReading) @server {
                TemperatureStore_save(reading)
            }

            task Recent() @server -> chan<List<TempReading>> {
                let readings = TemperatureStore_recent(10)
                send readings
            }
            """);

        var main = new GoCodeGenerator().Generate(ir, Opts)
            .Single(file => file.FilePath == "server/main.go")
            .Content;

        Assert.Contains("junexisUser \"junexis/server/user\"", main);
        Assert.Contains("var TemperatureStore_save = func(reading TempReading) { junexisUser.TemperatureStore_save(reading) }", main);
        Assert.Contains("var TemperatureStore_recent = func(limit int64) []TempReading { return __junexisSliceAs[TempReading](junexisUser.TemperatureStore_recent(limit)) }", main);
        Assert.Contains("payload, err := json.Marshal(value)", main);
        Assert.Contains("json.Unmarshal(payload, &zero)", main);
    }

    [Fact]
    public async Task Cpp_ExternMotionContract_DeclaresUserOwnedBoundaryInsteadOfStaticStub()
    {
        var ir = await TransformToIrAsync("""
            struct MotionCommand { x: Float }
            struct MachinePosition { x: Float }

            extern motion MotionDriver {
                target embedded
                language cpp
                file "user/embedded/motion_driver.cpp"

                fn move_linear(cmd: MotionCommand)
                fn read_position() -> MachinePosition
            }

            extern task MotionDriver_move_linear(cmd: MotionCommand) @embedded
            extern task MotionDriver_read_position() @embedded -> MachinePosition

            task Controller(cmd: MotionCommand) @embedded {
                MotionDriver_move_linear(cmd)
                let pos = MotionDriver_read_position()
            }
            """);

        var source = new CppCodeGenerator().Generate(ir, Opts)
            .Single(file => file.FilePath == "embedded/Controller.cpp")
            .Content;

        Assert.Contains("void MotionDriver_move_linear(MotionCommand cmd);", source);
        Assert.Contains("MachinePosition MotionDriver_read_position();", source);
        Assert.DoesNotContain("static void MotionDriver_move_linear", source);
        Assert.DoesNotContain("static MachinePosition MotionDriver_read_position", source);
    }
}
