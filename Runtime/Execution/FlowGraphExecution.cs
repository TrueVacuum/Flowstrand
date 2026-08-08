using System;
using System.Collections.Generic;
using UnityEngine;

namespace Flowstrand
{
    public sealed class FlowGraphExecution
    {
        private readonly FlowGraph _graph;
        private readonly UnityEngine.Object _owner;
        private readonly List<ExecutionBranch> _activeBranches = new List<ExecutionBranch>();
        private readonly Dictionary<string, JoinAllState> _joinAllStates =
            new Dictionary<string, JoinAllState>(StringComparer.Ordinal);
        private readonly Dictionary<string, JoinAnyState> _joinAnyStates =
            new Dictionary<string, JoinAnyState>(StringComparer.Ordinal);
        private readonly Dictionary<string, FlowNodeRuntimeState> _nodeStates =
            new Dictionary<string, FlowNodeRuntimeState>(StringComparer.Ordinal);
        private int _nextBranchId;

        public FlowGraphExecution(
            FlowGraph graph,
            FlowBlackboard blackboard = null,
            UnityEngine.Object owner = null)
        {
            _graph = graph != null ? graph : throw new ArgumentNullException(nameof(graph));
            Blackboard = blackboard ?? new FlowBlackboard();
            _owner = owner;
        }

        public event Action<FlowGraphExecution> Started;
        public event Action<FlowNode> NodeEntered;
        public event Action<FlowNode, FlowNodeStatus> NodeExited;
        public event Action<FlowExecutionStatus> Finished;

        public FlowGraph Graph => _graph;
        public FlowBlackboard Blackboard { get; }
        public FlowExecutionStatus Status { get; private set; } = FlowExecutionStatus.Idle;
        public FlowNode CurrentNode =>
            _activeBranches.Count > 0 ? _activeBranches[0].Node : null;
        public int ActiveBranchCount => _activeBranches.Count;

        public bool IsNodeActive(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return false;
            }

            for (int i = 0; i < _activeBranches.Count; i++)
            {
                if (_activeBranches[i].Node != null &&
                    string.Equals(
                        _activeBranches[i].Node.Id,
                        nodeId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public FlowNodeRuntimeState GetNodeRuntimeState(string nodeId)
        {
            if (IsNodeActive(nodeId))
            {
                return FlowNodeRuntimeState.Running;
            }

            return !string.IsNullOrEmpty(nodeId) &&
                   _nodeStates.TryGetValue(nodeId, out FlowNodeRuntimeState state)
                ? state
                : FlowNodeRuntimeState.NotVisited;
        }

        public bool Start()
        {
            if (Status == FlowExecutionStatus.Running)
            {
                return false;
            }

            FlowGraphValidationResult validation = FlowGraphValidator.Validate(_graph);
            if (validation.HasErrors)
            {
                for (int i = 0; i < validation.Issues.Count; i++)
                {
                    FlowGraphIssue issue = validation.Issues[i];
                    if (issue.Severity == FlowGraphIssueSeverity.Error)
                    {
                        Debug.LogError($"Flow Graph validation: {issue.Message}", LogContext);
                    }
                }

                Finish(FlowExecutionStatus.Failed);
                return false;
            }

            FlowNode entryNode = FindEntryNode();
            if (entryNode == null)
            {
                Debug.LogError($"Flow Graph '{_graph.name}' requires exactly one Entry node.", _graph);
                Finish(FlowExecutionStatus.Failed);
                return false;
            }

            _activeBranches.Clear();
            _joinAllStates.Clear();
            _joinAnyStates.Clear();
            _nodeStates.Clear();
            _nextBranchId = 0;
            Status = FlowExecutionStatus.Running;
            Started?.Invoke(this);

            ExecutionBranch root = new ExecutionBranch(++_nextBranchId);
            _activeBranches.Add(root);
            if (!TryEnterNode(root, entryNode, null))
            {
                return false;
            }

            return true;
        }

        public void Tick(float deltaTime, int maximumTransitions = 128)
        {
            if (Status != FlowExecutionStatus.Running)
            {
                return;
            }

            if (maximumTransitions < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumTransitions));
            }

            Queue<WorkItem> pending = new Queue<WorkItem>();
            ExecutionBranch[] frameBranches = _activeBranches.ToArray();
            for (int i = 0; i < frameBranches.Length; i++)
            {
                frameBranches[i].Context.Advance(deltaTime);
                pending.Enqueue(new WorkItem(frameBranches[i], 0));
            }

            while (pending.Count > 0 && Status == FlowExecutionStatus.Running)
            {
                WorkItem work = pending.Dequeue();
                if (!_activeBranches.Contains(work.Branch))
                {
                    continue;
                }

                FlowNodeStatus nodeStatus;
                try
                {
                    nodeStatus = work.Branch.Node.OnUpdate(work.Branch.Context);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, LogContext);
                    _nodeStates[work.Branch.Node.Id] = FlowNodeRuntimeState.Failed;
                    _activeBranches.Remove(work.Branch);
                    FailExecution();
                    return;
                }

                if (nodeStatus == FlowNodeStatus.Running)
                {
                    continue;
                }

                if (work.ImmediateTransitions >= maximumTransitions)
                {
                    Debug.LogError(
                        $"Flow Graph '{_graph.name}' exceeded {maximumTransitions} immediate " +
                        "node transitions in one branch. The graph may contain an infinite loop.",
                        LogContext);
                    FailExecution();
                    return;
                }

                if (!TryExitNode(work.Branch, nodeStatus))
                {
                    return;
                }

                int nextTransition = work.ImmediateTransitions + 1;
                if (nodeStatus == FlowNodeStatus.Succeeded &&
                    work.Branch.Node is IFlowForkNode)
                {
                    HandleFork(work.Branch, nextTransition, pending);
                }
                else if (nodeStatus == FlowNodeStatus.Succeeded &&
                         work.Branch.Node is IFlowJoinNode joinNode)
                {
                    HandleJoin(work.Branch, joinNode, nextTransition, pending);
                }
                else
                {
                    HandleRegularTransition(
                        work.Branch,
                        nodeStatus,
                        nextTransition,
                        pending);
                }
            }
        }

        public void Stop()
        {
            if (Status != FlowExecutionStatus.Running)
            {
                return;
            }

            CancelAllActiveBranches();
            _joinAllStates.Clear();
            _joinAnyStates.Clear();
            Finish(FlowExecutionStatus.Stopped);
        }

        private void HandleFork(
            ExecutionBranch branch,
            int immediateTransitions,
            Queue<WorkItem> pending)
        {
            List<FlowEdge> destinations = new List<FlowEdge>();
            IReadOnlyList<FlowPort> outputPorts = branch.Node.OutputPorts;
            for (int i = 0; i < outputPorts.Count; i++)
            {
                List<FlowEdge> connections = FindConnections(branch.Node.Id, outputPorts[i].Id);
                if (connections.Count > 1)
                {
                    Debug.LogError(
                        $"Parallel port '{branch.Node.Id}.{outputPorts[i].Id}' has multiple connections.",
                        LogContext);
                    FailExecution();
                    return;
                }

                if (connections.Count == 1)
                {
                    destinations.Add(connections[0]);
                }
            }

            if (destinations.Count == 0)
            {
                CompleteSuccessfulBranch(branch);
                return;
            }

            if (destinations.Count == 1)
            {
                ContinueAlongEdge(branch, destinations[0], immediateTransitions, pending);
                return;
            }

            if (!ContinueAlongEdge(branch, destinations[0], immediateTransitions, pending))
            {
                return;
            }

            for (int i = 1; i < destinations.Count; i++)
            {
                ExecutionBranch child = new ExecutionBranch(++_nextBranchId)
                {
                };
                _activeBranches.Add(child);
                if (!ContinueAlongEdge(child, destinations[i], immediateTransitions, pending))
                {
                    return;
                }
            }
        }

        private void HandleJoin(
            ExecutionBranch branch,
            IFlowJoinNode joinNode,
            int immediateTransitions,
            Queue<WorkItem> pending)
        {
            if (string.IsNullOrEmpty(branch.IncomingPortId))
            {
                Debug.LogError(
                    $"Join node '{branch.Node.Id}' was reached without a valid input port.",
                    LogContext);
                FailExecution();
                return;
            }

            if (joinNode.JoinMode == FlowJoinMode.Any)
            {
                HandleJoinAny(branch, immediateTransitions, pending);
                return;
            }

            HandleJoinAll(branch, immediateTransitions, pending);
        }

        private void HandleJoinAll(
            ExecutionBranch branch,
            int immediateTransitions,
            Queue<WorkItem> pending)
        {
            if (!_joinAllStates.TryGetValue(branch.Node.Id, out JoinAllState state))
            {
                state = new JoinAllState(branch.Node.InputPorts);
                _joinAllStates.Add(branch.Node.Id, state);
            }

            _activeBranches.Remove(branch);
            if (!state.TryArrive(branch.IncomingPortId, branch, out ExecutionBranch survivor))
            {
                Debug.LogError(
                    $"Join All node '{branch.Node.Id}' received an unknown input port.",
                    LogContext);
                FailExecution();
                return;
            }

            if (survivor == null)
            {
                _nodeStates[branch.Node.Id] = FlowNodeRuntimeState.Waiting;
                CheckForDeadlock();
                return;
            }

            _nodeStates[branch.Node.Id] = FlowNodeRuntimeState.Succeeded;
            _activeBranches.Add(survivor);
            ContinueFromJoin(survivor, immediateTransitions, pending);
        }

        private void HandleJoinAny(
            ExecutionBranch branch,
            int immediateTransitions,
            Queue<WorkItem> pending)
        {
            if (!_joinAnyStates.TryGetValue(branch.Node.Id, out JoinAnyState state))
            {
                state = new JoinAnyState(branch.Node.InputPorts);
                _joinAnyStates.Add(branch.Node.Id, state);
            }

            if (!state.TryArrive(branch.IncomingPortId, out bool shouldContinue))
            {
                Debug.LogError(
                    $"Join Any node '{branch.Node.Id}' received an unknown input port.",
                    LogContext);
                FailExecution();
                return;
            }

            if (shouldContinue)
            {
                ContinueFromJoin(branch, immediateTransitions, pending);
            }
            else
            {
                _activeBranches.Remove(branch);
                CheckForDeadlock();
            }
        }

        private void ContinueFromJoin(
            ExecutionBranch branch,
            int immediateTransitions,
            Queue<WorkItem> pending)
        {
            List<FlowEdge> connections = FindConnections(
                branch.Node.Id,
                FlowPortIds.Completed);
            if (connections.Count > 1)
            {
                Debug.LogError(
                    $"Join output '{branch.Node.Id}.{FlowPortIds.Completed}' has multiple connections.",
                    LogContext);
                FailExecution();
                return;
            }

            if (connections.Count == 0)
            {
                CompleteSuccessfulBranch(branch);
                return;
            }

            ContinueAlongEdge(branch, connections[0], immediateTransitions, pending);
        }

        private void HandleRegularTransition(
            ExecutionBranch branch,
            FlowNodeStatus nodeStatus,
            int immediateTransitions,
            Queue<WorkItem> pending)
        {
            string outputPortId = branch.Context.SelectedOutputPortId;
            if (string.IsNullOrEmpty(outputPortId))
            {
                outputPortId = nodeStatus == FlowNodeStatus.Succeeded
                    ? FlowPortIds.Completed
                    : FlowPortIds.Failed;
            }

            List<FlowEdge> connections = FindConnections(branch.Node.Id, outputPortId);
            if (connections.Count > 1)
            {
                Debug.LogError(
                    $"Output port '{branch.Node.Id}.{outputPortId}' has multiple connections.",
                    LogContext);
                FailExecution();
                return;
            }

            if (connections.Count == 0)
            {
                if (nodeStatus == FlowNodeStatus.Failed)
                {
                    FailExecution();
                }
                else
                {
                    CompleteSuccessfulBranch(branch);
                }

                return;
            }

            ContinueAlongEdge(branch, connections[0], immediateTransitions, pending);
        }

        private bool ContinueAlongEdge(
            ExecutionBranch branch,
            FlowEdge edge,
            int immediateTransitions,
            Queue<WorkItem> pending)
        {
            FlowNode nextNode = _graph.FindNode(edge.InputNodeId);
            if (nextNode == null)
            {
                Debug.LogError("A Flow Graph edge references a node that no longer exists.", LogContext);
                FailExecution();
                return false;
            }

            if (!TryEnterNode(branch, nextNode, edge.InputPortId))
            {
                return false;
            }

            branch.Context.Advance(0f);
            pending.Enqueue(new WorkItem(branch, immediateTransitions));
            return true;
        }

        private bool TryEnterNode(
            ExecutionBranch branch,
            FlowNode node,
            string incomingPortId)
        {
            branch.Node = node;
            branch.IncomingPortId = incomingPortId;
            branch.Context = new FlowNodeContext(node, Blackboard, _owner);
            _nodeStates[node.Id] = FlowNodeRuntimeState.Running;
            try
            {
                node.OnEnter(branch.Context);
                NodeEntered?.Invoke(node);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, LogContext);
                _nodeStates[node.Id] = FlowNodeRuntimeState.Failed;
                _activeBranches.Remove(branch);
                FailExecution();
                return false;
            }
        }

        private bool TryExitNode(ExecutionBranch branch, FlowNodeStatus status)
        {
            try
            {
                branch.Node.OnExit(branch.Context, status);
                _nodeStates[branch.Node.Id] = status == FlowNodeStatus.Succeeded
                    ? FlowNodeRuntimeState.Succeeded
                    : FlowNodeRuntimeState.Failed;
                NodeExited?.Invoke(branch.Node, status);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, LogContext);
                _nodeStates[branch.Node.Id] = FlowNodeRuntimeState.Failed;
                _activeBranches.Remove(branch);
                FailExecution();
                return false;
            }
        }

        private void CompleteSuccessfulBranch(ExecutionBranch branch)
        {
            _activeBranches.Remove(branch);
            CheckForDeadlock();
        }

        private void CheckForDeadlock()
        {
            if (Status != FlowExecutionStatus.Running || _activeBranches.Count > 0)
            {
                return;
            }

            bool hasWaitingJoinAll = false;
            foreach (JoinAllState state in _joinAllStates.Values)
            {
                if (state.HasWaitingBranches)
                {
                    hasWaitingJoinAll = true;
                    break;
                }
            }

            if (hasWaitingJoinAll)
            {
                foreach (KeyValuePair<string, JoinAllState> pair in _joinAllStates)
                {
                    if (pair.Value.HasWaitingBranches)
                    {
                        _nodeStates[pair.Key] = FlowNodeRuntimeState.Failed;
                    }
                }

                Debug.LogError(
                    $"Flow Graph '{_graph.name}' stopped with Join All waiting for inputs that never arrived.",
                    LogContext);
                _joinAllStates.Clear();
                _joinAnyStates.Clear();
                Finish(FlowExecutionStatus.Failed);
            }
            else
            {
                Finish(FlowExecutionStatus.Succeeded);
            }
        }

        private void FailExecution()
        {
            if (Status != FlowExecutionStatus.Running)
            {
                return;
            }

            CancelAllActiveBranches();
            _joinAllStates.Clear();
            _joinAnyStates.Clear();
            Finish(FlowExecutionStatus.Failed);
        }

        private void CancelAllActiveBranches()
        {
            ExecutionBranch[] branches = _activeBranches.ToArray();
            _activeBranches.Clear();
            for (int i = 0; i < branches.Length; i++)
            {
                TryCancelNode(branches[i]);
            }
        }

        private void TryCancelNode(ExecutionBranch branch)
        {
            try
            {
                branch.Node?.OnCancel(branch.Context);
                if (branch.Node != null &&
                    GetNodeRuntimeState(branch.Node.Id) == FlowNodeRuntimeState.Running)
                {
                    _nodeStates[branch.Node.Id] = FlowNodeRuntimeState.Cancelled;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, LogContext);
            }
        }

        private List<FlowEdge> FindConnections(string outputNodeId, string outputPortId)
        {
            List<FlowEdge> result = new List<FlowEdge>();
            for (int i = 0; i < _graph.Edges.Count; i++)
            {
                FlowEdge edge = _graph.Edges[i];
                if (edge != null &&
                    edge.OutputNodeId == outputNodeId &&
                    edge.OutputPortId == outputPortId)
                {
                    result.Add(edge);
                }
            }

            return result;
        }

        private FlowNode FindEntryNode()
        {
            FlowNode entryNode = null;
            for (int i = 0; i < _graph.Nodes.Count; i++)
            {
                if (_graph.Nodes[i] is not EntryNode candidate)
                {
                    continue;
                }

                if (entryNode != null)
                {
                    return null;
                }

                entryNode = candidate;
            }

            return entryNode;
        }

        private void Finish(FlowExecutionStatus status)
        {
            Status = status;
            Finished?.Invoke(status);
        }

        private UnityEngine.Object LogContext => _owner != null ? _owner : _graph;

        private sealed class ExecutionBranch
        {
            public ExecutionBranch(int id)
            {
                Id = id;
            }

            public int Id { get; }
            public FlowNode Node { get; set; }
            public FlowNodeContext Context { get; set; }
            public string IncomingPortId { get; set; }
        }

        private sealed class JoinAllState
        {
            private readonly List<string> _inputPortIds = new List<string>();
            private readonly Dictionary<string, Queue<ExecutionBranch>> _arrivals =
                new Dictionary<string, Queue<ExecutionBranch>>(StringComparer.Ordinal);

            public JoinAllState(IReadOnlyList<FlowPort> inputPorts)
            {
                for (int i = 0; i < inputPorts.Count; i++)
                {
                    string portId = inputPorts[i].Id;
                    _inputPortIds.Add(portId);
                    _arrivals.Add(portId, new Queue<ExecutionBranch>());
                }
            }

            public bool HasWaitingBranches
            {
                get
                {
                    for (int i = 0; i < _inputPortIds.Count; i++)
                    {
                        if (_arrivals[_inputPortIds[i]].Count > 0)
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }

            public bool TryArrive(
                string inputPortId,
                ExecutionBranch branch,
                out ExecutionBranch survivor)
            {
                survivor = null;
                if (!_arrivals.TryGetValue(inputPortId, out Queue<ExecutionBranch> queue))
                {
                    return false;
                }

                queue.Enqueue(branch);
                for (int i = 0; i < _inputPortIds.Count; i++)
                {
                    if (_arrivals[_inputPortIds[i]].Count == 0)
                    {
                        return true;
                    }
                }

                for (int i = 0; i < _inputPortIds.Count; i++)
                {
                    ExecutionBranch arrived = _arrivals[_inputPortIds[i]].Dequeue();
                    survivor ??= arrived;
                }

                return true;
            }
        }

        private sealed class JoinAnyState
        {
            private readonly int _inputCount;
            private readonly Dictionary<string, int> _arrivalCounts =
                new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly Dictionary<int, HashSet<string>> _roundArrivals =
                new Dictionary<int, HashSet<string>>();
            private readonly HashSet<int> _emittedRounds = new HashSet<int>();

            public JoinAnyState(IReadOnlyList<FlowPort> inputPorts)
            {
                _inputCount = inputPorts.Count;
                for (int i = 0; i < inputPorts.Count; i++)
                {
                    _arrivalCounts.Add(inputPorts[i].Id, 0);
                }
            }

            public bool TryArrive(string inputPortId, out bool shouldContinue)
            {
                shouldContinue = false;
                if (!_arrivalCounts.TryGetValue(inputPortId, out int round))
                {
                    return false;
                }

                _arrivalCounts[inputPortId] = round + 1;
                if (!_roundArrivals.TryGetValue(round, out HashSet<string> arrivals))
                {
                    arrivals = new HashSet<string>(StringComparer.Ordinal);
                    _roundArrivals.Add(round, arrivals);
                }

                arrivals.Add(inputPortId);
                shouldContinue = _emittedRounds.Add(round);
                if (arrivals.Count >= _inputCount)
                {
                    _roundArrivals.Remove(round);
                    _emittedRounds.Remove(round);
                }

                return true;
            }
        }

        private readonly struct WorkItem
        {
            public WorkItem(ExecutionBranch branch, int immediateTransitions)
            {
                Branch = branch;
                ImmediateTransitions = immediateTransitions;
            }

            public ExecutionBranch Branch { get; }
            public int ImmediateTransitions { get; }
        }
    }
}
