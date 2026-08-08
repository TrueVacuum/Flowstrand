using System;
using UnityEngine;

namespace Flowstrand
{
    public sealed class FlowNodeContext
    {
        private readonly FlowNode _node;
        private object _state;
        private string _selectedOutputPortId;

        internal FlowNodeContext(
            FlowNode node,
            FlowBlackboard blackboard,
            UnityEngine.Object owner)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));
            Blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            Owner = owner;
        }

        public FlowBlackboard Blackboard { get; }
        public UnityEngine.Object Owner { get; }
        public string NodeId => _node.Id;
        public float DeltaTime { get; private set; }
        public float ElapsedTime { get; private set; }

        internal string SelectedOutputPortId => _selectedOutputPortId;

        public void SelectOutput(string portId)
        {
            if (!_node.TryGetPort(portId, FlowPortDirection.Output, out _))
            {
                throw new ArgumentException(
                    $"Node '{_node.Id}' has no output port '{portId}'.",
                    nameof(portId));
            }

            _selectedOutputPortId = portId;
        }

        public TState GetOrCreateState<TState>() where TState : class, new()
        {
            if (_state == null)
            {
                _state = new TState();
            }

            if (_state is not TState typedState)
            {
                throw new InvalidOperationException(
                    $"This node execution already stores state of type '{_state.GetType().Name}'.");
            }

            return typedState;
        }

        internal void Advance(float deltaTime)
        {
            DeltaTime = Mathf.Max(0f, deltaTime);
            ElapsedTime += DeltaTime;
        }
    }
}
