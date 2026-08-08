using UnityEditor;
using UnityEngine;

namespace Flowstrand.Editor
{
    [CustomEditor(typeof(FlowGraph))]
    internal sealed class FlowGraphInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            FlowGraph graph = (FlowGraph)target;

            EditorGUILayout.LabelField("Format Version", graph.FormatVersion.ToString());
            EditorGUILayout.LabelField("Nodes", graph.Nodes.Count.ToString());
            EditorGUILayout.LabelField("Edges", graph.Edges.Count.ToString());
            EditorGUILayout.Space();

            if (GUILayout.Button("Open Flow Graph"))
            {
                FlowGraphEditorWindow.Open(graph);
            }
        }
    }
}
