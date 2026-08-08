using UnityEditor;
using UnityEngine;

namespace Flowstrand.Editor
{
    internal static class FlowGraphAssetMenu
    {
        [MenuItem("Assets/Create/Flowstrand/Flow Graph")]
        private static void CreateFlowGraph()
        {
            FlowGraph graph = ScriptableObject.CreateInstance<FlowGraph>();
            graph.CreateNode<EntryNode>(new Vector2(100f, 100f));
            ProjectWindowUtil.CreateAsset(graph, "FlowGraph.asset");
        }
    }
}
