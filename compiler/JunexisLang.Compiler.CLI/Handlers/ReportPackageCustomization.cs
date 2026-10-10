using System.Text.Json;
using System.Text.Json.Nodes;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class ReportPackageCustomization
{
    internal const string Schema = "junexis.report-package/0.1";

    internal static bool TryMerge(
        string generatedPackage,
        string userPackage,
        out string mergedPackage,
        out string error)
    {
        mergedPackage = generatedPackage;
        error = string.Empty;

        try
        {
            var generated = JsonNode.Parse(generatedPackage) as JsonObject
                ?? throw new InvalidDataException("generated report/package.json must be a JSON object");
            var user = JsonNode.Parse(userPackage) as JsonObject
                ?? throw new InvalidDataException("user/report/package.json must be a JSON object");

            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "$schema",
                "dependencies",
                "devDependencies"
            };
            var unsupported = user.Select(property => property.Key)
                .Where(key => !allowed.Contains(key))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            if (unsupported.Length > 0)
                throw new InvalidDataException(
                    $"unsupported field(s): {string.Join(", ", unsupported)}; only $schema, dependencies and devDependencies are allowed");

            if (user["$schema"] is JsonValue schemaValue
                && (!schemaValue.TryGetValue<string>(out var schema) || schema != Schema))
                throw new InvalidDataException($"$schema must equal '{Schema}'");

            MergeSection(generated, user, "dependencies");
            MergeSection(generated, user, "devDependencies");
            mergedPackage = generated.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static bool TryValidate(string userPackage, out string error) =>
        TryMerge(
            "{\"dependencies\":{},\"devDependencies\":{}}",
            userPackage,
            out _,
            out error);

    private static void MergeSection(JsonObject generated, JsonObject user, string sectionName)
    {
        if (user[sectionName] is null)
            return;
        if (user[sectionName] is not JsonObject additions)
            throw new InvalidDataException($"{sectionName} must be a JSON object");

        var target = generated[sectionName] as JsonObject;
        if (target is null)
        {
            target = new JsonObject();
            generated[sectionName] = target;
        }

        foreach (var property in additions.OrderBy(property => property.Key, StringComparer.Ordinal))
        {
            if (property.Value is not JsonValue value
                || !value.TryGetValue<string>(out var requestedVersion)
                || string.IsNullOrWhiteSpace(requestedVersion))
                throw new InvalidDataException($"{sectionName}.{property.Key} must be a non-empty version string");

            if (target[property.Key] is JsonValue existingValue
                && existingValue.TryGetValue<string>(out var existingVersion)
                && !string.Equals(existingVersion, requestedVersion, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"{sectionName}.{property.Key} is owned by Junexis ({existingVersion}) and cannot be replaced with {requestedVersion}");

            target[property.Key] = requestedVersion;
        }
    }
}
