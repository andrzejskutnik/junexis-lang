using JunexisLang.Compiler.LspHost;

if (args is ["--version"])
{
    Console.WriteLine(LspHostIdentity.RenderJson());
    return 0;
}

if (args is ["--distribution-manifest", var runtimeIdentifier, var entrypoint, var selfContainedText])
{
    if (!bool.TryParse(selfContainedText, out var selfContained))
    {
        Console.Error.WriteLine("selfContained must be true or false.");
        return 2;
    }

    try
    {
        Console.WriteLine(LspHostIdentity.RenderDistributionManifest(
            runtimeIdentifier,
            entrypoint,
            selfContained));
        return 0;
    }
    catch (ArgumentException error)
    {
        Console.Error.WriteLine(error.Message);
        return 2;
    }
}

if (args.Length > 0 && args[0] == "--distribution-manifest")
{
    Console.Error.WriteLine(
        "Usage: --distribution-manifest <runtimeIdentifier> <entrypoint> <selfContained>");
    return 2;
}

return await LspHostRunner.RunAsync();

