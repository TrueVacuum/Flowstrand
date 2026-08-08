using System;
using System.Collections.Generic;
using UnityEngine;

namespace Flowstrand
{
    public sealed class FlowGraph : ScriptableObject
    {
        public const int CurrentFormatVersion = 1;

        [SerializeField, HideInInspector] private int _formatVersion = CurrentFormatVersion;
        [SerializeReference, HideInInspector] private List<FlowNode> _nodes = new List<FlowNode>();
        [SerializeField, HideInInspector] private List<FlowEdge> _edges = new List<FlowEdge>();

        public int FormatVersion => _formatVersion;
        public IReadOnlyList<FlowNode> Nodes => _nodes;
        public IReadOnlyList<FlowEdge> Edges => _edges;

        public FlowNode CreateNode(Type nodeType, Vector2 position)
        {
            if (nodeType == null)
            {
                throw new ArgumentNullException(nameof(nodeType));
            }

            if (nodeType.IsAbstract || !typeof(FlowNode).IsAssignableFrom(nodeType))
            {
                throw new ArgumentException(
                    $"Type '{nodeType.FullName}' must be a non-abstract FlowNode.",
                    nameof(nodeType));
            }

            if (nodeType == typeof(EntryNode) && FindEntryNode() != null)
            {
                throw new InvalidOperationException(
                    "A Flow Graph can contain only one Entry node.");
            }

            FlowNode node = (FlowNode)Activator.CreateInstance(nodeType, true);
            node.EnsureId();
            node.Position = position;
            _nodes.Add(node);
            return node;
        }

        public TNode CreateNode<TNode>(Vector2 position) where TNode : FlowNode, new()
        {
            return (TNode)CreateNode(typeof(TNode), position);
        }

        public FlowNode DuplicateNode(FlowNode source, Vector2 position)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source is EntryNode)
            {
                throw new InvalidOperationException("Entry nodes cannot be duplicated.");
            }

            Type nodeType = source.GetType();
            if (nodeType.IsAbstract || !typeof(FlowNode).IsAssignableFrom(nodeType))
            {
                throw new ArgumentException(
                    $"Type '{nodeType.FullName}' must be a non-abstract FlowNode.",
                    nameof(source));
            }

            FlowNode copy = (FlowNode)Activator.CreateInstance(nodeType, true);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), copy);
            copy.RegenerateId();
            copy.Position = position;
            _nodes.Add(copy);
            return copy;
        }

        public bool RemoveNode(string nodeId)
        {
            FlowNode target = FindNode(nodeId);
            if (target is EntryNode && CountEntryNodes() <= 1)
            {
                return false;
            }

            int removedCount = _nodes.RemoveAll(node => node != null && node.Id == nodeId);
            if (removedCount == 0)
            {
                return false;
            }

            _edges.RemoveAll(edge =>
                edge == null || edge.OutputNodeId == nodeId || edge.InputNodeId == nodeId);
            return true;
        }

        public FlowEdge Connect(
            string outputNodeId,
            string outputPortId,
            string inputNodeId,
            string inputPortId)
        {
            FlowNode outputNode = FindNode(outputNodeId);
            FlowNode inputNode = FindNode(inputNodeId);

            if (outputNode == null || inputNode == null)
            {
                throw new ArgumentException("Both edge endpoints must exist in this graph.");
            }

            if (!outputNode.TryGetPort(
                    outputPortId,
                    FlowPortDirection.Output,
                    out FlowPort outputPort))
            {
                throw new ArgumentException(
                    $"Node '{outputNodeId}' has no output port '{outputPortId}'.",
                    nameof(outputPortId));
            }

            if (!inputNode.TryGetPort(
                    inputPortId,
                    FlowPortDirection.Input,
                    out FlowPort inputPort))
            {
                throw new ArgumentException(
                    $"Node '{inputNodeId}' has no input port '{inputPortId}'.",
                    nameof(inputPortId));
            }

            if (HasConnection(outputNodeId, outputPortId, inputNodeId, inputPortId))
            {
                throw new InvalidOperationException("The requested connection already exists.");
            }

            RemoveConnectionsForSingleCapacityPort(
                outputNodeId,
                outputPortId,
                FlowPortDirection.Output,
                outputPort.Capacity);
            RemoveConnectionsForSingleCapacityPort(
                inputNodeId,
                inputPortId,
                FlowPortDirection.Input,
                inputPort.Capacity);

            FlowEdge edge = new FlowEdge(
                outputNodeId,
                outputPortId,
                inputNodeId,
                inputPortId);
            _edges.Add(edge);
            return edge;
        }

        public bool Disconnect(string edgeId)
        {
            return _edges.RemoveAll(edge => edge != null && edge.Id == edgeId) > 0;
        }

        public int DisconnectPort(
            string nodeId,
            string portId,
            FlowPortDirection direction)
        {
            return direction == FlowPortDirection.Output
                ? _edges.RemoveAll(edge =>
                    edge == null ||
                    (edge.OutputNodeId == nodeId && edge.OutputPortId == portId))
                : _edges.RemoveAll(edge =>
                    edge == null ||
                    (edge.InputNodeId == nodeId && edge.InputPortId == portId));
        }

        public FlowNode FindNode(string nodeId)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                FlowNode node = _nodes[i];
                if (node != null && string.Equals(node.Id, nodeId, StringComparison.Ordinal))
                {
                    return node;
                }
            }

            return null;
        }

        private EntryNode FindEntryNode()
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i] is EntryNode entry)
                {
                    return entry;
                }
            }

            return null;
        }

        private int CountEntryNodes()
        {
            int count = 0;
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i] is EntryNode)
                {
                    count++;
                }
            }

            return count;
        }

        private void OnEnable()
        {
            EnsureIdentifiers();
        }

        private void EnsureIdentifiers()
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                _nodes[i]?.EnsureId();
            }

            for (int i = 0; i < _edges.Count; i++)
            {
                _edges[i]?.EnsureId();
            }
        }

        private bool HasConnection(
            string outputNodeId,
            string outputPortId,
            string inputNodeId,
            string inputPortId)
        {
            for (int i = 0; i < _edges.Count; i++)
            {
                FlowEdge edge = _edges[i];
                if (edge != null &&
                    edge.OutputNodeId == outputNodeId &&
                    edge.OutputPortId == outputPortId &&
                    edge.InputNodeId == inputNodeId &&
                    edge.InputPortId == inputPortId)
                {
                    return true;
                }
            }

            return false;
        }

        private void RemoveConnectionsForSingleCapacityPort(
            string nodeId,
            string portId,
            FlowPortDirection direction,
            FlowPortCapacity capacity)
        {
            if (capacity != FlowPortCapacity.Single)
            {
                return;
            }

            if (direction == FlowPortDirection.Output)
            {
                _edges.RemoveAll(edge =>
                    edge == null ||
                    (edge.OutputNodeId == nodeId && edge.OutputPortId == portId));
            }
            else
            {
                _edges.RemoveAll(edge =>
                    edge == null ||
                    (edge.InputNodeId == nodeId && edge.InputPortId == portId));
            }
        }
    }
}
