using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Flowstrand.Editor
{
    internal static class FlowGraphAiContextExporter
    {
        public static string Export(FlowGraph graph)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            ContextDocument document = new ContextDocument
            {
                graphName = graph.name,
                assetPath = AssetDatabase.GetAssetPath(graph),
                formatVersion = graph.FormatVersion
            };

            FlowGraphValidationResult validation = FlowGraphValidator.Validate(graph);
            for (int i = 0; i < validation.Issues.Count; i++)
            {
                FlowGraphIssue issue = validation.Issues[i];
                document.validationIssues.Add(new IssueRecord
                {
                    severity = issue.Severity.ToString(),
                    message = issue.Message,
                    nodeId = issue.NodeId,
                    edgeId = issue.EdgeId
                });
            }

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                FlowNode node = graph.Nodes[i];
                if (node == null)
                {
                    continue;
                }

                NodeRecord record = new NodeRecord
                {
                    id = node.Id,
                    type = node.GetType().FullName,
                    position = node.Position,
                    serializedData = JsonUtility.ToJson(node)
                };
                AddPorts(node.InputPorts, record.inputs);
                AddPorts(node.OutputPorts, record.outputs);
                document.nodes.Add(record);
            }

            for (int i = 0; i < graph.Edges.Count; i++)
            {
                FlowEdge edge = graph.Edges[i];
                if (edge == null)
                {
                    continue;
                }

                document.edges.Add(new EdgeRecord
                {
                    id = edge.Id,
                    outputNodeId = edge.OutputNodeId,
                    outputPortId = edge.OutputPortId,
                    inputNodeId = edge.InputNodeId,
                    inputPortId = edge.InputPortId
                });
            }

            return JsonUtility.ToJson(document, true);
        }

        private static void AddPorts(
            IReadOnlyList<FlowPort> source,
            ICollection<PortRecord> destination)
        {
            for (int i = 0; i < source.Count; i++)
            {
                FlowPort port = source[i];
                destination.Add(new PortRecord
                {
                    id = port.Id,
                    name = port.DisplayName,
                    direction = port.Direction.ToString(),
                    capacity = port.Capacity.ToString()
                });
            }
        }

        [Serializable]
        private sealed class ContextDocument
        {
            public string schema = "flowstrand.ai-context.v1";
            public string graphName;
            public string assetPath;
            public int formatVersion;
            public List<IssueRecord> validationIssues = new List<IssueRecord>();
            public List<NodeRecord> nodes = new List<NodeRecord>();
            public List<EdgeRecord> edges = new List<EdgeRecord>();
        }

        [Serializable]
        private sealed class IssueRecord
        {
            public string severity;
            public string message;
            public string nodeId;
            public string edgeId;
        }

        [Serializable]
        private sealed class NodeRecord
        {
            public string id;
            public string type;
            public Vector2 position;
            public string serializedData;
            public List<PortRecord> inputs = new List<PortRecord>();
            public List<PortRecord> outputs = new List<PortRecord>();
        }

        [Serializable]
        private sealed class PortRecord
        {
            public string id;
            public string name;
            public string direction;
            public string capacity;
        }

        [Serializable]
        private sealed class EdgeRecord
        {
            public string id;
            public string outputNodeId;
            public string outputPortId;
            public string inputNodeId;
            public string inputPortId;
        }
    }
}
