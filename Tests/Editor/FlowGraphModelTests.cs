using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Flowstrand.Tests
{
    public sealed class FlowGraphModelTests
    {
        private FlowGraph _graph;

        [SetUp]
        public void SetUp()
        {
            _graph = ScriptableObject.CreateInstance<FlowGraph>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_graph);
        }

        [Test]
        public void Entry_IsUniqueAndCannotBeRemoved()
        {
            EntryNode entry = _graph.CreateNode<EntryNode>(Vector2.zero);

            Assert.Throws<InvalidOperationException>(() =>
                _graph.CreateNode<EntryNode>(Vector2.one));
            Assert.That(_graph.RemoveNode(entry.Id), Is.False);
            Assert.That(_graph.Nodes.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConnectingSingleOutput_ReplacesItsPreviousConnection()
        {
            EntryNode entry = _graph.CreateNode<EntryNode>(Vector2.zero);
            DelayNode first = _graph.CreateNode<DelayNode>(Vector2.right);
            DelayNode second = _graph.CreateNode<DelayNode>(Vector2.up);

            _graph.Connect(
                entry.Id,
                FlowPortIds.Completed,
                first.Id,
                FlowPortIds.Enter);
            _graph.Connect(
                entry.Id,
                FlowPortIds.Completed,
                second.Id,
                FlowPortIds.Enter);

            Assert.That(_graph.Edges.Count, Is.EqualTo(1));
            Assert.That(_graph.Edges[0].InputNodeId, Is.EqualTo(second.Id));
        }

        [Test]
        public void RemovingDynamicPort_CanRemoveOnlyItsOwnConnection()
        {
            ParallelNode parallel = _graph.CreateNode<ParallelNode>(Vector2.zero);
            DelayNode first = _graph.CreateNode<DelayNode>(Vector2.right);
            DelayNode second = _graph.CreateNode<DelayNode>(Vector2.up);
            string firstPort = parallel.OutputPorts[0].Id;
            string addedPort = parallel.AddDynamicPort();

            _graph.Connect(parallel.Id, firstPort, first.Id, FlowPortIds.Enter);
            _graph.Connect(parallel.Id, addedPort, second.Id, FlowPortIds.Enter);

            Assert.That(parallel.TryRemoveDynamicPort(out string removedPort), Is.True);
            Assert.That(removedPort, Is.EqualTo(addedPort));
            Assert.That(
                _graph.DisconnectPort(
                    parallel.Id,
                    removedPort,
                    FlowPortDirection.Output),
                Is.EqualTo(1));

            Assert.That(_graph.Edges.Count, Is.EqualTo(1));
            Assert.That(_graph.Edges[0].OutputPortId, Is.EqualTo(firstPort));
        }

        [Test]
        public void Validator_ReportsUnreachableNodeAsWarning()
        {
            _graph.CreateNode<EntryNode>(Vector2.zero);
            DelayNode unreachable = _graph.CreateNode<DelayNode>(Vector2.one);

            FlowGraphValidationResult result = FlowGraphValidator.Validate(_graph);

            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Issues.Any(issue =>
                issue.Severity == FlowGraphIssueSeverity.Warning &&
                issue.NodeId == unreachable.Id), Is.True);
        }

        [Test]
        public void Validator_ReportsUnconnectedJoinInputAsError()
        {
            EntryNode entry = _graph.CreateNode<EntryNode>(Vector2.zero);
            JoinAllNode join = _graph.CreateNode<JoinAllNode>(Vector2.right);
            _graph.Connect(
                entry.Id,
                FlowPortIds.Completed,
                join.Id,
                join.InputPorts[0].Id);

            FlowGraphValidationResult result = FlowGraphValidator.Validate(_graph);

            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Issues.Any(issue =>
                issue.Severity == FlowGraphIssueSeverity.Error &&
                issue.NodeId == join.Id &&
                issue.Message.Contains("Input 2")), Is.True);
        }
    }
}
