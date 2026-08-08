using System;
using System.Collections.Generic;
using UnityEngine;

namespace Flowstrand
{
    [Serializable]
    public abstract class FlowNode
    {
        private static readonly FlowPort[] DefaultInputs =
        {
            new FlowPort(
                FlowPortIds.Enter,
                "Enter",
                FlowPortDirection.Input,
                FlowPortCapacity.Multiple)
        };

        private static readonly FlowPort[] DefaultOutputs =
        {
            new FlowPort(
                FlowPortIds.Completed,
                "Completed",
                FlowPortDirection.Output)
        };

        [SerializeField, HideInInspector] private string _id;
        [SerializeField, HideInInspector] private Vector2 _position;

        public string Id => _id;
        public Vector2 Position
        {
            get => _position;
            set => _position = value;
        }

        public virtual IReadOnlyList<FlowPort> InputPorts => DefaultInputs;
        public virtual IReadOnlyList<FlowPort> OutputPorts => DefaultOutputs;

        public virtual void OnEnter(FlowNodeContext context)
        {
        }

        public abstract FlowNodeStatus OnUpdate(FlowNodeContext context);

        public virtual void OnExit(FlowNodeContext context, FlowNodeStatus status)
        {
        }

        public virtual void OnCancel(FlowNodeContext context)
        {
        }

        public bool TryGetPort(
            string portId,
            FlowPortDirection direction,
            out FlowPort port)
        {
            IReadOnlyList<FlowPort> ports = direction == FlowPortDirection.Input
                ? InputPorts
                : OutputPorts;

            for (int i = 0; i < ports.Count; i++)
            {
                if (string.Equals(ports[i].Id, portId, StringComparison.Ordinal))
                {
                    port = ports[i];
                    return true;
                }
            }

            port = default;
            return false;
        }

        internal void EnsureId()
        {
            if (string.IsNullOrEmpty(_id))
            {
                _id = Guid.NewGuid().ToString("N");
            }
        }

        internal void RegenerateId()
        {
            _id = Guid.NewGuid().ToString("N");
        }
    }
}
