using System.Text.Json;
using System.Text.Json.Nodes;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class ReportViewCustomization
{
    internal const string Schema = "junexis.report-view/0.1";

    internal static bool TryCompile(
        string source,
        IReadOnlySet<string> reportTasks,
        out string typeScript,
        out string error)
    {
        typeScript = string.Empty;
        error = string.Empty;
        try
        {
            if (source.Length > 262_144)
                throw new InvalidDataException("view configuration cannot exceed 256 KiB");
            var root = JsonNode.Parse(source) as JsonObject
                ?? throw new InvalidDataException("user/report/view.json must be a JSON object");
            RequireOnly(root, "$schema", "title", "layout", "panels");
            RequireString(root, "$schema", Schema);
            var title = RequireNonEmptyString(root, "title");
            if (title.Length > 120)
                throw new InvalidDataException("title cannot exceed 120 characters");
            var layout = RequireNonEmptyString(root, "layout");
            if (layout is not ("grid" or "list"))
                throw new InvalidDataException("layout must equal 'grid' or 'list'");
            if (root["panels"] is not JsonArray panels || panels.Count is < 1 or > 32)
                throw new InvalidDataException("panels must contain between 1 and 32 entries");

            var normalizedPanels = new JsonArray();
            var seenTasks = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in panels)
            {
                if (node is not JsonObject panel)
                    throw new InvalidDataException("each panel must be a JSON object");
                RequireOnly(panel, "task", "title", "fields");
                var task = RequireNonEmptyString(panel, "task");
                if (!reportTasks.Contains(task))
                    throw new InvalidDataException($"panel references unknown @report task '{task}'");
                if (!seenTasks.Add(task))
                    throw new InvalidDataException($"panel for task '{task}' is declared more than once");
                var panelTitle = RequireNonEmptyString(panel, "title");
                if (panelTitle.Length > 120)
                    throw new InvalidDataException($"panel title for '{task}' cannot exceed 120 characters");
                if (panel["fields"] is not JsonArray fields || fields.Count > 32)
                    throw new InvalidDataException($"fields for '{task}' must be an array with at most 32 entries");

                var normalizedFields = new JsonArray();
                foreach (var fieldNode in fields)
                {
                    if (fieldNode is not JsonObject field)
                        throw new InvalidDataException($"each field for '{task}' must be a JSON object");
                    RequireOnly(field, "path", "label", "unit", "format");
                    var path = RequireNonEmptyString(field, "path");
                    if (!System.Text.RegularExpressions.Regex.IsMatch(
                            path,
                            "^[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z_][A-Za-z0-9_]*)*$",
                            System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                        throw new InvalidDataException($"field path '{path}' must use dotted identifiers");
                    var label = RequireNonEmptyString(field, "label");
                    if (label.Length > 120)
                        throw new InvalidDataException($"label for field '{path}' cannot exceed 120 characters");
                    var format = OptionalString(field, "format") ?? "auto";
                    if (format is not ("auto" or "number" or "boolean" or "text" or "json"))
                        throw new InvalidDataException($"unsupported format '{format}' for field '{path}'");
                    var unit = OptionalString(field, "unit");
                    if (unit is { Length: > 32 })
                        throw new InvalidDataException($"unit for field '{path}' cannot exceed 32 characters");

                    var normalizedField = new JsonObject
                    {
                        ["path"] = path,
                        ["label"] = label,
                        ["format"] = format
                    };
                    if (!string.IsNullOrWhiteSpace(unit))
                        normalizedField["unit"] = unit;
                    normalizedFields.Add(normalizedField);
                }

                normalizedPanels.Add(new JsonObject
                {
                    ["task"] = task,
                    ["title"] = panelTitle,
                    ["fields"] = normalizedFields
                });
            }

            var normalized = new JsonObject
            {
                ["schema"] = Schema,
                ["title"] = title,
                ["layout"] = layout,
                ["panels"] = normalizedPanels
            };
            typeScript = """
                // Generated from developer-owned user/report/view.json.
                import type { JunexisReportView } from './report-view-contract.js';

                export const userReportView: JunexisReportView =
                """
                + normalized.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
                + ";\n";
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static bool TryValidate(string source, out string error)
    {
        try
        {
            var root = JsonNode.Parse(source) as JsonObject;
            var tasks = (root?["panels"] as JsonArray)?
                .OfType<JsonObject>()
                .Select(panel => panel["task"])
                .OfType<JsonValue>()
                .Select(value => value.TryGetValue<string>(out var task) ? task : string.Empty)
                .Where(task => !string.IsNullOrWhiteSpace(task))
                .ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
            return TryCompile(source, tasks, out _, out error);
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void RequireOnly(JsonObject value, params string[] allowedNames)
    {
        var allowed = allowedNames.ToHashSet(StringComparer.Ordinal);
        var unsupported = value.Select(property => property.Key)
            .Where(key => !allowed.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        if (unsupported.Length > 0)
            throw new InvalidDataException($"unsupported field(s): {string.Join(", ", unsupported)}");
    }

    private static void RequireString(JsonObject value, string name, string expected)
    {
        var actual = RequireNonEmptyString(value, name);
        if (actual != expected)
            throw new InvalidDataException($"{name} must equal '{expected}'");
    }

    private static string RequireNonEmptyString(JsonObject value, string name) =>
        OptionalString(value, name) is { Length: > 0 } result
            ? result
            : throw new InvalidDataException($"{name} must be a non-empty string");

    private static string? OptionalString(JsonObject value, string name)
    {
        if (value[name] is null)
            return null;
        if (value[name] is not JsonValue json
            || !json.TryGetValue<string>(out var result))
            throw new InvalidDataException($"{name} must be a string");
        return result.Trim();
    }
}
