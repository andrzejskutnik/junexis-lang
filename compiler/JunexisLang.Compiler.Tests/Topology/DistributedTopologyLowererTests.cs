namespace JunexisLang.Compiler.Tests.Topology;

using JunexisLang.Compiler.Core;
using JunexisLang.Compiler.IR;
using JunexisLang.Compiler.Topology;

public sealed class DistributedTopologyLowererTests
{
    [Fact]
    public async Task RestAndDeployment_AreLoweredToExecutablePlanAsync()
    {
        const string source = """
            task ListUsers() @server -> stream<Text> { }
            task Dashboard(input: stream<Text>) @report { }

            protocol rest {
                GET("/users", ListUsers())
            }

            pipeline Main {
                use ListUsers -> Dashboard.input
            }

            deployment Production {
                target server {
                    ListUsers @server { replicas: 3 }
                }
                target report {
                    Dashboard @report {}
                }
            }
            """;

        var plan = DistributedTopologyLowerer.Lower(await TransformAsync(source));

        Assert.True(plan.Success, string.Join(Environment.NewLine, plan.Diagnostics.Select(d => d.Message)));
        var binding = Assert.Single(plan.Plan.ProtocolBindings);
        Assert.Equal("rest", binding.Protocol);
        Assert.Equal("GET", binding.Method);
        Assert.Equal("/users", binding.Path);
        Assert.Equal("ListUsers", binding.Task);

        var server = Assert.Single(plan.Plan.Placements, placement => placement.Task == "ListUsers");
        Assert.True(server.Explicit);
        Assert.Equal(3, server.Instances);

        var edge = Assert.Single(plan.Plan.Edges);
        Assert.Equal(IRCommKind.Stream, edge.CommunicationKind);
        Assert.Equal("http", edge.Protocol);
        Assert.Equal("json", edge.Encoding);
        Assert.Equal("junexis-wire/0.2", edge.WireVersion);
    }

    [Fact]
    public async Task ProtocolBinding_ToUnknownTask_IsAnErrorAsync()
    {
        const string source = """
            task Existing() @server { }
            protocol rest { GET("/missing", Missing()) }
            """;

        var result = DistributedTopologyLowerer.Lower(await TransformAsync(source));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "D6001");
    }

    [Fact]
    public async Task DeploymentPlatformMismatch_IsAnErrorAsync()
    {
        const string source = """
            task Worker() @server { }
            deployment Production {
                target report { Worker @server {} }
            }
            """;

        var result = DistributedTopologyLowerer.Lower(await TransformAsync(source));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "D6102");
    }

    private static async Task<IRDocument> TransformAsync(string source)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, source);
            var check = await new JunexisCompiler().CheckAsync(new CheckOptions(path));
            Assert.True(check.Success, string.Join(Environment.NewLine, check.Diagnostics.Select(d => d.Message)));
            return new AstToIrTransformer().Transform(check.Ast!);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
