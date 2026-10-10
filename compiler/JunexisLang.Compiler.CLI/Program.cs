using System.CommandLine;
using JunexisLang.Compiler.CLI.Handlers;
using System.Text.Json;

if (args is ["--distribution-manifest", var runtimeIdentifier, var entrypoint, var selfContainedText])
{
    if (!bool.TryParse(selfContainedText, out var selfContained) ||
        string.IsNullOrWhiteSpace(runtimeIdentifier) ||
        string.IsNullOrWhiteSpace(entrypoint) ||
        Path.GetFileName(entrypoint) != entrypoint)
    {
        Console.Error.WriteLine(
            "Usage: --distribution-manifest <runtimeIdentifier> <entrypoint> <selfContained>");
        return 2;
    }

    var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        name = "JunexisLang.Compiler.CLI",
        version,
        protocolVersion = 2,
        runtimeIdentifier,
        entrypoint,
        readyToRun = true,
        selfContained
    }));
    return 0;
}

if (args.Length > 0 && args[0] == "--distribution-manifest")
{
    Console.Error.WriteLine(
        "Usage: --distribution-manifest <runtimeIdentifier> <entrypoint> <selfContained>");
    return 2;
}

var root = new RootCommand("junexis \u2014 Junexis Language compiler and toolchain");

// ─── init [path] ────────────────────────────────────────────────────
var initPath  = new Argument<DirectoryInfo>("path", () => new DirectoryInfo("."), "Project directory (default: current directory)");
var initName  = new Option<string?>(["--name", "-n"], "Project name (default: directory name)");
var initForce = new Option<bool>("--force", "Create missing project files even when the directory is not empty");

var initCmd = new Command("init", "Create a Junexis project layout");
initCmd.Add(initPath);
initCmd.Add(initName);
initCmd.Add(initForce);
initCmd.SetHandler(InitHandler.HandleAsync, initPath, initName, initForce);

// ─── parse <input> ──────────────────────────────────────────────────
var parseInput = new Argument<FileInfo>("input", "Source file (.jnx)");
var parseJson  = new Option<bool>("--json", "Emit diagnostics as JSON");

var parseCmd = new Command("parse", "Parse a .jnx file into AST without semantic validation");
parseCmd.Add(parseInput);
parseCmd.Add(parseJson);
parseCmd.SetHandler(ParseHandler.HandleAsync, parseInput, parseJson);

// ─── build <input> (`compile` = compatibility alias) ───────────────
var compileInput  = new Argument<string?>("input", () => null, "Source file (.jnx) or project directory (default: discovered project)");
var compileOut    = new Option<DirectoryInfo?>(["--out", "-o"], "Output directory (default: <input-dir>/out)");
var compileTarget = new Option<string[]>(["--target", "-t"],    "Target platform: embedded, server, report (repeatable)");
var compileJson   = new Option<bool>("--json", "Emit diagnostics as JSON");
var compileOutputSchema = new Option<string>("--output-schema", () => "1.0", "JSON output schema: 1.0 or 2.0");
var compileBuildId = new Option<string?>("--build-id", "Expected analysis preflight build identifier");

var compileCmd = new Command("build", "Build a .jnx file and generate target artifacts");
compileCmd.AddAlias("compile");
compileCmd.Add(compileInput);
compileCmd.Add(compileOut);
compileCmd.Add(compileTarget);
compileCmd.Add(compileJson);
compileCmd.Add(compileOutputSchema);
compileCmd.Add(compileBuildId);
compileCmd.SetHandler(CompileHandler.HandleAsync, compileInput, compileOut, compileTarget, compileJson, compileOutputSchema, compileBuildId);

// ─── analyze <input> ────────────────────────────────────────────────
var analyzeInput = new Argument<string?>("input", () => null, "Source file (.jnx) or project directory (default: discovered project)");
var analyzeOut = new Option<DirectoryInfo?>(["--out", "-o"], "Output directory (default: project output)");
var analyzeTarget = new Option<string[]>(["--target", "-t"], "Target platform used to establish build identity");
var analyzeJson = new Option<bool>("--json", "Emit Tooling Protocol JSON");
var analyzeCmd = new Command("analyze", "Check and publish an analysis snapshot without generating target code");
analyzeCmd.Add(analyzeInput);
analyzeCmd.Add(analyzeOut);
analyzeCmd.Add(analyzeTarget);
analyzeCmd.Add(analyzeJson);
analyzeCmd.SetHandler(AnalyzeHandler.HandleAsync, analyzeInput, analyzeOut, analyzeTarget, analyzeJson);

// ─── check <input> ──────────────────────────────────────────────────
var checkInput = new Argument<string?>("input", () => null, "Source file (.jnx) or project directory (default: discovered project)");
var checkJson  = new Option<bool>("--json", "Emit diagnostics as JSON");

var checkCmd = new Command("check", "Parse and check a .jnx file without generating code");
checkCmd.Add(checkInput);
checkCmd.Add(checkJson);
checkCmd.SetHandler(CheckHandler.HandleAsync, checkInput, checkJson);

// ─── restore [project] ─────────────────────────────────────────────
var restoreInput = new Argument<string?>("project", () => null, "Project directory (default: discovered project)");
var restoreOffline = new Option<bool>("--offline", "Restore only from the project package cache and lockfile");
var restoreRegistry = new Option<string?>("--registry", "Registry base URL used to restore application packages");
var restoreCmd = new Command("restore", "Restore exact package versions and write junexis.packages.lock");
restoreCmd.Add(restoreInput);
restoreCmd.Add(restoreOffline);
restoreCmd.Add(restoreRegistry);
restoreCmd.SetHandler(RestoreHandler.HandleAsync, restoreInput, restoreOffline, restoreRegistry);

// ─── run [project] ─────────────────────────────────────────────────
var runInput = new Argument<string?>("project", () => null, "Project directory (default: discovered project)");
var runDuration = new Option<int>("--duration", () => 8, "Bounded application session duration in seconds (1-300)");
var runGoExe = new Option<string>("--go-exe", () => "go", "Go executable");
var runNpmExe = new Option<string>("--npm-exe", () => "npm", "npm executable used to build a report application");
var runNodeExe = new Option<string>("--node-exe", () => "node", "Node.js executable used to serve a report application");
var runNoOpen = new Option<bool>("--no-open", "Do not open the generated report application in the default browser");
var runStorageRuntimeHost = new Option<string?>(
    "--storage-runtime-host",
    "Storage Runtime host executable or .dll (required by a bound storage project)");
var runCmd = new Command("run", "Build and run a server and its generated report application as one bounded local session");
runCmd.Add(runInput);
runCmd.Add(runDuration);
runCmd.Add(runGoExe);
runCmd.Add(runNpmExe);
runCmd.Add(runNodeExe);
runCmd.Add(runNoOpen);
runCmd.Add(runStorageRuntimeHost);
runCmd.SetHandler(async context =>
{
    Console.OutputEncoding = new System.Text.UTF8Encoding(false);
    context.ExitCode = await RunHandler.HandleAsync(
        context.ParseResult.GetValueForArgument(runInput),
        context.ParseResult.GetValueForOption(runDuration),
        context.ParseResult.GetValueForOption(runGoExe)!,
        context.ParseResult.GetValueForOption(runNpmExe)!,
        context.ParseResult.GetValueForOption(runNodeExe)!,
        context.ParseResult.GetValueForOption(runNoOpen),
        context.ParseResult.GetValueForOption(runStorageRuntimeHost));
});

// ─── clean [project] ────────────────────────────────────────────────
var cleanInput = new Argument<string?>("project", () => null, "Project directory (default: discovered project)");
var cleanDryRun = new Option<bool>("--dry-run", "Print paths that would be removed without deleting them");

var cleanCmd = new Command("clean", "Remove generated project artifacts without touching source files");
cleanCmd.Add(cleanInput);
cleanCmd.Add(cleanDryRun);
cleanCmd.SetHandler(CleanHandler.HandleAsync, cleanInput, cleanDryRun);

// ─── doctor [path] ─────────────────────────────────────────────────
var doctorInput = new Argument<string?>("path", () => null, "Project directory or .jnx file (default: current directory)");

var doctorCmd = new Command("doctor", "Diagnose Junexis project layout and source health");
doctorCmd.Add(doctorInput);
doctorCmd.SetHandler(DoctorHandler.HandleAsync, doctorInput);

// ─── sdk new provider <id> ─────────────────────────────────────────
var sdkProviderId = new Argument<string>("id", "Provider package ID, e.g. Acme.Storage.Redis");
var sdkProviderOutput = new Option<DirectoryInfo?>(["--output", "-o"], "Target directory");
var sdkProviderSource = new Option<DirectoryInfo?>("--sdk-source", "Local Junexis SDK package feed");
var sdkProviderJson = new Option<bool>("--json", "Emit a versioned JSON result");
var sdkNewProviderCmd = new Command("provider", "Create a safe, working Storage Runtime provider");
sdkNewProviderCmd.Add(sdkProviderId);
sdkNewProviderCmd.Add(sdkProviderOutput);
sdkNewProviderCmd.Add(sdkProviderSource);
sdkNewProviderCmd.Add(sdkProviderJson);
sdkNewProviderCmd.SetHandler(async context =>
{
    context.ExitCode = await SdkToolHandler.NewProviderAsync(
        context.ParseResult.GetValueForArgument(sdkProviderId),
        context.ParseResult.GetValueForOption(sdkProviderOutput),
        context.ParseResult.GetValueForOption(sdkProviderSource),
        context.ParseResult.GetValueForOption(sdkProviderJson));
});
var sdkNewCmd = new Command("new", "Create a Junexis extension");
sdkNewCmd.Add(sdkNewProviderCmd);
var sdkTestProviderPath = new Argument<DirectoryInfo>("path", "Provider project directory");
var sdkTestConfiguration = new Option<string>("--configuration", () => "Release", "Build configuration: Release or Debug");
var sdkTestNoBuild = new Option<bool>("--no-build", "Test an already built provider");
var sdkTestOffline = new Option<bool>("--offline", "Use local package sources without vulnerability feed access");
var sdkTestProfile = new Option<string[]>("--profile", "Capability conformance profile; currently storage-plans")
{
    AllowMultipleArgumentsPerToken = true
};
var sdkTestJson = new Option<bool>("--json", "Emit a versioned JSON conformance result");
var sdkTestProviderCmd = new Command("provider", "Run the portable provider Conformance Kit");
sdkTestProviderCmd.Add(sdkTestProviderPath);
sdkTestProviderCmd.Add(sdkTestConfiguration);
sdkTestProviderCmd.Add(sdkTestNoBuild);
sdkTestProviderCmd.Add(sdkTestOffline);
sdkTestProviderCmd.Add(sdkTestProfile);
sdkTestProviderCmd.Add(sdkTestJson);
sdkTestProviderCmd.SetHandler(async context =>
{
    context.ExitCode = await SdkToolHandler.TestProviderAsync(
        context.ParseResult.GetValueForArgument(sdkTestProviderPath),
        context.ParseResult.GetValueForOption(sdkTestConfiguration)!,
        context.ParseResult.GetValueForOption(sdkTestNoBuild),
        context.ParseResult.GetValueForOption(sdkTestOffline),
        context.ParseResult.GetValueForOption(sdkTestProfile) ?? [],
        context.ParseResult.GetValueForOption(sdkTestJson));
});
var sdkTestCmd = new Command("test", "Verify a Junexis extension");
sdkTestCmd.Add(sdkTestProviderCmd);
var sdkValidateProviderPath = new Argument<DirectoryInfo>("path", "Provider project directory");
var sdkValidateJson = new Option<bool>("--json", "Emit versioned JSON diagnostics");
var sdkValidateProviderCmd = new Command("provider", "Validate provider manifests and project metadata");
sdkValidateProviderCmd.Add(sdkValidateProviderPath);
sdkValidateProviderCmd.Add(sdkValidateJson);
sdkValidateProviderCmd.SetHandler(async context =>
{
    context.ExitCode = await SdkToolHandler.ValidateProviderAsync(
        context.ParseResult.GetValueForArgument(sdkValidateProviderPath),
        context.ParseResult.GetValueForOption(sdkValidateJson));
});
var sdkValidateCmd = new Command("validate", "Validate a Junexis extension without executing it");
sdkValidateCmd.Add(sdkValidateProviderCmd);
var sdkCompatibilityProviderPath = new Argument<DirectoryInfo>("path", "Provider project directory");
var sdkCompatibilityRuntime = new Option<string>("--runtime-version", "Runtime Host SemVer") { IsRequired = true };
var sdkCompatibilityRid = new Option<string?>("--rid", "Target runtime identifier; defaults to the current host");
var sdkCompatibilityRequire = new Option<string[]>("--require", "Required provider capability; may be repeated")
{
    AllowMultipleArgumentsPerToken = true
};
var sdkCompatibilityJson = new Option<bool>("--json", "Emit a versioned compatibility result");
var sdkCompatibilityProviderCmd = new Command("provider", "Resolve provider compatibility before execution");
sdkCompatibilityProviderCmd.Add(sdkCompatibilityProviderPath);
sdkCompatibilityProviderCmd.Add(sdkCompatibilityRuntime);
sdkCompatibilityProviderCmd.Add(sdkCompatibilityRid);
sdkCompatibilityProviderCmd.Add(sdkCompatibilityRequire);
sdkCompatibilityProviderCmd.Add(sdkCompatibilityJson);
sdkCompatibilityProviderCmd.SetHandler(async context =>
{
    context.ExitCode = await SdkToolHandler.CompatibilityProviderAsync(
        context.ParseResult.GetValueForArgument(sdkCompatibilityProviderPath),
        context.ParseResult.GetValueForOption(sdkCompatibilityRuntime)!,
        context.ParseResult.GetValueForOption(sdkCompatibilityRid),
        context.ParseResult.GetValueForOption(sdkCompatibilityRequire) ?? [],
        context.ParseResult.GetValueForOption(sdkCompatibilityJson));
});
var sdkCompatibilityCmd = new Command("compatibility", "Check an extension against a target environment");
sdkCompatibilityCmd.Add(sdkCompatibilityProviderCmd);
var sdkCmd = new Command("sdk", "Build and verify Junexis extensions");
sdkCmd.Add(sdkNewCmd);
sdkCmd.Add(sdkTestCmd);
sdkCmd.Add(sdkValidateCmd);
sdkCmd.Add(sdkCompatibilityCmd);

// ─── extern scaffold [project] ─────────────────────────────────────
var externInput = new Argument<string?>("project", () => null, "Project directory (default: discovered project)");
var externDryRun = new Option<bool>("--dry-run", "Print files that would be created without writing them");

var externCmd = new Command("extern", "Work with developer-owned extern integration files");
var externScaffoldCmd = new Command("scaffold", "Create missing developer-owned extern files without overwriting existing files");
externScaffoldCmd.Add(externInput);
externScaffoldCmd.Add(externDryRun);
externScaffoldCmd.SetHandler(ExternScaffoldHandler.HandleAsync, externInput, externDryRun);
externCmd.Add(externScaffoldCmd);

// ─── report scaffold [project] ─────────────────────────────────────
var reportInput = new Argument<string?>("project", () => null, "Project directory (default: discovered project)");
var reportDryRun = new Option<bool>("--dry-run", "Print the adapter path without creating it");
var reportFramework = new Option<string>("--framework", () => "dom", "Custom UI framework: dom, react, or vue");
var reportDeclarative = new Option<bool>("--declarative", "Create a validated declarative view instead of custom UI code");
var reportCmd = new Command("report", "Work with the generated report application");
var reportScaffoldCmd = new Command("scaffold", "Create a developer-owned report adapter without overwriting existing code");
reportScaffoldCmd.Add(reportInput);
reportScaffoldCmd.Add(reportDryRun);
reportScaffoldCmd.Add(reportFramework);
reportScaffoldCmd.Add(reportDeclarative);
reportScaffoldCmd.SetHandler(
    ReportScaffoldHandler.HandleAsync,
    reportInput,
    reportDryRun,
    reportFramework,
    reportDeclarative);
reportCmd.Add(reportScaffoldCmd);

// ─── runtime primitives ─────────────────────────────────────────────
var runtimeJson = new Option<bool>("--json", "Emit runtime primitive inventory as JSON");

var runtimeCmd = new Command("runtime", "Inspect Junexis runtime contracts");
var runtimePrimitivesCmd = new Command("primitives", "List known runtime primitives and backend implementation coverage");
runtimePrimitivesCmd.Add(runtimeJson);
runtimePrimitivesCmd.SetHandler(RuntimePrimitivesHandler.HandleAsync, runtimeJson);
runtimeCmd.Add(runtimePrimitivesCmd);

// ─── conformance <report> ──────────────────────────────────────────
var conformanceReport = new Argument<FileInfo>("report", "Semantic Conformance JSON report");
var conformanceJson = new Option<bool>("--json", "Emit normalized non-blocking conformance status as JSON");
var conformanceCmd = new Command("conformance", "Show Semantic Conformance status and non-blocking gaps");
conformanceCmd.Add(conformanceReport);
conformanceCmd.Add(conformanceJson);
conformanceCmd.SetHandler(SemanticConformanceHandler.HandleAsync, conformanceReport, conformanceJson);

// ─── lsp ────────────────────────────────────────────────────────────
var lspCmd = new Command("lsp", "Start Language Server (stdin/stdout, JSON-RPC 2.0)");
lspCmd.SetHandler(LspHandler.HandleAsync);

// ─── validate-example <input> ───────────────────────────────────────
var validateInput = new Argument<FileInfo>("input", "Source file (.jnx) to validate end-to-end");
var validateGoExe = new Option<string>("--go-exe", () => "go", "Path to the Go executable (default: 'go' from PATH)");

var validateCmd = new Command("validate-example", "Run full E2E validation: check → compile (Go/server) → go build");
validateCmd.Add(validateInput);
validateCmd.Add(validateGoExe);
validateCmd.SetHandler(ValidateExampleHandler.HandleAsync, validateInput, validateGoExe);

// ─── artifact-gate <input> ──────────────────────────────────────────────
var gateInput = new Argument<string?>("input", () => null, "Source file (.jnx) or project directory to validate through native target toolchains");
var gateTargets = new Option<string[]>(["--target", "-t"], "Target: embedded, server, report (default: all applicable)");
var gateGoExe = new Option<string>("--go-exe", () => "go", "Go executable");
var gateNpmExe = new Option<string>("--npm-exe", () => "npm", "npm executable");
var gatePlatformIoExe = new Option<string>("--platformio-exe", () => "pio", "PlatformIO executable");
var gateCmd = new Command("artifact-gate", "Run M4 artifact contracts and native toolchain builds");
gateCmd.Add(gateInput);
gateCmd.Add(gateTargets);
gateCmd.Add(gateGoExe);
gateCmd.Add(gateNpmExe);
gateCmd.Add(gatePlatformIoExe);
gateCmd.SetHandler(ArtifactGateHandler.HandleAsync, gateInput, gateTargets, gateGoExe, gateNpmExe, gatePlatformIoExe);

// ─── deploy <input> ─────────────────────────────────────────────────
var deployInput     = new Argument<string?>("input", () => null, "Source file (.jnx) or project directory (default: discovered project)");
var deployOut       = new Option<DirectoryInfo?>(["--out", "-o"], "Output directory (default: <input-dir>/out)");
var deployTraceTag  = new Option<string>("--trace-image", () => "ghcr.io/junexis/narrative:latest",
                                        "Docker image tag for the TraceCollector service");

var deployCmd = new Command("deploy", "Generate docker-compose.yml for the .jnx project");
deployCmd.Add(deployInput);
deployCmd.Add(deployOut);
deployCmd.Add(deployTraceTag);
deployCmd.SetHandler(DeployHandler.HandleAsync, deployInput, deployOut, deployTraceTag);

var deployPreflightBundle = new Argument<FileInfo>(
    "bundle",
    "Verified System Bundle ZIP produced by 'junexis package'");
var deployPreflightReceipt = new Option<FileInfo?>(
    "--receipt",
    "Package receipt (default: <bundle>.package-receipt.json)");
var deployPreflightTargets = new Option<string[]>(
    ["--target", "-t"],
    "Deployment target: embedded, server, report (repeatable; default: bundle targets)");
var deployPreflightMentor = new Option<string?>(
    "--mentor-exe",
    "Mentor CLI executable or DLL (fallback: JUNEXIS_MENTOR_CLI/PATH)");
var deployPreflightAuthenticityPolicy = new Option<string>(
    "--auth-policy",
    () => "permissive",
    "System Bundle authenticity policy: permissive, signed, or trusted");
var deployPreflightSignature = new Option<FileInfo?>(
    "--signature",
    "Detached System Bundle signature (default: <bundle>.signature.json)");
var deployPreflightTrustedSigners = new Option<string[]>(
    "--trusted-signer",
    "Trusted signer SHA-256 fingerprint (repeatable)");
var deployPreflightOutput = new Option<FileInfo?>(
    ["--report-out"],
    "Write the deterministic deployment preflight JSON report");
var deployPreflightJson = new Option<bool>(
    "--json",
    "Emit the deployment preflight report as JSON");
var deployPreflightCmd = new Command(
    "preflight",
    "Verify package evidence and target readiness; request a non-blocking Mentor opinion");
deployPreflightCmd.Add(deployPreflightBundle);
deployPreflightCmd.Add(deployPreflightReceipt);
deployPreflightCmd.Add(deployPreflightTargets);
deployPreflightCmd.Add(deployPreflightMentor);
deployPreflightCmd.Add(deployPreflightAuthenticityPolicy);
deployPreflightCmd.Add(deployPreflightSignature);
deployPreflightCmd.Add(deployPreflightTrustedSigners);
deployPreflightCmd.Add(deployPreflightOutput);
deployPreflightCmd.Add(deployPreflightJson);
deployPreflightCmd.SetHandler(async context =>
{
    context.ExitCode = await DeploymentPreflightHandler.HandleAsync(
        context.ParseResult.GetValueForArgument(deployPreflightBundle),
        context.ParseResult.GetValueForOption(deployPreflightReceipt),
        context.ParseResult.GetValueForOption(deployPreflightTargets),
        context.ParseResult.GetValueForOption(deployPreflightMentor),
        context.ParseResult.GetValueForOption(deployPreflightAuthenticityPolicy)!,
        context.ParseResult.GetValueForOption(deployPreflightSignature),
        context.ParseResult.GetValueForOption(deployPreflightTrustedSigners),
        context.ParseResult.GetValueForOption(deployPreflightOutput),
        context.ParseResult.GetValueForOption(deployPreflightJson));
});
deployCmd.Add(deployPreflightCmd);

var deployLocalBundle = new Argument<FileInfo>(
    "bundle",
    "Verified System Bundle ZIP produced by 'junexis package'");
var deployLocalPreflight = new Option<FileInfo>(
    "--preflight",
    "Technically ready M7.3 preflight report")
{
    IsRequired = true
};
var deployLocalReceipt = new Option<FileInfo?>(
    "--receipt",
    "Package receipt (default: <bundle>.package-receipt.json)");
var deployLocalReceiptOut = new Option<FileInfo?>(
    "--receipt-out",
    "Deployment receipt output path (default: <preflight-dir>/deployment-receipt.json)");
var deployLocalSessionOut = new Option<FileInfo?>(
    "--session-out",
    "Deployment session output path (default: <preflight-dir>/deployment-session.json)");
var deployLocalTargets = new Option<string[]>(
    ["--target", "-t"],
    "Local target: server or report (repeatable; default: preflight targets)");
var deployLocalGo = new Option<string>(
    "--go-exe",
    () => "go",
    "Go executable");
var deployLocalNpm = new Option<string>(
    "--npm-exe",
    () => "npm",
    "npm executable");
var deployLocalDuration = new Option<int>(
    "--duration",
    () => 5,
    "Bounded session duration in seconds (1-300)");
var deployLocalWorkDirectory = new Option<DirectoryInfo?>(
    "--work-dir",
    "New or empty workspace to preserve after the session (default: owned temporary directory)");
var deployLocalCmd = new Command(
    "local",
    "Run a verified server/report package as a bounded local session");
deployLocalCmd.Add(deployLocalBundle);
deployLocalCmd.Add(deployLocalPreflight);
deployLocalCmd.Add(deployLocalReceipt);
deployLocalCmd.Add(deployLocalReceiptOut);
deployLocalCmd.Add(deployLocalSessionOut);
deployLocalCmd.Add(deployLocalTargets);
deployLocalCmd.Add(deployLocalGo);
deployLocalCmd.Add(deployLocalNpm);
deployLocalCmd.Add(deployLocalDuration);
deployLocalCmd.Add(deployLocalWorkDirectory);
deployLocalCmd.SetHandler(async context =>
    {
        var bundle = context.ParseResult.GetValueForArgument(deployLocalBundle);
        var preflight = context.ParseResult.GetValueForOption(deployLocalPreflight)!;
        var receipt = context.ParseResult.GetValueForOption(deployLocalReceipt);
        var receiptOut = context.ParseResult.GetValueForOption(deployLocalReceiptOut);
        var sessionOut = context.ParseResult.GetValueForOption(deployLocalSessionOut);
        var requestedTargets = context.ParseResult.GetValueForOption(deployLocalTargets);
        var goExecutable = context.ParseResult.GetValueForOption(deployLocalGo)!;
        var npmExecutable = context.ParseResult.GetValueForOption(deployLocalNpm)!;
        var duration = context.ParseResult.GetValueForOption(deployLocalDuration);
        var workDir = context.ParseResult.GetValueForOption(deployLocalWorkDirectory);
        context.ExitCode = await LocalDeploymentHandler.HandleAsync(
            bundle,
            preflight,
            receipt,
            receiptOut,
            sessionOut,
            requestedTargets,
            goExecutable,
            npmExecutable,
            duration,
            workDir);
    });
var deployLocalStatusSession = new Option<FileInfo>(
    "--session",
    "Deployment session file published by 'junexis deploy local'")
{
    IsRequired = true
};
var deployLocalStopOwnerTool = new Option<string?>(
    "--owner-tool",
    "Active owner tool required to stop a session with a live owner lease");
var deployLocalStopOwnerInstanceId = new Option<string?>(
    "--owner-instance-id",
    "Active owner instance identity required to stop a session with a live owner lease");
var deployLocalStopJson = new Option<bool>(
    "--json",
    "Emit the updated deployment session as JSON");
var deployLocalStatusJson = new Option<bool>("--json", "Emit the deployment session as JSON");
var deployLocalStatusCmd = new Command(
    "status",
    "Read a deterministic deployment-session.json snapshot for bounded local deployment");
deployLocalStatusCmd.Add(deployLocalStatusSession);
deployLocalStatusCmd.Add(deployLocalStatusJson);
deployLocalStatusCmd.SetHandler(LocalDeploymentHandler.ReportStatusAsync, deployLocalStatusSession, deployLocalStatusJson);
deployLocalCmd.Add(deployLocalStatusCmd);
var deployLocalStopSession = new Option<FileInfo>(
    "--session",
    "Deployment session file published by 'junexis deploy local'")
{
    IsRequired = true
};
var deployLocalStopCmd = new Command(
    "stop",
    "Stop a deterministic local deployment session only when trusted ownership is no longer active");
deployLocalStopCmd.Add(deployLocalStopSession);
deployLocalStopCmd.Add(deployLocalStopOwnerTool);
deployLocalStopCmd.Add(deployLocalStopOwnerInstanceId);
deployLocalStopCmd.Add(deployLocalStopJson);
deployLocalStopCmd.SetHandler(
    LocalDeploymentHandler.StopSessionAsync,
    deployLocalStopSession,
    deployLocalStopOwnerTool,
    deployLocalStopOwnerInstanceId,
    deployLocalStopJson);
deployLocalCmd.Add(deployLocalStopCmd);
var deployLocalAdoptSession = new Option<FileInfo>(
    "--session",
    "Deployment session file to adopt")
{
    IsRequired = true
};
var deployLocalAdoptOwnerTool = new Option<string>(
    "--owner-tool",
    "Logical tool identity taking over the session lease")
{
    IsRequired = true
};
var deployLocalAdoptOwnerInstanceId = new Option<string>(
    "--owner-instance-id",
    "Stable host instance identity taking over the session lease")
{
    IsRequired = true
};
var deployLocalAdoptLeaseSeconds = new Option<int>(
    "--lease-seconds",
    () => 30,
    "Lease duration granted to the adopting owner (5-3600)");
var deployLocalAdoptOwnerProcessId = new Option<int>(
    "--owner-process-id",
    "Process ID of the long-lived host taking or renewing the lease")
{
    IsRequired = true
};
var deployLocalAdoptJson = new Option<bool>("--json", "Emit the adopted session as JSON");
var deployLocalAdoptCmd = new Command(
    "adopt",
    "Explicitly adopt an orphaned local deployment session for a named owner identity");
deployLocalAdoptCmd.Add(deployLocalAdoptSession);
deployLocalAdoptCmd.Add(deployLocalAdoptOwnerTool);
deployLocalAdoptCmd.Add(deployLocalAdoptOwnerInstanceId);
deployLocalAdoptCmd.Add(deployLocalAdoptLeaseSeconds);
deployLocalAdoptCmd.Add(deployLocalAdoptOwnerProcessId);
deployLocalAdoptCmd.Add(deployLocalAdoptJson);
deployLocalAdoptCmd.SetHandler(
    LocalDeploymentHandler.AdoptSessionAsync,
    deployLocalAdoptSession,
    deployLocalAdoptOwnerTool,
    deployLocalAdoptOwnerInstanceId,
    deployLocalAdoptOwnerProcessId,
    deployLocalAdoptLeaseSeconds,
    deployLocalAdoptJson);
deployLocalCmd.Add(deployLocalAdoptCmd);
deployCmd.Add(deployLocalCmd);

var deployEmbeddedPlatformIoExe = new Option<string>(
    "--platformio-exe",
    () => "pio",
    "PlatformIO executable used for embedded device discovery");
var deployEmbeddedJson = new Option<bool>("--json", "Emit embedded device probe as JSON");
var deployEmbeddedProbeCmd = new Command(
    "probe",
    "Detect PlatformIO and enumerate candidate embedded devices without flashing");
deployEmbeddedProbeCmd.Add(deployEmbeddedPlatformIoExe);
deployEmbeddedProbeCmd.Add(deployEmbeddedJson);
deployEmbeddedProbeCmd.SetHandler(EmbeddedProbeHandler.HandleAsync, deployEmbeddedPlatformIoExe, deployEmbeddedJson);
var deployEmbeddedSelectPort = new Option<string>(
    "--port",
    "Detected serial port to bind into an embedded device selection contract")
{
    IsRequired = true
};
var deployEmbeddedSelectionOut = new Option<FileInfo?>(
    "--selection-out",
    "Embedded device selection output path (default: ./embedded-device-selection.json)");
var deployEmbeddedSelectCmd = new Command(
    "select",
    "Persist one explicitly selected embedded device from the current PlatformIO probe");
deployEmbeddedSelectCmd.Add(deployEmbeddedPlatformIoExe);
deployEmbeddedSelectCmd.Add(deployEmbeddedSelectPort);
deployEmbeddedSelectCmd.Add(deployEmbeddedSelectionOut);
deployEmbeddedSelectCmd.Add(deployEmbeddedJson);
deployEmbeddedSelectCmd.SetHandler(
    EmbeddedDeploymentHandler.SelectAsync,
    deployEmbeddedPlatformIoExe,
    deployEmbeddedSelectPort,
    deployEmbeddedSelectionOut,
    deployEmbeddedJson);
var deployEmbeddedFlashBundle = new Argument<FileInfo>(
    "bundle",
    "Verified System Bundle ZIP with embedded artifacts");
var deployEmbeddedFlashReceipt = new Option<FileInfo?>(
    "--receipt",
    "Package receipt (default: <bundle>.package-receipt.json)");
var deployEmbeddedFlashSelection = new Option<FileInfo>(
    "--selection",
    "Embedded device selection file published by 'junexis deploy embedded select'")
{
    IsRequired = true
};
var deployEmbeddedFlashWorkDir = new Option<DirectoryInfo?>(
    "--work-dir",
    "New or empty workspace to preserve after embedded flash (default: owned temporary directory)");
var deployEmbeddedFlashReceiptOut = new Option<FileInfo?>(
    "--receipt-out",
    "Embedded flash receipt output path (default: <selection-dir>/embedded-flash-receipt.json)");
var deployEmbeddedFlashAuthPolicy = new Option<string>(
    "--auth-policy",
    () => "permissive",
    "System Bundle authenticity policy: permissive, signed, or trusted");
var deployEmbeddedFlashSignature = new Option<FileInfo?>(
    "--signature",
    "Detached System Bundle signature (default: <bundle>.signature.json)");
var deployEmbeddedFlashTrustedSigners = new Option<string[]>(
    "--trusted-signer",
    "Trusted signer SHA-256 fingerprint (repeatable)");
var deployEmbeddedFlashCmd = new Command(
    "flash",
    "Upload verified embedded artifacts to one explicitly selected device");
deployEmbeddedFlashCmd.Add(deployEmbeddedFlashBundle);
deployEmbeddedFlashCmd.Add(deployEmbeddedFlashReceipt);
deployEmbeddedFlashCmd.Add(deployEmbeddedFlashSelection);
deployEmbeddedFlashCmd.Add(deployEmbeddedPlatformIoExe);
deployEmbeddedFlashCmd.Add(deployEmbeddedFlashWorkDir);
deployEmbeddedFlashCmd.Add(deployEmbeddedFlashReceiptOut);
deployEmbeddedFlashCmd.Add(deployEmbeddedFlashAuthPolicy);
deployEmbeddedFlashCmd.Add(deployEmbeddedFlashSignature);
deployEmbeddedFlashCmd.Add(deployEmbeddedFlashTrustedSigners);
deployEmbeddedFlashCmd.Add(deployEmbeddedJson);
deployEmbeddedFlashCmd.SetHandler(async context =>
{
    context.ExitCode = await EmbeddedDeploymentHandler.FlashAsync(
        context.ParseResult.GetValueForArgument(deployEmbeddedFlashBundle),
        context.ParseResult.GetValueForOption(deployEmbeddedFlashReceipt),
        context.ParseResult.GetValueForOption(deployEmbeddedFlashSelection)!,
        context.ParseResult.GetValueForOption(deployEmbeddedPlatformIoExe)!,
        context.ParseResult.GetValueForOption(deployEmbeddedFlashWorkDir),
        context.ParseResult.GetValueForOption(deployEmbeddedFlashReceiptOut),
        context.ParseResult.GetValueForOption(deployEmbeddedJson),
        context.ParseResult.GetValueForOption(deployEmbeddedFlashAuthPolicy)!,
        context.ParseResult.GetValueForOption(deployEmbeddedFlashSignature),
        context.ParseResult.GetValueForOption(deployEmbeddedFlashTrustedSigners));
});
var deployEmbeddedCmd = new Command(
    "embedded",
    "Inspect embedded deployment prerequisites without mutating hardware");
deployEmbeddedCmd.Add(deployEmbeddedProbeCmd);
deployEmbeddedCmd.Add(deployEmbeddedSelectCmd);
deployEmbeddedCmd.Add(deployEmbeddedFlashCmd);
deployCmd.Add(deployEmbeddedCmd);

// ─── bundle create/verify ───────────────────────────────────────────
var bundleProject = new Argument<string?>(
    "project",
    () => null,
    "Project directory (default: discovered project)");
var bundleOutput = new Option<FileInfo?>(
    ["--out", "-o"],
    "Destination ZIP (default: <project-out>/system/<name>-<build>.system-bundle.zip)");
var bundleCreateCmd = new Command(
    "create",
    "Create Experimental System Bundle 0.1 from the latest project build");
bundleCreateCmd.Add(bundleProject);
bundleCreateCmd.Add(bundleOutput);
bundleCreateCmd.SetHandler(SystemBundleHandler.CreateAsync, bundleProject, bundleOutput);

var bundleFile = new Argument<FileInfo>("bundle", "System Bundle ZIP to verify offline");
var bundleVerifyCmd = new Command(
    "verify",
    "Verify System Bundle integrity, authenticity, and admission policy");
var bundleSignature = new Option<FileInfo?>(
    "--signature",
    "Detached signature (default: <bundle>.signature.json)");
var bundleAuthenticityPolicy = new Option<string>(
    "--auth-policy",
    () => "permissive",
    "Authenticity policy: permissive, signed, or trusted");
var bundleTrustedSigners = new Option<string[]>(
    "--trusted-signer",
    "Trusted signer SHA-256 fingerprint (repeatable)");
bundleVerifyCmd.Add(bundleFile);
bundleVerifyCmd.Add(bundleSignature);
bundleVerifyCmd.Add(bundleAuthenticityPolicy);
bundleVerifyCmd.Add(bundleTrustedSigners);
bundleVerifyCmd.SetHandler(
    SystemBundleHandler.VerifyAsync,
    bundleFile,
    bundleSignature,
    bundleAuthenticityPolicy,
    bundleTrustedSigners);

var bundleSignFile = new Argument<FileInfo>("bundle", "Verified System Bundle ZIP to sign");
var bundleSignKey = new Option<FileInfo>("--key", "Externally managed ECDSA P-256 private key PEM")
{
    IsRequired = true
};
var bundleSigner = new Option<string>("--signer", "Human-readable signer name")
{
    IsRequired = true
};
var bundleSignatureOutput = new Option<FileInfo?>(
    ["--out", "-o"],
    "Detached signature output (default: <bundle>.signature.json)");
var bundleSignCmd = new Command(
    "sign",
    "Sign the exact System Bundle manifest with an external ECDSA P-256 key");
bundleSignCmd.Add(bundleSignFile);
bundleSignCmd.Add(bundleSignKey);
bundleSignCmd.Add(bundleSigner);
bundleSignCmd.Add(bundleSignatureOutput);
bundleSignCmd.SetHandler(
    SystemBundleHandler.SignAsync,
    bundleSignFile,
    bundleSignKey,
    bundleSigner,
    bundleSignatureOutput);

var bundleCmd = new Command(
    "bundle",
    "Create or verify an experimental Junexis System Bundle");
bundleCmd.Add(bundleCreateCmd);
bundleCmd.Add(bundleVerifyCmd);
bundleCmd.Add(bundleSignCmd);

// ─── package [project] ──────────────────────────────────────────────
var packageProject = new Argument<string?>(
    "project",
    () => null,
    "Project directory (default: discovered project)");
var packageOutput = new Option<FileInfo?>(
    ["--out", "-o"],
    "Destination ZIP (default: <project-out>/system/<name>-<build>.system-bundle.zip)");
var packageTargets = new Option<string[]>(
    ["--target", "-t"],
    "Target: embedded, server, report (repeatable; default: project targets)");
var packageGoExe = new Option<string>("--go-exe", () => "go", "Go executable");
var packageNpmExe = new Option<string>("--npm-exe", () => "npm", "npm executable");
var packagePlatformIoExe = new Option<string>(
    "--platformio-exe",
    () => "pio",
    "PlatformIO executable");
var packageSignKey = new Option<FileInfo?>(
    "--sign-key",
    "Externally managed ECDSA P-256 private key PEM");
var packageSigner = new Option<string?>(
    "--signer",
    "Human-readable signer name (required with --sign-key)");
var packageStorageRuntimeHost = new Option<string?>(
    "--storage-runtime-host",
    "Storage Runtime host executable or .dll included for a bound storage project");
var packageCmd = new Command(
    "package",
    "Preflight native toolchains, build, create, and verify a System Bundle 0.1");
packageCmd.Add(packageProject);
packageCmd.Add(packageOutput);
packageCmd.Add(packageTargets);
packageCmd.Add(packageGoExe);
packageCmd.Add(packageNpmExe);
packageCmd.Add(packagePlatformIoExe);
packageCmd.Add(packageSignKey);
packageCmd.Add(packageSigner);
packageCmd.Add(packageStorageRuntimeHost);
packageCmd.SetHandler(async context =>
{
    context.ExitCode = await PackageHandler.HandleAsync(
        context.ParseResult.GetValueForArgument(packageProject),
        context.ParseResult.GetValueForOption(packageOutput),
        context.ParseResult.GetValueForOption(packageTargets),
        context.ParseResult.GetValueForOption(packageGoExe)!,
        context.ParseResult.GetValueForOption(packageNpmExe)!,
        context.ParseResult.GetValueForOption(packagePlatformIoExe)!,
        context.ParseResult.GetValueForOption(packageSignKey),
        context.ParseResult.GetValueForOption(packageSigner),
        context.ParseResult.GetValueForOption(packageStorageRuntimeHost));
});

// ─── lab package/verify/run ────────────────────────────────────────
var labPackageProject = new Argument<string?>(
    "project",
    () => null,
    "Katalog projektu laboratorium (domyślnie: wykryty projekt)");
var labPackageOutput = new Option<FileInfo?>(
    ["--out", "-o"],
    "Docelowa paczka ZIP (domyślnie: <out>/laboratory/<id>-<wersja>-<runtime>.junexis-lab.zip)");
var labPackageGoExe = new Option<string>("--go-exe", () => "go", "Plik wykonywalny Go");
var labPackageRuntime = new Option<string?>(
    "--runtime",
    "Docelowy RID: win-x64, linux-x64 albo osx-arm64 (domyślnie: bieżący system)");
var labPackageCmd = new Command(
    "package",
    "Zbuduj samodzielną, zweryfikowaną paczkę laboratorium dla wybranego systemu");
labPackageCmd.Add(labPackageProject);
labPackageCmd.Add(labPackageOutput);
labPackageCmd.Add(labPackageGoExe);
labPackageCmd.Add(labPackageRuntime);
labPackageCmd.SetHandler(async context =>
{
    context.ExitCode = await LaboratoryHandler.PackageAsync(
        context.ParseResult.GetValueForArgument(labPackageProject),
        context.ParseResult.GetValueForOption(labPackageOutput),
        context.ParseResult.GetValueForOption(labPackageGoExe)!,
        context.ParseResult.GetValueForOption(labPackageRuntime));
});

var labVerifyFile = new Argument<FileInfo>("package", "Paczka .junexis-lab.zip do sprawdzenia");
var labVerifyJson = new Option<bool>("--json", "Zapisz wynik jako JSON");
var labVerifyCmd = new Command("verify", "Sprawdź integralność paczki laboratorium offline");
labVerifyCmd.Add(labVerifyFile);
labVerifyCmd.Add(labVerifyJson);
labVerifyCmd.SetHandler(LaboratoryHandler.VerifyAsync, labVerifyFile, labVerifyJson);

var labRunFile = new Argument<FileInfo>("package", "Zweryfikowana paczka .junexis-lab.zip");
var labRunHttpPort = new Option<int>("--http-port", () => 8080, "Port panelu i API HTTP");
var labRunHealthPort = new Option<int>("--health-port", () => 8081, "Port kontroli zdrowia");
var labRunTcpPort = new Option<int>("--tcp-port", () => 9000, "Port komunikacji TCP");
var labRunDuration = new Option<int>(
    "--duration",
    () => 0,
    "Automatyczne zatrzymanie po liczbie sekund; 0 oznacza pracę do Ctrl+C");
var labRunTraceUrl = new Option<string?>(
    "--trace-url",
    "Adres TraceCollectora do rekonstrukcji przyczyn");
var labRunCmd = new Command(
    "run",
    "Uruchom gotowe laboratorium bez checkoutu źródeł i kompilacji");
labRunCmd.Add(labRunFile);
labRunCmd.Add(labRunHttpPort);
labRunCmd.Add(labRunHealthPort);
labRunCmd.Add(labRunTcpPort);
labRunCmd.Add(labRunDuration);
labRunCmd.Add(labRunTraceUrl);
labRunCmd.SetHandler(
    LaboratoryHandler.RunAsync,
    labRunFile,
    labRunHttpPort,
    labRunHealthPort,
    labRunTcpPort,
    labRunDuration,
    labRunTraceUrl);

var labCmd = new Command(
    "lab",
    "Pobierz, sprawdź i uruchom samodzielne Laboratorium Junexis");
labCmd.Add(labPackageCmd);
labCmd.Add(labVerifyCmd);
labCmd.Add(labRunCmd);

root.Add(compileCmd);
root.Add(initCmd);
root.Add(parseCmd);
root.Add(checkCmd);
root.Add(restoreCmd);
root.Add(runCmd);
root.Add(analyzeCmd);
root.Add(cleanCmd);
root.Add(doctorCmd);
root.Add(sdkCmd);
root.Add(externCmd);
root.Add(reportCmd);
root.Add(runtimeCmd);
root.Add(conformanceCmd);
root.Add(lspCmd);
root.Add(validateCmd);
root.Add(gateCmd);
root.Add(deployCmd);
root.Add(bundleCmd);
root.Add(packageCmd);
root.Add(labCmd);

return await root.InvokeAsync(args);
