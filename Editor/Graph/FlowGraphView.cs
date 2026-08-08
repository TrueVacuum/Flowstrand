using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using GraphEdge = UnityEditor.Experimental.GraphView.Edge;
using GraphPort = UnityEditor.Experimental.GraphView.Port;

namespace Flowstrand.Editor
{
    internal sealed class FlowGraphView : GraphView
    {
        private const string StyleSheetPath =
            "Packages/com.truevacuum.flowstrand/Editor/Styles/FlowGraphView.uss";

        private readonly FlowGraphEditorWindow _window;
        private readonly FlowNodeSearchProvider _searchProvider;
        private readonly Dictionary<string, FlowNodeView> _nodeViews =
            new Dictionary<string, FlowNodeView>(StringComparer.Ordinal);
        private FlowGraph _graph;
        private bool _isPopulating;
        private int _pasteCount;
        private Vector2 _lastMouseGraphPosition;
        private bool _hasMouseGraphPosition;

        public FlowGraphView(FlowGraphEditorWindow window)
        {
            _window = window;
            style.flexGrow = 1f;

            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }

            GridBackground grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();
            this.AddManipulator(new ContentZoomer());
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            graphViewChanged = HandleGraphViewChanged;
            nodeCreationRequest = OpenNodeSearch;
            serializeGraphElements = SerializeSelection;
            canPasteSerializedData = CanPasteFlowData;
            unserializeAndPaste = PasteSerializedData;
            RegisterCallback<MouseMoveEvent>(
                HandleMouseMove,
                TrickleDown.TrickleDown);

            _searchProvider = ScriptableObject.CreateInstance<FlowNodeSearchProvider>();
            _searchProvider.hideFlags = HideFlags.HideAndDontSave;
        }

        public void SetGraph(FlowGraph graph)
        {
            _graph = graph;
            Populate();
        }

        public void CreateNode(Type nodeType, Vector2 graphPosition)
        {
            if (_graph == null)
            {
                return;
            }

            Undo.RecordObject(_graph, "Create Flow Node");
            FlowNode node = _graph.CreateNode(nodeType, graphPosition);
            AddNodeView(node);
            EditorUtility.SetDirty(_graph);
        }

        public void SetRuntimeExecution(FlowGraphExecution execution)
        {
            foreach (KeyValuePair<string, FlowNodeView> pair in _nodeViews)
            {
                pair.Value.SetRuntimeState(
                    execution != null
                        ? execution.GetNodeRuntimeState(pair.Key)
                        : FlowNodeRuntimeState.NotVisited);
            }
        }

        public override List<GraphPort> GetCompatiblePorts(
            GraphPort startPort,
            NodeAdapter nodeAdapter)
        {
            return ports
                .Where(port =>
                    port != startPort &&
                    port.node != startPort.node &&
                    port.direction != startPort.direction &&
                    port.portType == startPort.portType)
                .ToList();
        }

        private void Populate()
        {
            _isPopulating = true;
            foreach (GraphElement element in graphElements.ToList())
            {
                RemoveElement(element);
            }

            _nodeViews.Clear();

            if (_graph != null)
            {
                for (int i = 0; i < _graph.Nodes.Count; i++)
                {
                    FlowNode node = _graph.Nodes[i];
                    if (node != null)
                    {
                        AddNodeView(node);
                    }
                }

                for (int i = 0; i < _graph.Edges.Count; i++)
                {
                    AddEdgeView(_graph.Edges[i]);
                }
            }

            _isPopulating = false;
        }

        private void AddNodeView(FlowNode node)
        {
            FlowNodeView view = new FlowNodeView(_graph, node, ChangeDynamicPortCount);
            _nodeViews[node.Id] = view;
            AddElement(view);
        }

        private void ChangeDynamicPortCount(FlowNode node, int delta)
        {
            if (_graph == null || node is not IDynamicFlowPortNode dynamicNode || delta == 0)
            {
                return;
            }

            Undo.RecordObject(_graph, delta > 0 ? "Add Flow Port" : "Remove Flow Port");
            if (delta > 0)
            {
                dynamicNode.AddDynamicPort();
            }
            else if (dynamicNode.TryRemoveDynamicPort(out string removedPortId))
            {
                _graph.DisconnectPort(node.Id, removedPortId, dynamicNode.DynamicPortDirection);
            }
            else
            {
                return;
            }

            EditorUtility.SetDirty(_graph);
            Populate();
        }

        private void AddEdgeView(FlowEdge edge)
        {
            if (edge == null ||
                !_nodeViews.TryGetValue(edge.OutputNodeId, out FlowNodeView outputNode) ||
                !_nodeViews.TryGetValue(edge.InputNodeId, out FlowNodeView inputNode) ||
                !outputNode.TryGetPort(
                    edge.OutputPortId,
                    FlowPortDirection.Output,
                    out GraphPort outputPort) ||
                !inputNode.TryGetPort(
                    edge.InputPortId,
                    FlowPortDirection.Input,
                    out GraphPort inputPort))
            {
                return;
            }

            GraphEdge edgeView = outputPort.ConnectTo(inputPort);
            edgeView.userData = edge.Id;
            AddElement(edgeView);
        }

        private void OpenNodeSearch(NodeCreationContext context)
        {
            if (_graph == null)
            {
                return;
            }

            Vector2 windowPosition = context.screenMousePosition - _window.position.position;
            Vector2 graphPosition = contentViewContainer.WorldToLocal(windowPosition);
            _searchProvider.Initialize(this, graphPosition);
            SearchWindow.Open(
                new SearchWindowContext(context.screenMousePosition),
                _searchProvider);
        }

        private void HandleMouseMove(MouseMoveEvent evt)
        {
            Vector2 worldPosition = this.LocalToWorld(evt.localMousePosition);
            _lastMouseGraphPosition = contentViewContainer.WorldToLocal(worldPosition);
            _hasMouseGraphPosition = true;
        }

        private GraphViewChange HandleGraphViewChanged(GraphViewChange change)
        {
            if (_isPopulating || _graph == null)
            {
                return change;
            }

            bool recordedUndo = false;
            void RecordUndo(string label)
            {
                if (!recordedUndo)
                {
                    Undo.RecordObject(_graph, label);
                    recordedUndo = true;
                }
            }

            if (change.movedElements != null)
            {
                foreach (GraphElement element in change.movedElements)
                {
                    if (element is FlowNodeView nodeView)
                    {
                        RecordUndo("Move Flow Node");
                        nodeView.Node.Position = nodeView.GetPosition().position;
                    }
                }
            }

            if (change.edgesToCreate != null)
            {
                foreach (GraphEdge edgeView in change.edgesToCreate)
                {
                    if (edgeView.output?.node is not FlowNodeView outputNode ||
                        edgeView.input?.node is not FlowNodeView inputNode ||
                        edgeView.output.userData is not FlowPort outputPort ||
                        edgeView.input.userData is not FlowPort inputPort)
                    {
                        continue;
                    }

                    RecordUndo("Connect Flow Nodes");
                    FlowEdge edge = _graph.Connect(
                        outputNode.Node.Id,
                        outputPort.Id,
                        inputNode.Node.Id,
                        inputPort.Id);
                    edgeView.userData = edge.Id;
                }
            }

            if (change.elementsToRemove != null)
            {
                foreach (GraphElement element in change.elementsToRemove)
                {
                    switch (element)
                    {
                        case FlowNodeView nodeView:
                            RecordUndo("Delete Flow Node");
                            _graph.RemoveNode(nodeView.Node.Id);
                            _nodeViews.Remove(nodeView.Node.Id);
                            break;
                        case GraphEdge edgeView when edgeView.userData is string edgeId:
                            RecordUndo("Disconnect Flow Nodes");
                            _graph.Disconnect(edgeId);
                            break;
                    }
                }
            }

            if (recordedUndo)
            {
                EditorUtility.SetDirty(_graph);
            }

            return change;
        }

        private string SerializeSelection(IEnumerable<GraphElement> elements)
        {
            _pasteCount = 0;
            ClipboardData data = new ClipboardData();
            HashSet<string> selectedNodeIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (GraphElement element in elements)
            {
                if (element is not FlowNodeView nodeView)
                {
                    continue;
                }

                FlowNode node = nodeView.Node;
                if (node is EntryNode)
                {
                    continue;
                }

                selectedNodeIds.Add(node.Id);
                data.nodes.Add(new ClipboardNode
                {
                    originalId = node.Id,
                    typeName = node.GetType().AssemblyQualifiedName,
                    json = JsonUtility.ToJson(node),
                    position = node.Position
                });
            }

            if (_graph != null)
            {
                for (int i = 0; i < _graph.Edges.Count; i++)
                {
                    FlowEdge edge = _graph.Edges[i];
                    if (edge != null &&
                        selectedNodeIds.Contains(edge.OutputNodeId) &&
                        selectedNodeIds.Contains(edge.InputNodeId))
                    {
                        data.edges.Add(new ClipboardEdge
                        {
                            outputNodeId = edge.OutputNodeId,
                            outputPortId = edge.OutputPortId,
                            inputNodeId = edge.InputNodeId,
                            inputPortId = edge.InputPortId
                        });
                    }
                }
            }

            return JsonUtility.ToJson(data);
        }

        private static bool CanPasteFlowData(string serializedData)
        {
            if (string.IsNullOrWhiteSpace(serializedData))
            {
                return false;
            }

            try
            {
                ClipboardData data = JsonUtility.FromJson<ClipboardData>(serializedData);
                return data != null &&
                       data.marker == ClipboardData.Marker &&
                       data.nodes != null &&
                       data.nodes.Count > 0;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private void PasteSerializedData(string operationName, string serializedData)
        {
            if (_graph == null || !CanPasteFlowData(serializedData))
            {
                return;
            }

            ClipboardData data = JsonUtility.FromJson<ClipboardData>(serializedData);
            Undo.RecordObject(_graph, string.IsNullOrEmpty(operationName)
                ? "Paste Flow Nodes"
                : operationName);

            Vector2 sourceAnchor = data.nodes[0].position;
            for (int i = 1; i < data.nodes.Count; i++)
            {
                sourceAnchor = Vector2.Min(sourceAnchor, data.nodes[i].position);
            }

            Vector2 targetAnchor = _hasMouseGraphPosition
                ? _lastMouseGraphPosition
                : contentViewContainer.WorldToLocal(worldBound.center);
            Vector2 offset = targetAnchor - sourceAnchor + Vector2.one * (20f * _pasteCount);
            _pasteCount++;
            Dictionary<string, FlowNode> pastedNodes =
                new Dictionary<string, FlowNode>(StringComparer.Ordinal);

            ClearSelection();
            for (int i = 0; i < data.nodes.Count; i++)
            {
                ClipboardNode record = data.nodes[i];
                Type nodeType = Type.GetType(record.typeName);
                if (nodeType == null ||
                    nodeType.IsAbstract ||
                    !typeof(FlowNode).IsAssignableFrom(nodeType) ||
                    nodeType == typeof(EntryNode))
                {
                    Debug.LogWarning(
                        $"Flowstrand could not paste missing node type '{record.typeName}'.",
                        _graph);
                    continue;
                }

                FlowNode serializedNode = (FlowNode)Activator.CreateInstance(nodeType);
                JsonUtility.FromJsonOverwrite(record.json, serializedNode);
                FlowNode pastedNode = _graph.DuplicateNode(
                    serializedNode,
                    record.position + offset);
                pastedNodes[record.originalId] = pastedNode;

                AddNodeView(pastedNode);
                FlowNodeView pastedView = _nodeViews[pastedNode.Id];
                AddToSelection(pastedView);
            }

            for (int i = 0; i < data.edges.Count; i++)
            {
                ClipboardEdge record = data.edges[i];
                if (!pastedNodes.TryGetValue(record.outputNodeId, out FlowNode outputNode) ||
                    !pastedNodes.TryGetValue(record.inputNodeId, out FlowNode inputNode))
                {
                    continue;
                }

                FlowEdge pastedEdge = _graph.Connect(
                    outputNode.Id,
                    record.outputPortId,
                    inputNode.Id,
                    record.inputPortId);
                AddEdgeView(pastedEdge);
            }

            EditorUtility.SetDirty(_graph);
        }

        [Serializable]
        private sealed class ClipboardData
        {
            public const string Marker = "Flowstrand.GraphClipboard.v1";

            public string marker = Marker;
            public List<ClipboardNode> nodes = new List<ClipboardNode>();
            public List<ClipboardEdge> edges = new List<ClipboardEdge>();
        }

        [Serializable]
        private sealed class ClipboardNode
        {
            public string originalId;
            public string typeName;
            public string json;
            public Vector2 position;
        }

        [Serializable]
        private sealed class ClipboardEdge
        {
            public string outputNodeId;
            public string outputPortId;
            public string inputNodeId;
            public string inputPortId;
        }
    }
}
