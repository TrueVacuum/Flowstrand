using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using GraphPort = UnityEditor.Experimental.GraphView.Port;

namespace Flowstrand.Editor
{
    internal sealed class FlowNodeView : Node
    {
        private readonly FlowGraph _graph;
        private readonly Dictionary<string, GraphPort> _inputPorts =
            new Dictionary<string, GraphPort>(StringComparer.Ordinal);
        private readonly Dictionary<string, GraphPort> _outputPorts =
            new Dictionary<string, GraphPort>(StringComparer.Ordinal);
        private readonly List<FieldInfo> _serializedFields;
        private readonly Action<FlowNode, int> _changeDynamicPortCount;

        public FlowNodeView(
            FlowGraph graph,
            FlowNode node,
            Action<FlowNode, int> changeDynamicPortCount)
        {
            _graph = graph;
            _changeDynamicPortCount = changeDynamicPortCount;
            Node = node;
            _serializedFields = GetVisibleSerializedFields(node.GetType());
            viewDataKey = node.Id;
            title = GetTitle(node.GetType());
            style.borderTopLeftRadius = 6f;
            style.borderTopRightRadius = 6f;
            style.borderBottomLeftRadius = 6f;
            style.borderBottomRightRadius = 6f;

            if (node is EntryNode && CountEntryNodes(graph) <= 1)
            {
                capabilities &= ~Capabilities.Deletable;
                capabilities &= ~Capabilities.Copiable;
            }

            BuildPorts(node.InputPorts, inputContainer, _inputPorts);
            BuildPorts(node.OutputPorts, outputContainer, _outputPorts);
            BuildProperties();
            BuildDynamicPortControls();

            SetPosition(new Rect(node.Position, new Vector2(240f, 120f)));
            RefreshExpandedState();
            RefreshPorts();
        }

        public FlowNode Node { get; }

        public void SetRuntimeState(FlowNodeRuntimeState runtimeState)
        {
            EnableInClassList(
                "flow-node-running",
                runtimeState == FlowNodeRuntimeState.Running);

            Color highlight;
            float width;
            switch (runtimeState)
            {
                case FlowNodeRuntimeState.Running:
                    highlight = new Color32(73, 210, 255, 255);
                    width = 3f;
                    break;
                case FlowNodeRuntimeState.Succeeded:
                    highlight = new Color32(83, 200, 112, 255);
                    width = 2f;
                    break;
                case FlowNodeRuntimeState.Waiting:
                    highlight = new Color32(238, 184, 74, 255);
                    width = 2f;
                    break;
                case FlowNodeRuntimeState.Failed:
                    highlight = new Color32(238, 76, 76, 255);
                    width = 3f;
                    break;
                case FlowNodeRuntimeState.Cancelled:
                    highlight = new Color32(232, 158, 68, 255);
                    width = 2f;
                    break;
                default:
                    ClearRuntimeStyle();
                    return;
            }

            style.borderLeftColor = highlight;
            style.borderRightColor = highlight;
            style.borderTopColor = highlight;
            style.borderBottomColor = highlight;
            style.borderLeftWidth = width;
            style.borderRightWidth = width;
            style.borderTopWidth = width;
            style.borderBottomWidth = width;
        }

        private void ClearRuntimeStyle()
        {
            style.borderLeftColor = StyleKeyword.Null;
            style.borderRightColor = StyleKeyword.Null;
            style.borderTopColor = StyleKeyword.Null;
            style.borderBottomColor = StyleKeyword.Null;
            style.borderLeftWidth = StyleKeyword.Null;
            style.borderRightWidth = StyleKeyword.Null;
            style.borderTopWidth = StyleKeyword.Null;
            style.borderBottomWidth = StyleKeyword.Null;
        }

        public bool TryGetPort(
            string portId,
            FlowPortDirection direction,
            out GraphPort port)
        {
            return (direction == FlowPortDirection.Input ? _inputPorts : _outputPorts)
                .TryGetValue(portId, out port);
        }

        private void BuildPorts(
            IReadOnlyList<FlowPort> ports,
            VisualElement container,
            IDictionary<string, GraphPort> lookup)
        {
            for (int i = 0; i < ports.Count; i++)
            {
                FlowPort model = ports[i];
                Direction direction = model.Direction == FlowPortDirection.Input
                    ? Direction.Input
                    : Direction.Output;
                Port.Capacity capacity = model.Capacity == FlowPortCapacity.Single
                    ? Port.Capacity.Single
                    : Port.Capacity.Multi;

                GraphPort view = InstantiatePort(
                    Orientation.Horizontal,
                    direction,
                    capacity,
                    typeof(FlowPort));
                view.portName = model.DisplayName;
                view.userData = model;
                lookup.Add(model.Id, view);
                container.Add(view);
            }
        }

        private void BuildProperties()
        {
            if (_serializedFields.Count == 0)
            {
                return;
            }

            IMGUIContainer properties = new IMGUIContainer(DrawProperties)
            {
                name = "flow-node-properties"
            };
            extensionContainer.Add(properties);
        }

        private void BuildDynamicPortControls()
        {
            if (Node is not IDynamicFlowPortNode dynamicNode)
            {
                return;
            }

            Button removeButton = new Button(() =>
                _changeDynamicPortCount?.Invoke(Node, -1))
            {
                text = "−",
                tooltip = "Remove the last flow port"
            };
            removeButton.SetEnabled(
                dynamicNode.DynamicPortCount > dynamicNode.MinimumDynamicPortCount);

            Button addButton = new Button(() =>
                _changeDynamicPortCount?.Invoke(Node, 1))
            {
                text = "+",
                tooltip = "Add a flow port"
            };

            titleButtonContainer.Add(removeButton);
            titleButtonContainer.Add(addButton);
        }

        private void DrawProperties()
        {
            if (_graph == null || Node == null)
            {
                return;
            }

            SerializedObject serializedGraph = new SerializedObject(_graph);
            SerializedProperty nodeProperty = FindNodeProperty(serializedGraph, Node.Id);
            if (nodeProperty == null)
            {
                return;
            }

            serializedGraph.Update();
            for (int i = 0; i < _serializedFields.Count; i++)
            {
                SerializedProperty property = nodeProperty.FindPropertyRelative(
                    _serializedFields[i].Name);
                if (property == null)
                {
                    continue;
                }

                EditorGUILayout.PropertyField(property, true);
            }

            if (serializedGraph.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(_graph);
            }
        }

        private static List<FieldInfo> GetVisibleSerializedFields(Type nodeType)
        {
            Stack<Type> hierarchy = new Stack<Type>();
            Type current = nodeType;
            while (current != null && typeof(FlowNode).IsAssignableFrom(current))
            {
                hierarchy.Push(current);
                current = current.BaseType;
            }

            List<FieldInfo> result = new List<FieldInfo>();
            while (hierarchy.Count > 0)
            {
                FieldInfo[] fields = hierarchy.Pop().GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                Array.Sort(fields, (left, right) =>
                    left.MetadataToken.CompareTo(right.MetadataToken));

                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    bool explicitlySerialized =
                        field.IsDefined(typeof(SerializeField), true) ||
                        field.IsDefined(typeof(SerializeReference), true);
                    bool hidden = field.IsDefined(typeof(HideInInspector), true);

                    if (!field.IsStatic &&
                        !field.IsInitOnly &&
                        !field.IsLiteral &&
                        !field.IsNotSerialized &&
                        !hidden &&
                        (field.IsPublic || explicitlySerialized))
                    {
                        result.Add(field);
                    }
                }
            }

            return result;
        }

        private static SerializedProperty FindNodeProperty(
            SerializedObject serializedGraph,
            string nodeId)
        {
            SerializedProperty nodes = serializedGraph.FindProperty("_nodes");
            for (int i = 0; i < nodes.arraySize; i++)
            {
                SerializedProperty candidate = nodes.GetArrayElementAtIndex(i);
                if (candidate.managedReferenceValue is FlowNode node && node.Id == nodeId)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string GetTitle(Type type)
        {
            object[] attributes = type.GetCustomAttributes(typeof(FlowNodeMenuAttribute), false);
            if (attributes.Length > 0)
            {
                string path = ((FlowNodeMenuAttribute)attributes[0]).Path;
                int separator = path.LastIndexOf('/');
                return separator >= 0 ? path.Substring(separator + 1) : path;
            }

            string name = type.Name.EndsWith("Node", StringComparison.Ordinal)
                ? type.Name.Substring(0, type.Name.Length - 4)
                : type.Name;
            return ObjectNames.NicifyVariableName(name);
        }

        private static int CountEntryNodes(FlowGraph graph)
        {
            int count = 0;
            if (graph == null)
            {
                return count;
            }

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                if (graph.Nodes[i] is EntryNode)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
