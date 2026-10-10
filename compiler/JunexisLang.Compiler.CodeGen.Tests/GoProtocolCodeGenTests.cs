namespace JunexisLang.Compiler.CodeGen.Tests;

public sealed partial class CodeGeneratorTests
{
    [Fact]
    public async Task Go_RestProtocol_EmitsTypedRouteAndOpenApiAsync()
    {
        const string source = """
            struct User { id: u64, name: Text }
            task GetUser(id: u64) @server -> User { }
            protocol rest {
                GET("/users/{id}", GetUser(path id: u64))
            }
            deployment Production {
                target server { GetUser @server { replicas: 2 } }
            }
            """;

        var files = new GoCodeGenerator().Generate(await TransformToIrAsync(source), Opts);
        var main = files.Single(file => file.FilePath == "server/main.go").Content;
        var openApi = files.Single(file => file.FilePath == "server/openapi.json").Content;

        Assert.Contains("http.HandleFunc(\"GET /users/{id}\"", main);
        Assert.Contains("r.PathValue(\"id\")", main);
        Assert.Contains("result := GetUser(id)", main);
        Assert.Contains("registerProtocolRoutes()", main);
        Assert.DoesNotContain("go func() { defer wg.Done(); GetUser(", main);
        Assert.Contains("\"operationId\": \"GetUser\"", openApi);
    }
}
