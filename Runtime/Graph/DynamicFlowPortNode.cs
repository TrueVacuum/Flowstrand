using System;
using System.Collections.Generic;
using UnityEngine;

namespace Flowstrand
{
    [Serializable]
    public abstract class DynamicFlowPortNode : FlowNode, IDynamicFlowPortNode
    {
        [SerializeField, HideInInspector]
        private List<string> _dynamicPortIds = new List<string>();

        public abstract FlowPortDirection DynamicPortDirection { get; }
        public int DynamicPortCount
        {
            get
            {
                EnsureMinimumPorts();
                return _dynamicPortIds.Count;
            }
        }

        public virtual int MinimumDynamicPortCount => 2;

        public string AddDynamicPort()
        {
            EnsureMinimumPorts();
            string id = CreatePortId();
            _dynamicPortIds.Add(id);
            return id;
        }

        public bool TryRemoveDynamicPort(out string removedPortId)
        {
            EnsureMinimumPorts();
            if (_dynamicPortIds.Count <= MinimumDynamicPortCount)
            {
                removedPortId = null;
                return false;
            }

            int index = _dynamicPortIds.Count - 1;
            removedPortId = _dynamicPortIds[index];
            _dynamicPortIds.RemoveAt(index);
            return true;
        }

        protected IReadOnlyList<FlowPort> BuildDynamicPorts()
        {
            EnsureMinimumPorts();
            FlowPort[] ports = new FlowPort[_dynamicPortIds.Count];
            for (int i = 0; i < _dynamicPortIds.Count; i++)
            {
                ports[i] = new FlowPort(
                    _dynamicPortIds[i],
                    GetDynamicPortDisplayName(i),
                    DynamicPortDirection);
            }

            return ports;
        }

        protected virtual string GetDynamicPortDisplayName(int index)
        {
            return DynamicPortDirection == FlowPortDirection.Input
                ? $"Input {index + 1}"
                : $"Branch {index + 1}";
        }

        private void EnsureMinimumPorts()
        {
            _dynamicPortIds ??= new List<string>();
            while (_dynamicPortIds.Count < MinimumDynamicPortCount)
            {
                _dynamicPortIds.Add(CreatePortId());
            }

            HashSet<string> usedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _dynamicPortIds.Count; i++)
            {
                if (string.IsNullOrEmpty(_dynamicPortIds[i]) || !usedIds.Add(_dynamicPortIds[i]))
                {
                    _dynamicPortIds[i] = CreatePortId();
                    usedIds.Add(_dynamicPortIds[i]);
                }
            }
        }

        private static string CreatePortId()
        {
            return $"dynamic_{Guid.NewGuid():N}";
        }
    }
}
