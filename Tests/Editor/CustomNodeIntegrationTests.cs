using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Flowstrand.Tests
{
    public sealed class CustomNodeIntegrationTests
    {
        [Test]
        public void ProjectDefinedNode_IsDiscoveredByEditorRegistry()
        {
            Assert.That(
                Flowstrand.Editor.FlowNodeTypeRegistry.Entries.Any(entry =>
                    entry.Type == typeof(ProjectLikeTestNode) &&
                    entry.Path == "Tests/Project Like"),
                Is.True);
        }

        [Test]
        public void ProjectDefinedNode_IsIncludedInAiContextWithoutExporterChanges()
        {
            FlowGraph graph = ScriptableObject.CreateInstance<FlowGraph>();
            graph.name = "CustomNodeGraph";
            try
            {
                EntryNode entry = graph.CreateNode<EntryNode>(Vector2.zero);
                ProjectLikeTestNode custom = graph.CreateNode<ProjectLikeTestNode>(Vector2.one);
                graph.Connect(
                    entry.Id,
                    FlowPortIds.Completed,
                    custom.Id,
                    FlowPortIds.Enter);

                string context = Flowstrand.Editor.FlowGraphAiContextExporter.Export(graph);

                Assert.That(context, Does.Contain(typeof(ProjectLikeTestNode).FullName));
                Assert.That(context, Does.Contain("_projectValue"));
                Assert.That(context, Does.Contain("42"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }
    }

    [Serializable]
    [FlowNodeMenu("Tests/Project Like")]
    public sealed class ProjectLikeTestNode : FlowNode
    {
        [SerializeField] private int _projectValue = 42;

        public int ProjectValue => _projectValue;

        public override FlowNodeStatus OnUpdate(FlowNodeContext context)
        {
            return FlowNodeStatus.Succeeded;
        }
    }
}
