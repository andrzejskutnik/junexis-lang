namespace JunexisLang.Compiler.CodeGen.Tests;

using JunexisLang.Compiler.IR;

/// <summary>
/// Testy plików wyjściowych i junexis_app.cpp generowanych przez CppCodeGenerator.
/// </summary>
public sealed partial class CodeGeneratorTests
{
    [Fact]
    public void Cpp_NoEmbeddedTask_ReturnsEmpty()
    {
        var gen = new CppCodeGenerator();
        var doc = DocWith(ServerTask("Process"));

        var files = gen.Generate(doc, Opts);

        Assert.Empty(files);
    }

    [Fact]
    public void Cpp_SingleTask_GeneratesHeaderAndMain()
    {
        var gen = new CppCodeGenerator();
        var doc = DocWith(EmbeddedTask("Read"));

        var files = gen.Generate(doc, Opts);

        Assert.Equal(7, files.Count);
        Assert.Contains(files, f => f.FilePath == "embedded/Read.h");
        Assert.Contains(files, f => f.FilePath == "embedded/Read.cpp");
        Assert.Contains(files, f => f.FilePath == "embedded/junexis_app.cpp");
        Assert.Contains(files, f => f.FilePath == "embedded/platformio.ini");
        Assert.Contains(files, f => f.FilePath == "embedded/junexis_types.h");
        Assert.Contains(files, f => f.FilePath == "embedded/junexis_config.h");
        Assert.Contains(files, f => f.FilePath == "embedded/junexis/runtime.h");
    }

    [Fact]
    public void Cpp_App_ContainsSetupAndLoop()
    {
        var gen = new CppCodeGenerator();
        var doc = DocWith(EmbeddedTask("Blink"));
        var main = gen.Generate(doc, Opts)
            .Single(f => f.FilePath == "embedded/junexis_app.cpp");

        Assert.Contains("void setup()", main.Content);
        Assert.Contains("void loop()", main.Content);
    }

    [Fact]
    public void Cpp_App_IncludesAllTaskHeaders()
    {
        var gen = new CppCodeGenerator();
        var doc = DocWith([EmbeddedTask("A"), EmbeddedTask("B")]);
        var main = gen.Generate(doc, Opts)
            .Single(f => f.FilePath == "embedded/junexis_app.cpp");

        Assert.Contains("#include \"A.h\"", main.Content);
        Assert.Contains("#include \"B.h\"", main.Content);
    }

    [Fact]
    public void Cpp_App_PlatformBadge_IsEmbedded()
    {
        var gen = new CppCodeGenerator();
        Assert.Equal("embedded", gen.Platform);
    }

    [Fact]
    public void Cpp_App_UsesCapabilitiesForIncludesAndBootstrap()
    {
        var gen = new CppCodeGenerator();
        var main = gen.Generate(DocWith(EmbeddedTask("Read", variant: "esp32c3")), Opts)
            .Single(f => f.FilePath == "embedded/junexis_app.cpp");

        Assert.Contains("#include <WiFi.h>", main.Content);
        Assert.DoesNotContain("#include <FS.h>", main.Content);
        Assert.Contains("static void setupPlatformNetworking()", main.Content);
        Assert.Contains("setupPlatformNetworking();", main.Content);
        Assert.Contains("Networking bootstrap enabled by platform profile.", main.Content);
        Assert.DoesNotContain("File I/O bootstrap enabled by platform profile.", main.Content);
    }

    [Fact]
    public void Cpp_App_NetworkingBootstrap_EmitsWifiBeginAndRetryLoop()
    {
        var gen = new CppCodeGenerator();
        var main = gen.Generate(DocWith(EmbeddedTask("Sensor", variant: "esp32c3")), Opts)
            .Single(f => f.FilePath == "embedded/junexis_app.cpp");

        Assert.Contains("kWifiSsid", main.Content);
        Assert.Contains("kWifiPass", main.Content);
        Assert.Contains("WiFi.begin(kWifiSsid, kWifiPass);", main.Content);
        Assert.Contains("WiFi.waitForConnectResult() != WL_CONNECTED", main.Content);
        Assert.DoesNotContain("// TODO:", main.Content);
    }

    [Fact]
    public void Cpp_App_ThreadingBootstrap_EmitsFreeRtosTaskCreate()
    {
        var gen = new CppCodeGenerator();
        // esp32s3: threading=true, networking=true, fileIO=false
        var main = gen.Generate(DocWith(EmbeddedTask("Monitor", variant: "esp32s3")), Opts)
            .Single(f => f.FilePath == "embedded/junexis_app.cpp");

        Assert.Contains("setupPlatformThreading();", main.Content);
        Assert.Contains("static TaskHandle_t task_Monitor_Handle = nullptr;", main.Content);
        Assert.Contains("xTaskCreate(", main.Content);
        Assert.Contains("\"Monitor\"", main.Content);
        Assert.Contains("task_Monitor_Handle", main.Content);
        Assert.DoesNotContain("// TODO:", main.Content);
    }

    [Fact]
    public void Cpp_App_ThreadingBootstrap_MultipleTasksCreateAllHandles()
    {
        var gen = new CppCodeGenerator();
        var doc = DocWith([EmbeddedTask("Alpha", variant: "esp32s3"), EmbeddedTask("Beta", variant: "esp32s3")]);
        var main = gen.Generate(doc, Opts)
            .Single(f => f.FilePath == "embedded/junexis_app.cpp");

        Assert.Contains("task_Alpha_Handle", main.Content);
        Assert.Contains("task_Beta_Handle", main.Content);
        Assert.Contains("\"Alpha\"", main.Content);
        Assert.Contains("\"Beta\"", main.Content);
    }
}
