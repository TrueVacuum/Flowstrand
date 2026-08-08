using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Flowstrand.Tests
{
    public sealed class FlowGraphSerializationTests
    {
        [Test]
        public void DuplicateNode_CopiesConfigurationButGeneratesNewIdentity()
        {
            FlowGraph graph = ScriptableObject.CreateInstance<FlowGraph>();
            try
            {
                DelayNode original = graph.CreateNode<DelayNode>(new Vector2(10f, 20f));
                JsonUtility.FromJsonOverwrite("{\"_seconds\":2.5}", original);

                DelayNode copy = (DelayNode)graph.DuplicateNode(
                    original,
                    new Vector2(30f, 40f));

                Assert.That(copy.Id, Is.Not.EqualTo(original.Id));
                Assert.That(copy.Position, Is.EqualTo(new Vector2(30f, 40f)));
                Assert.That(JsonUtility.ToJson(copy), Does.Contain("2.5"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void DuplicateDynamicNode_PreservesStablePortIdsWithinTheCopy()
        {
            FlowGraph graph = ScriptableObject.CreateInstance<FlowGraph>();
            try
            {
                ParallelNode original = graph.CreateNode<ParallelNode>(Vector2.zero);
                original.AddDynamicPort();

                ParallelNode copy = (ParallelNode)graph.DuplicateNode(original, Vector2.one);

                Assert.That(copy.Id, Is.Not.EqualTo(original.Id));
                Assert.That(copy.OutputPorts.Count, Is.EqualTo(original.OutputPorts.Count));
                for (int i = 0; i < original.OutputPorts.Count; i++)
                {
                    Assert.That(copy.OutputPorts[i].Id, Is.EqualTo(original.OutputPorts[i].Id));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void AssetRoundTrip_PreservesManagedNodeTypesConfigurationAndEdges()
        {
            string folder = $"Assets/__FlowstrandTests_{Guid.NewGuid():N}";
            string assetPath = $"{folder}/RoundTrip.asset";
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            FlowGraph graph = ScriptableObject.CreateInstance<FlowGraph>();

            try
            {
                EntryNode entry = graph.CreateNode<EntryNode>(Vector2.zero);
                DelayNode delay = graph.CreateNode<DelayNode>(Vector2.one);
                JsonUtility.FromJsonOverwrite("{\"_seconds\":3.25}", delay);
                graph.Connect(
                    entry.Id,
                    FlowPortIds.Completed,
                    delay.Id,
                    FlowPortIds.Enter);
                AssetDatabase.CreateAsset(graph, assetPath);
                EditorUtility.SetDirty(graph);
                AssetDatabase.SaveAssets();
                Resources.UnloadAsset(graph);

                FlowGraph loaded = AssetDatabase.LoadAssetAtPath<FlowGraph>(assetPath);

                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded.Nodes.Count, Is.EqualTo(2));
                Assert.That(loaded.Nodes[0], Is.TypeOf<EntryNode>());
                Assert.That(loaded.Nodes[1], Is.TypeOf<DelayNode>());
                Assert.That(JsonUtility.ToJson(loaded.Nodes[1]), Does.Contain("3.25"));
                Assert.That(loaded.Edges.Count, Is.EqualTo(1));
                Assert.That(loaded.Edges[0].OutputNodeId, Is.EqualTo(loaded.Nodes[0].Id));
                Assert.That(loaded.Edges[0].InputNodeId, Is.EqualTo(loaded.Nodes[1].Id));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
