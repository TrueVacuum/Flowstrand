using System;
using System.Collections.Generic;

namespace Flowstrand
{
    public enum FlowGraphIssueSeverity
    {
        Warning,
        Error
    }

    public sealed class FlowGraphIssue
    {
        internal FlowGraphIssue(
            FlowGraphIssueSeverity severity,
            string message,
            string nodeId = null,
            string edgeId = null)
        {
            Severity = severity;
            Message = message;
            NodeId = nodeId;
            EdgeId = edgeId;
        }

        public FlowGraphIssueSeverity Severity { get; }
        public string Message { get; }
        public string NodeId { get; }
        public string EdgeId { get; }
    }

    public sealed class FlowGraphValidationResult
    {
        internal FlowGraphValidationResult(List<FlowGraphIssue> issues)
        {
            Issues = issues;
        }

        public IReadOnlyList<FlowGraphIssue> Issues { get; }

        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i].Severity == FlowGraphIssueSeverity.Error)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    public static class FlowGraphValidator
    {
        public static FlowGraphValidationResult Validate(FlowGraph graph)
        {
            List<FlowGraphIssue> issues = new List<FlowGraphIssue>();
            if (graph == null)
            {
                issues.Add(new FlowGraphIssue(
                    FlowGraphIssueSeverity.Error,
                    "A Flow Graph is required."));
                return new FlowGraphValidationResult(issues);
            }

            Dictionary<string, FlowNode> nodes =
                new Dictionary<string, FlowNode>(StringComparer.Ordinal);
            List<FlowNode> entries = new List<FlowNode>();

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                FlowNode node = graph.Nodes[i];
                if (node == null)
                {
                    issues.Add(new FlowGraphIssue(
                        FlowGraphIssueSeverity.Error,
                        $"Node slot {i} contains missing serialized data."));
                    continue;
                }

                if (string.IsNullOrEmpty(node.Id))
                {
                    issues.Add(new FlowGraphIssue(
                        FlowGraphIssueSeverity.Error,
                        $"Node at index {i} has no stable ID."));
                }
                else if (!nodes.TryAdd(node.Id, node))
                {
                    issues.Add(new FlowGraphIssue(
                        FlowGraphIssueSeverity.Error,
                        $"Multiple nodes use ID '{node.Id}'.",
                        node.Id));
                }

                if (node is EntryNode)
                {
                    entries.Add(node);
                }

                ValidatePorts(node, node.InputPorts, FlowPortDirection.Input, issues);
                ValidatePorts(node, node.OutputPorts, FlowPortDirection.Output, issues);
            }

            if (entries.Count != 1)
            {
                issues.Add(new FlowGraphIssue(
                    FlowGraphIssueSeverity.Error,
                    entries.Count == 0
                        ? "The graph requires one Entry node."
                        : $"The graph has {entries.Count} Entry nodes; exactly one is required."));
            }

            HashSet<string> edgeIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> connections = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, int> portConnectionCounts =
                new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, List<string>> adjacency =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);

            for (int i = 0; i < graph.Edges.Count; i++)
            {
                FlowEdge edge = graph.Edges[i];
                if (edge == null)
                {
                    issues.Add(new FlowGraphIssue(
                        FlowGraphIssueSeverity.Error,
                        $"Edge slot {i} contains missing serialized data."));
                    continue;
                }

                if (string.IsNullOrEmpty(edge.Id) || !edgeIds.Add(edge.Id))
                {
                    issues.Add(new FlowGraphIssue(
                        FlowGraphIssueSeverity.Error,
                        string.IsNullOrEmpty(edge.Id)
                            ? $"Edge at index {i} has no stable ID."
                            : $"Multiple edges use ID '{edge.Id}'.",
                        edgeId: edge.Id));
                }

                if (!nodes.TryGetValue(edge.OutputNodeId, out FlowNode outputNode) ||
                    !nodes.TryGetValue(edge.InputNodeId, out FlowNode inputNode))
                {
                    issues.Add(new FlowGraphIssue(
                        FlowGraphIssueSeverity.Error,
                        "An edge references a node that no longer exists.",
                        edgeId: edge.Id));
                    continue;
                }

                bool validOutput = outputNode.TryGetPort(
                    edge.OutputPortId,
                    FlowPortDirection.Output,
                    out FlowPort outputPort);
                bool validInput = inputNode.TryGetPort(
                    edge.InputPortId,
                    FlowPortDirection.Input,
                    out FlowPort inputPort);
                if (!validOutput || !validInput)
                {
                    issues.Add(new FlowGraphIssue(
                        FlowGraphIssueSeverity.Error,
                        "An edge references a port that no longer exists.",
                        edgeId: edge.Id));
                    continue;
                }

                string connectionKey =
                    $"{edge.OutputNodeId}\n{edge.OutputPortId}\n{edge.InputNodeId}\n{edge.InputPortId}";
                if (!connections.Add(connectionKey))
                {
                    issues.Add(new FlowGraphIssue(
                        FlowGraphIssueSeverity.Error,
                        "The same connection appears more than once.",
                        edgeId: edge.Id));
                }

                CountPortConnection(
                    edge.OutputNodeId,
                    edge.OutputPortId,
                    outputPort.Capacity,
                    "output",
                    portConnectionCounts,
                    issues);
                CountPortConnection(
                    edge.InputNodeId,
                    edge.InputPortId,
                    inputPort.Capacity,
                    "input",
                    portConnectionCounts,
                    issues);

                if (!adjacency.TryGetValue(edge.OutputNodeId, out List<string> destinations))
                {
                    destinations = new List<string>();
                    adjacency.Add(edge.OutputNodeId, destinations);
                }

                destinations.Add(edge.InputNodeId);
            }

            ValidateDynamicPortConnections(graph, portConnectionCounts, issues);
            AddUnreachableNodeWarnings(entries, nodes, adjacency, issues);
            return new FlowGraphValidationResult(issues);
        }

        private static void ValidateDynamicPortConnections(
            FlowGraph graph,
            IReadOnlyDictionary<string, int> connectionCounts,
            ICollection<FlowGraphIssue> issues)
        {
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                FlowNode node = graph.Nodes[i];
                if (node is ParallelNode)
                {
                    int connectedBranches = 0;
                    IReadOnlyList<FlowPort> outputs = node.OutputPorts;
                    for (int portIndex = 0; portIndex < outputs.Count; portIndex++)
                    {
                        if (GetConnectionCount(
                                connectionCounts,
                                node.Id,
                                outputs[portIndex].Id,
                                "output") > 0)
                        {
                            connectedBranches++;
                        }
                        else
                        {
                            issues.Add(new FlowGraphIssue(
                                FlowGraphIssueSeverity.Warning,
                                $"Parallel node branch '{outputs[portIndex].DisplayName}' is not connected.",
                                node.Id));
                        }
                    }

                    if (connectedBranches < 2)
                    {
                        issues.Add(new FlowGraphIssue(
                            FlowGraphIssueSeverity.Warning,
                            "Parallel node has fewer than two connected branches and will not run in parallel.",
                            node.Id));
                    }
                }
                else if (node is IFlowJoinNode)
                {
                    IReadOnlyList<FlowPort> inputs = node.InputPorts;
                    for (int portIndex = 0; portIndex < inputs.Count; portIndex++)
                    {
                        if (GetConnectionCount(
                                connectionCounts,
                                node.Id,
                                inputs[portIndex].Id,
                                "input") == 0)
                        {
                            issues.Add(new FlowGraphIssue(
                                FlowGraphIssueSeverity.Error,
                                $"{node.GetType().Name} input '{inputs[portIndex].DisplayName}' is not connected.",
                                node.Id));
                        }
                    }
                }
            }
        }

        private static int GetConnectionCount(
            IReadOnlyDictionary<string, int> counts,
            string nodeId,
            string portId,
            string direction)
        {
            string key = $"{direction}\n{nodeId}\n{portId}";
            return counts.TryGetValue(key, out int count) ? count : 0;
        }

        private static void ValidatePorts(
            FlowNode node,
            IReadOnlyList<FlowPort> ports,
            FlowPortDirection expectedDirection,
            ICollection<FlowGraphIssue> issues)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < ports.Count; i++)
            {
                FlowPort port = ports[i];
                if (port.Direction != expectedDirection)
                {
                    issues.Add(new FlowGraphIssue(
                        FlowGraphIssueSeverity.Error,
                        $"Node '{node.GetType().Name}' declares port '{port.Id}' in the wrong direction.",
                        node.Id));
                }

                if (string.IsNullOrWhiteSpace(port.Id) || !ids.Add(port.Id))
                {
                    issues.Add(new FlowGraphIssue(
                        FlowGraphIssueSeverity.Error,
                        $"Node '{node.GetType().Name}' has an empty or duplicate port ID.",
                        node.Id));
                }
            }
        }

        private static void CountPortConnection(
            string nodeId,
            string portId,
            FlowPortCapacity capacity,
            string direction,
            IDictionary<string, int> counts,
            ICollection<FlowGraphIssue> issues)
        {
            string key = $"{direction}\n{nodeId}\n{portId}";
            counts.TryGetValue(key, out int count);
            count++;
            counts[key] = count;
            if (capacity == FlowPortCapacity.Single && count > 1)
            {
                issues.Add(new FlowGraphIssue(
                    FlowGraphIssueSeverity.Error,
                    $"Single-capacity {direction} port '{nodeId}.{portId}' has multiple connections.",
                    nodeId));
            }
        }

        private static void AddUnreachableNodeWarnings(
            IReadOnlyList<FlowNode> entries,
            IReadOnlyDictionary<string, FlowNode> nodes,
            IReadOnlyDictionary<string, List<string>> adjacency,
            ICollection<FlowGraphIssue> issues)
        {
            HashSet<string> reachable = new HashSet<string>(StringComparer.Ordinal);
            Queue<string> pending = new Queue<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (!string.IsNullOrEmpty(entries[i].Id) && reachable.Add(entries[i].Id))
                {
                    pending.Enqueue(entries[i].Id);
                }
            }

            while (pending.Count > 0)
            {
                string nodeId = pending.Dequeue();
                if (!adjacency.TryGetValue(nodeId, out List<string> destinations))
                {
                    continue;
                }

                for (int i = 0; i < destinations.Count; i++)
                {
                    if (reachable.Add(destinations[i]))
                    {
                        pending.Enqueue(destinations[i]);
                    }
                }
            }

            foreach (KeyValuePair<string, FlowNode> pair in nodes)
            {
                if (!reachable.Contains(pair.Key))
                {
                    issues.Add(new FlowGraphIssue(
                        FlowGraphIssueSeverity.Warning,
                        $"Node '{pair.Value.GetType().Name}' is unreachable from Entry.",
                        pair.Key));
                }
            }
        }
    }
}
