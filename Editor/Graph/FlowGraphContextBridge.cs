using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UpmPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Flowstrand.Editor
{
    [InitializeOnLoad]
    internal static class FlowGraphContextBridge
    {
        private const double PollInterval = 0.2d;
        private const string InstructionsBegin =
            "<!-- FLOWSTRAND_AI_INTEGRATION_BEGIN -->";
        private const string InstructionsEnd =
            "<!-- FLOWSTRAND_AI_INTEGRATION_END -->";
        private static readonly string BridgeRoot;
        private static readonly string RequestDirectory;
        private static readonly string ResponseDirectory;
        private static double _nextPollTime;

        static FlowGraphContextBridge()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            BridgeRoot = Path.Combine(projectRoot ?? string.Empty, "Library", "Flowstrand", "ContextBridge");
            RequestDirectory = Path.Combine(BridgeRoot, "Requests");
            ResponseDirectory = Path.Combine(BridgeRoot, "Responses");
            Directory.CreateDirectory(RequestDirectory);
            Directory.CreateDirectory(ResponseDirectory);
            InstallProjectAiIntegration(projectRoot);
            EditorApplication.update += Poll;
        }

        private static void InstallProjectAiIntegration(string projectRoot)
        {
            if (string.IsNullOrEmpty(projectRoot))
            {
                return;
            }

            try
            {
                string toolsDirectory = Path.Combine(
                    projectRoot,
                    "Library",
                    "Flowstrand",
                    "Tools");
                Directory.CreateDirectory(toolsDirectory);

                UpmPackageInfo packageInfo = UpmPackageInfo.FindForAssembly(
                    typeof(FlowGraphContextBridge).Assembly);
                string sourceScript = packageInfo != null
                    ? Path.Combine(
                        packageInfo.resolvedPath,
                        "Tools~",
                        "Get-FlowGraphContext.ps1")
                    : null;
                string installedScript = Path.Combine(
                    toolsDirectory,
                    "Get-FlowGraphContext.ps1");
                if (!string.IsNullOrEmpty(sourceScript) && File.Exists(sourceScript))
                {
                    string scriptContent = File.ReadAllText(sourceScript);
                    WriteIfChanged(installedScript, scriptContent);
                }

                string agentsPath = Path.Combine(projectRoot, "AGENTS.md");
                string existing = File.Exists(agentsPath)
                    ? File.ReadAllText(agentsPath)
                    : "# Project Agent Instructions\n";
                string block = BuildAgentInstructions();
                string updated = ReplaceOrAppendManagedBlock(existing, block);
                WriteIfChanged(agentsPath, updated);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Flowstrand could not initialize AI integration: {exception.Message}");
            }
        }

        private static string BuildAgentInstructions()
        {
            return string.Join("\n", new[]
            {
                InstructionsBegin,
                "## Flowstrand graph access",
                string.Empty,
                "When asked to inspect a Flowstrand Flow Graph, invoke the project-local context tool yourself; do not ask the user to copy or export the graph manually.",
                string.Empty,
                "PowerShell:",
                "```powershell",
                "& \"<PROJECT_ROOT>\\Library\\Flowstrand\\Tools\\Get-FlowGraphContext.ps1\" -ProjectPath \"<PROJECT_ROOT>\" -Graph \"<GRAPH_NAME_OR_ASSET_PATH>\"",
                "```",
                string.Empty,
                "Pass an empty `-Graph` value to list available graphs. The Unity Editor must have this project open and must have finished compiling. The returned context is exported from the live ScriptableObject through Unity's AssetDatabase, so prefer it over parsing `.asset` YAML.",
                InstructionsEnd
            });
        }

        private static string ReplaceOrAppendManagedBlock(string existing, string block)
        {
            int begin = existing.IndexOf(InstructionsBegin, StringComparison.Ordinal);
            int end = existing.IndexOf(InstructionsEnd, StringComparison.Ordinal);
            if (begin >= 0 && end >= begin)
            {
                end += InstructionsEnd.Length;
                return existing.Substring(0, begin) + block + existing.Substring(end);
            }

            string trimmed = existing.TrimEnd();
            return string.IsNullOrEmpty(trimmed)
                ? block + "\n"
                : trimmed + "\n\n" + block + "\n";
        }

        private static void WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) &&
                string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            {
                return;
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPollTime)
            {
                return;
            }

            _nextPollTime = EditorApplication.timeSinceStartup + PollInterval;
            string[] requestFiles;
            try
            {
                requestFiles = Directory.GetFiles(RequestDirectory, "*.json");
            }
            catch (IOException)
            {
                return;
            }

            for (int i = 0; i < requestFiles.Length; i++)
            {
                ProcessRequest(requestFiles[i]);
            }
        }

        private static void ProcessRequest(string requestPath)
        {
            BridgeResponse response = new BridgeResponse();
            string requestId = Path.GetFileNameWithoutExtension(requestPath);
            try
            {
                BridgeRequest request = JsonUtility.FromJson<BridgeRequest>(
                    File.ReadAllText(requestPath));
                if (request == null || string.IsNullOrWhiteSpace(request.id))
                {
                    throw new InvalidDataException("The context request has no ID.");
                }

                requestId = request.id;
                response = Resolve(request.query);
                response.requestId = request.id;
            }
            catch (Exception exception)
            {
                response.success = false;
                response.requestId = requestId;
                response.error = exception.Message;
            }

            try
            {
                Directory.CreateDirectory(ResponseDirectory);
                File.WriteAllText(
                    Path.Combine(ResponseDirectory, $"{requestId}.json"),
                    JsonUtility.ToJson(response, true));
                File.Delete(requestPath);
            }
            catch (IOException exception)
            {
                Debug.LogException(exception);
            }
        }

        private static BridgeResponse Resolve(string query)
        {
            string[] guids = AssetDatabase.FindAssets("t:FlowGraph");
            List<string> paths = new List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return new BridgeResponse
                {
                    success = true,
                    matches = paths.ToArray()
                };
            }

            List<string> exactMatches = new List<string>();
            List<string> partialMatches = new List<string>();
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                string assetName = Path.GetFileNameWithoutExtension(path);
                if (string.Equals(assetName, query, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path, query, StringComparison.OrdinalIgnoreCase))
                {
                    exactMatches.Add(path);
                }
                else if (assetName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    partialMatches.Add(path);
                }
            }

            List<string> matches = exactMatches.Count > 0 ? exactMatches : partialMatches;
            if (matches.Count != 1)
            {
                return new BridgeResponse
                {
                    success = false,
                    error = matches.Count == 0
                        ? $"No Flow Graph matches '{query}'."
                        : $"More than one Flow Graph matches '{query}'. Use an asset path.",
                    matches = matches.ToArray()
                };
            }

            FlowGraph graph = AssetDatabase.LoadAssetAtPath<FlowGraph>(matches[0]);
            if (graph == null)
            {
                return new BridgeResponse
                {
                    success = false,
                    error = $"Could not load Flow Graph at '{matches[0]}'."
                };
            }

            return new BridgeResponse
            {
                success = true,
                graphName = graph.name,
                assetPath = matches[0],
                context = FlowGraphAiContextExporter.Export(graph),
                matches = new[] { matches[0] }
            };
        }

        [Serializable]
        private sealed class BridgeRequest
        {
            public string id;
            public string query;
        }

        [Serializable]
        private sealed class BridgeResponse
        {
            public bool success;
            public string requestId;
            public string graphName;
            public string assetPath;
            public string context;
            public string error;
            public string[] matches = Array.Empty<string>();
        }
    }
}
