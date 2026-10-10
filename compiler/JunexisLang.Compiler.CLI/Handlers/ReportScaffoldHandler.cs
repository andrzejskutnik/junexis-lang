using JunexisLang.Compiler.CLI.Project;
using JunexisLang.Compiler.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JunexisLang.Compiler.CLI.Handlers;

internal static class ReportScaffoldHandler
{
    internal static async Task<int> HandleAsync(
        string? inputPath,
        bool dryRun,
        string framework,
        bool declarative)
    {
        framework = framework.Trim().ToLowerInvariant();
        if (declarative)
        {
            if (framework != "dom")
            {
                await Console.Error.WriteLineAsync(
                    "error: --declarative cannot be combined with --framework");
                return 2;
            }
            framework = "declarative";
        }
        if (framework is not ("dom" or "declarative" or "react" or "vue"))
        {
            await Console.Error.WriteLineAsync(
                $"error: unknown report framework '{framework}'; expected dom, declarative, react, or vue");
            return 2;
        }

        ResolvedCliInput resolved;
        try
        {
            resolved = await CliProjectInputResolver.ResolveAsync(inputPath);
        }
        catch (CliInputException ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            return 1;
        }

        if (resolved.Project is null)
        {
            await Console.Error.WriteLineAsync(
                "error: report scaffold requires a Junexis project with junexis.project.toml");
            return 1;
        }
        if (!(resolved.Targets ?? []).Contains("report", StringComparer.OrdinalIgnoreCase))
        {
            await Console.Error.WriteLineAsync(
                "error: report scaffold requires the project target 'report'");
            return 1;
        }

        if (framework == "declarative")
        {
            var view = await CreateDeclarativeTemplateAsync(resolved.SourceFile);
            if (view is null)
                return 1;
            await CreateIfMissingAsync(
                Path.Combine(resolved.Project.RootDirectory.FullName, "user", "report", "view.json"),
                "user/report/view.json",
                view,
                dryRun);
        }
        else
        {
            const string adapterRelative = "user/report/adapter.ts";
            var adapterPath = Path.Combine(
                resolved.Project.RootDirectory.FullName,
                "user",
                "report",
                "adapter.ts");
            await CreateIfMissingAsync(adapterPath, adapterRelative, AdapterTemplates[framework], dryRun);
        }

        if (framework is "react" or "vue")
        {
            const string packageRelative = "user/report/package.json";
            var packagePath = Path.Combine(
                resolved.Project.RootDirectory.FullName,
                "user",
                "report",
                "package.json");
            await CreateIfMissingAsync(packagePath, packageRelative, PackageTemplates[framework], dryRun);
        }

        Console.WriteLine((resolved.Targets ?? []).Contains("server", StringComparer.OrdinalIgnoreCase)
            ? "next: junexis run . --duration 30"
            : "next: junexis build .");
        return 0;
    }

    private static async Task<string?> CreateDeclarativeTemplateAsync(FileInfo sourceFile)
    {
        var check = await new JunexisCompiler().CheckAsync(new CheckOptions(sourceFile.FullName));
        if (!check.Success || check.Ast is null)
        {
            await Console.Error.WriteLineAsync(
                "error: source must pass 'junexis check' before scaffolding a declarative report");
            return null;
        }
        var structural = StructuralAnalysisStage.Analyze(check.Ast);
        if (!structural.Success || structural.Document is null)
        {
            await Console.Error.WriteLineAsync(
                "error: source structural analysis failed before scaffolding a declarative report");
            return null;
        }
        var tasks = structural.Document.Tasks
            .Where(task => task.Platforms.Any(platform => platform.Target == "report"))
            .Select(task => task.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var panels = new JsonArray(tasks.Select(task => (JsonNode)new JsonObject
        {
            ["task"] = task,
            ["title"] = task,
            ["fields"] = new JsonArray()
        }).ToArray());
        var root = new JsonObject
        {
            ["$schema"] = ReportViewCustomization.Schema,
            ["title"] = "Panel systemu",
            ["layout"] = "grid",
            ["panels"] = panels
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            + Environment.NewLine;
    }

    private static async Task CreateIfMissingAsync(
        string path,
        string relative,
        string content,
        bool dryRun)
    {
        if (File.Exists(path))
        {
            Console.WriteLine($"skip existing {relative}");
            return;
        }
        if (dryRun)
        {
            Console.WriteLine($"would create {relative}");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
        Console.WriteLine($"created {relative}");
    }

    private const string DomTemplate = """
        // Developer-owned Junexis report adapter. This file is never overwritten.
        import type {
          JunexisReportAdapter,
          ReportAdapterObservation,
          ReportConnectionState
        } from './report-adapter-contract.js';

        let observationHost: HTMLElement | null = null;
        let connectionHost: HTMLElement | null = null;

        function renderObservation(observation: ReportAdapterObservation): void {
          if (!observationHost) return;
          observationHost.replaceChildren();
          const heading = document.createElement('h2');
          heading.textContent = observation.taskName;
          const value = document.createElement('pre');
          value.textContent = JSON.stringify(observation.item, null, 2);
          const history = document.createElement('p');
          history.textContent = `Historia: ${observation.history.length} / ${observation.historyLimit}`;
          observationHost.append(heading, value, history);
        }

        function renderConnection(state: ReportConnectionState, detail: string): void {
          if (!connectionHost) return;
          connectionHost.dataset.state = state;
          connectionHost.textContent = detail;
        }

        export const userReportAdapter: JunexisReportAdapter = {
          mount({ panels, taskNames }) {
            if (!panels) return false;
            panels.replaceChildren();
            const shell = document.createElement('section');
            shell.className = 'report-panel';
            connectionHost = document.createElement('p');
            connectionHost.setAttribute('role', 'status');
            connectionHost.textContent = 'Uruchamianie własnego interfejsu…';
            observationHost = document.createElement('div');
            observationHost.textContent = `Oczekiwanie na: ${taskNames.join(', ')}`;
            shell.append(connectionHost, observationHost);
            panels.appendChild(shell);
            return true;
          },
          onObservation(observation) {
            renderObservation(observation);
            return true;
          },
          onConnection({ state, detail }) {
            renderConnection(state, detail);
          }
        };
        """;

    private const string ReactTemplate = """
        // Developer-owned Junexis React adapter. This file is never overwritten.
        import * as React from 'react';
        import { createRoot, type Root } from 'react-dom/client';
        import type {
          JunexisReportAdapter,
          ReportAdapterObservation,
          ReportConnectionState
        } from './report-adapter-contract.js';

        let root: Root | null = null;
        let observation: ReportAdapterObservation | null = null;
        let connection: { state: ReportConnectionState; detail: string } = {
          state: 'connecting',
          detail: 'Łączenie…'
        };

        function render(): void {
          if (!root) return;
          root.render(React.createElement(
            'main',
            { className: 'report-panel' },
            React.createElement('p', { role: 'status', 'data-state': connection.state }, connection.detail),
            React.createElement('h2', null, observation?.taskName ?? 'Panel Junexis'),
            React.createElement('pre', null, observation ? JSON.stringify(observation.item, null, 2) : 'Oczekiwanie na dane…')
          ));
        }

        export const userReportAdapter: JunexisReportAdapter = {
          mount({ panels }) {
            if (!panels) return false;
            panels.replaceChildren();
            root = createRoot(panels);
            render();
            return true;
          },
          onObservation(next) {
            observation = next;
            render();
            return true;
          },
          onConnection(next) {
            connection = next;
            render();
          }
        };
        """;

    private const string VueTemplate = """
        // Developer-owned Junexis Vue adapter. This file is never overwritten.
        import { createApp, h, reactive } from 'vue';
        import type {
          JunexisReportAdapter,
          ReportAdapterObservation,
          ReportConnectionState
        } from './report-adapter-contract.js';

        const state = reactive<{
          observation: ReportAdapterObservation | null;
          connection: ReportConnectionState;
          detail: string;
        }>({ observation: null, connection: 'connecting', detail: 'Łączenie…' });

        export const userReportAdapter: JunexisReportAdapter = {
          mount({ panels }) {
            if (!panels) return false;
            panels.replaceChildren();
            createApp({
              render: () => h('main', { class: 'report-panel' }, [
                h('p', { role: 'status', 'data-state': state.connection }, state.detail),
                h('h2', state.observation?.taskName ?? 'Panel Junexis'),
                h('pre', state.observation ? JSON.stringify(state.observation.item, null, 2) : 'Oczekiwanie na dane…')
              ])
            }).mount(panels);
            return true;
          },
          onObservation(observation) {
            state.observation = observation;
            return true;
          },
          onConnection({ state: connection, detail }) {
            state.connection = connection;
            state.detail = detail;
          }
        };
        """;

    private static readonly IReadOnlyDictionary<string, string> AdapterTemplates =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dom"] = DomTemplate,
            ["react"] = ReactTemplate,
            ["vue"] = VueTemplate
        };

    private static readonly IReadOnlyDictionary<string, string> PackageTemplates =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["react"] = """
                {
                  "$schema": "junexis.report-package/0.1",
                  "dependencies": {
                    "react": "^19.1.0",
                    "react-dom": "^19.1.0"
                  },
                  "devDependencies": {
                    "@types/react": "^19.1.0",
                    "@types/react-dom": "^19.1.0"
                  }
                }
                """,
            ["vue"] = """
                {
                  "$schema": "junexis.report-package/0.1",
                  "dependencies": {
                    "vue": "^3.5.0"
                  }
                }
                """
        };
}
