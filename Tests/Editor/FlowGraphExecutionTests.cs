using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Flowstrand.Tests
{
    public sealed class FlowGraphExecutionTests
    {
        private FlowGraph _graph;
        private EntryNode _entry;

        [SetUp]
        public void SetUp()
        {
            _graph = ScriptableObject.CreateInstance<FlowGraph>();
            _entry = _graph.CreateNode<EntryNode>(Vector2.zero);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_graph);
        }

        [Test]
        public void SequentialGraph_EntersNodesInOrderAndCompletes()
        {
            DelayNode first = CreateDelay(0f);
            DelayNode second = CreateDelay(0f);
            Connect(_entry, FlowPortIds.Completed, first, FlowPortIds.Enter);
            Connect(first, FlowPortIds.Completed, second, FlowPortIds.Enter);
            List<string> entered = new List<string>();
            FlowGraphExecution execution = new FlowGraphExecution(_graph);
            execution.NodeEntered += node => entered.Add(node.Id);

            Assert.That(execution.Start(), Is.True);
            execution.Tick(0f);

            Assert.That(execution.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
            CollectionAssert.AreEqual(
                new[] { _entry.Id, first.Id, second.Id },
                entered);
        }

        [Test]
        public void Branch_UsesBlackboardValueToSelectOneOutput()
        {
            BranchNode branch = _graph.CreateNode<BranchNode>(Vector2.right);
            JsonUtility.FromJsonOverwrite(
                "{\"_blackboardKey\":\"condition\",\"_fallbackValue\":false}",
                branch);
            DelayNode trueNode = CreateDelay(0f);
            DelayNode falseNode = CreateDelay(0f);
            Connect(_entry, FlowPortIds.Completed, branch, FlowPortIds.Enter);
            Connect(branch, FlowPortIds.True, trueNode, FlowPortIds.Enter);
            Connect(branch, FlowPortIds.False, falseNode, FlowPortIds.Enter);
            FlowBlackboard blackboard = new FlowBlackboard();
            blackboard.Set("condition", true);
            List<string> entered = new List<string>();
            FlowGraphExecution execution = new FlowGraphExecution(_graph, blackboard);
            execution.NodeEntered += node => entered.Add(node.Id);

            execution.Start();
            execution.Tick(0f);

            Assert.That(entered, Does.Contain(trueNode.Id));
            Assert.That(entered, Does.Not.Contain(falseNode.Id));
            Assert.That(execution.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
        }

        [Test]
        public void Parallel_AdvancesBranchesIndependently()
        {
            ParallelNode parallel = _graph.CreateNode<ParallelNode>(Vector2.right);
            DelayNode shortDelay = CreateDelay(0.5f);
            DelayNode longDelay = CreateDelay(1f);
            Connect(_entry, FlowPortIds.Completed, parallel, FlowPortIds.Enter);
            Connect(parallel, parallel.OutputPorts[0].Id, shortDelay, FlowPortIds.Enter);
            Connect(parallel, parallel.OutputPorts[1].Id, longDelay, FlowPortIds.Enter);
            FlowGraphExecution execution = new FlowGraphExecution(_graph);

            execution.Start();
            execution.Tick(0f);
            Assert.That(execution.ActiveBranchCount, Is.EqualTo(2));

            execution.Tick(0.5f);
            Assert.That(execution.Status, Is.EqualTo(FlowExecutionStatus.Running));
            Assert.That(execution.ActiveBranchCount, Is.EqualTo(1));
            Assert.That(
                execution.GetNodeRuntimeState(shortDelay.Id),
                Is.EqualTo(FlowNodeRuntimeState.Succeeded));
            Assert.That(
                execution.GetNodeRuntimeState(longDelay.Id),
                Is.EqualTo(FlowNodeRuntimeState.Running));

            execution.Tick(0.5f);
            Assert.That(execution.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
        }

        [Test]
        public void JoinAll_WaitsForEveryInputBeforeContinuing()
        {
            ParallelNode parallel = _graph.CreateNode<ParallelNode>(Vector2.right);
            DelayNode immediate = CreateDelay(0f);
            DelayNode delayed = CreateDelay(1f);
            JoinAllNode join = _graph.CreateNode<JoinAllNode>(Vector2.right * 2f);
            DelayNode afterJoin = CreateDelay(0f);
            Connect(_entry, FlowPortIds.Completed, parallel, FlowPortIds.Enter);
            Connect(parallel, parallel.OutputPorts[0].Id, immediate, FlowPortIds.Enter);
            Connect(parallel, parallel.OutputPorts[1].Id, delayed, FlowPortIds.Enter);
            Connect(immediate, FlowPortIds.Completed, join, join.InputPorts[0].Id);
            Connect(delayed, FlowPortIds.Completed, join, join.InputPorts[1].Id);
            Connect(join, FlowPortIds.Completed, afterJoin, FlowPortIds.Enter);
            FlowGraphExecution execution = new FlowGraphExecution(_graph);

            execution.Start();
            execution.Tick(0f);

            Assert.That(execution.Status, Is.EqualTo(FlowExecutionStatus.Running));
            Assert.That(
                execution.GetNodeRuntimeState(join.Id),
                Is.EqualTo(FlowNodeRuntimeState.Waiting));
            Assert.That(
                execution.GetNodeRuntimeState(afterJoin.Id),
                Is.EqualTo(FlowNodeRuntimeState.NotVisited));

            execution.Tick(1f);

            Assert.That(execution.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
            Assert.That(
                execution.GetNodeRuntimeState(join.Id),
                Is.EqualTo(FlowNodeRuntimeState.Succeeded));
            Assert.That(
                execution.GetNodeRuntimeState(afterJoin.Id),
                Is.EqualTo(FlowNodeRuntimeState.Succeeded));
        }

        [Test]
        public void JoinAny_ContinuesOnlyOncePerInputRound()
        {
            ParallelNode parallel = _graph.CreateNode<ParallelNode>(Vector2.right);
            DelayNode immediate = CreateDelay(0f);
            DelayNode delayed = CreateDelay(1f);
            JoinAnyNode join = _graph.CreateNode<JoinAnyNode>(Vector2.right * 2f);
            DelayNode afterJoin = CreateDelay(0f);
            Connect(_entry, FlowPortIds.Completed, parallel, FlowPortIds.Enter);
            Connect(parallel, parallel.OutputPorts[0].Id, immediate, FlowPortIds.Enter);
            Connect(parallel, parallel.OutputPorts[1].Id, delayed, FlowPortIds.Enter);
            Connect(immediate, FlowPortIds.Completed, join, join.InputPorts[0].Id);
            Connect(delayed, FlowPortIds.Completed, join, join.InputPorts[1].Id);
            Connect(join, FlowPortIds.Completed, afterJoin, FlowPortIds.Enter);
            List<string> entered = new List<string>();
            FlowGraphExecution execution = new FlowGraphExecution(_graph);
            execution.NodeEntered += node => entered.Add(node.Id);

            execution.Start();
            execution.Tick(0f);
            Assert.That(entered.Count(id => id == afterJoin.Id), Is.EqualTo(1));
            Assert.That(execution.Status, Is.EqualTo(FlowExecutionStatus.Running));

            execution.Tick(1f);

            Assert.That(entered.Count(id => id == afterJoin.Id), Is.EqualTo(1));
            Assert.That(execution.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
        }

        [Test]
        public void NestedParallel_CrossLayerJoinsEachContinueOnce()
        {
            ParallelNode outer = _graph.CreateNode<ParallelNode>(Vector2.right);
            outer.AddDynamicPort();
            DelayNode allPath = CreateDelay(0f);
            ParallelNode inner = _graph.CreateNode<ParallelNode>(Vector2.right * 2f);
            DelayNode anyDelayedPath = CreateDelay(1f);
            JoinAllNode joinAll = _graph.CreateNode<JoinAllNode>(Vector2.right * 3f);
            JoinAnyNode joinAny = _graph.CreateNode<JoinAnyNode>(Vector2.right * 3f);
            DelayNode afterAll = CreateDelay(0f);
            DelayNode afterAny = CreateDelay(0f);

            Connect(_entry, FlowPortIds.Completed, outer, FlowPortIds.Enter);
            Connect(outer, outer.OutputPorts[0].Id, allPath, FlowPortIds.Enter);
            Connect(outer, outer.OutputPorts[1].Id, inner, FlowPortIds.Enter);
            Connect(outer, outer.OutputPorts[2].Id, anyDelayedPath, FlowPortIds.Enter);
            Connect(allPath, FlowPortIds.Completed, joinAll, joinAll.InputPorts[0].Id);
            Connect(inner, inner.OutputPorts[0].Id, joinAll, joinAll.InputPorts[1].Id);
            Connect(inner, inner.OutputPorts[1].Id, joinAny, joinAny.InputPorts[0].Id);
            Connect(anyDelayedPath, FlowPortIds.Completed, joinAny, joinAny.InputPorts[1].Id);
            Connect(joinAll, FlowPortIds.Completed, afterAll, FlowPortIds.Enter);
            Connect(joinAny, FlowPortIds.Completed, afterAny, FlowPortIds.Enter);

            List<string> entered = new List<string>();
            FlowGraphExecution execution = new FlowGraphExecution(_graph);
            execution.NodeEntered += node => entered.Add(node.Id);

            execution.Start();
            execution.Tick(0f);

            Assert.That(entered.Count(id => id == afterAll.Id), Is.EqualTo(1));
            Assert.That(entered.Count(id => id == afterAny.Id), Is.EqualTo(1));
            Assert.That(execution.Status, Is.EqualTo(FlowExecutionStatus.Running));

            execution.Tick(1f);

            Assert.That(entered.Count(id => id == afterAll.Id), Is.EqualTo(1));
            Assert.That(entered.Count(id => id == afterAny.Id), Is.EqualTo(1));
            Assert.That(execution.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
        }

        [Test]
        public void Stop_CancelsTheActiveNode()
        {
            DelayNode delay = CreateDelay(10f);
            Connect(_entry, FlowPortIds.Completed, delay, FlowPortIds.Enter);
            FlowGraphExecution execution = new FlowGraphExecution(_graph);

            execution.Start();
            execution.Tick(0f);
            execution.Stop();

            Assert.That(execution.Status, Is.EqualTo(FlowExecutionStatus.Stopped));
            Assert.That(
                execution.GetNodeRuntimeState(delay.Id),
                Is.EqualTo(FlowNodeRuntimeState.Cancelled));
        }

        [Test]
        public void ImmediateCycle_FailsAtTransitionLimit()
        {
            DelayNode first = CreateDelay(0f);
            DelayNode second = CreateDelay(0f);
            Connect(_entry, FlowPortIds.Completed, first, FlowPortIds.Enter);
            Connect(first, FlowPortIds.Completed, second, FlowPortIds.Enter);
            Connect(second, FlowPortIds.Completed, first, FlowPortIds.Enter);
            FlowGraphExecution execution = new FlowGraphExecution(_graph);
            LogAssert.Expect(LogType.Error, new Regex("exceeded 8 immediate"));

            execution.Start();
            execution.Tick(0f, 8);

            Assert.That(execution.Status, Is.EqualTo(FlowExecutionStatus.Failed));
        }

        [Test]
        public void NodeException_FailsExecutionAndMarksNodeFailed()
        {
            ThrowingTestNode throwing = (ThrowingTestNode)_graph.CreateNode(
                typeof(ThrowingTestNode),
                Vector2.right);
            Connect(_entry, FlowPortIds.Completed, throwing, FlowPortIds.Enter);
            FlowGraphExecution execution = new FlowGraphExecution(_graph);
            LogAssert.Expect(LogType.Exception, new Regex("Flowstrand test exception"));

            execution.Start();
            execution.Tick(0f);

            Assert.That(execution.Status, Is.EqualTo(FlowExecutionStatus.Failed));
            Assert.That(
                execution.GetNodeRuntimeState(throwing.Id),
                Is.EqualTo(FlowNodeRuntimeState.Failed));
        }

        private DelayNode CreateDelay(float seconds)
        {
            DelayNode node = _graph.CreateNode<DelayNode>(Vector2.zero);
            JsonUtility.FromJsonOverwrite($"{{\"_seconds\":{seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}", node);
            return node;
        }

        private void Connect(
            FlowNode output,
            string outputPort,
            FlowNode input,
            string inputPort)
        {
            _graph.Connect(output.Id, outputPort, input.Id, inputPort);
        }
    }

    [System.Serializable]
    internal sealed class ThrowingTestNode : FlowNode
    {
        public override FlowNodeStatus OnUpdate(FlowNodeContext context)
        {
            throw new System.InvalidOperationException("Flowstrand test exception");
        }
    }
}
