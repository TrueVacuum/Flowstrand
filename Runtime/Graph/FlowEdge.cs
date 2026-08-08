using System;
using UnityEngine;

namespace Flowstrand
{
    [Serializable]
    public sealed class FlowEdge
    {
        [SerializeField, HideInInspector] private string _id;
        [SerializeField, HideInInspector] private string _outputNodeId;
        [SerializeField, HideInInspector] private string _outputPortId;
        [SerializeField, HideInInspector] private string _inputNodeId;
        [SerializeField, HideInInspector] private string _inputPortId;

        internal FlowEdge(
            string outputNodeId,
            string outputPortId,
            string inputNodeId,
            string inputPortId)
        {
            _id = Guid.NewGuid().ToString("N");
            _outputNodeId = outputNodeId;
            _outputPortId = outputPortId;
            _inputNodeId = inputNodeId;
            _inputPortId = inputPortId;
        }

        public string Id => _id;
        public string OutputNodeId => _outputNodeId;
        public string OutputPortId => _outputPortId;
        public string InputNodeId => _inputNodeId;
        public string InputPortId => _inputPortId;

        internal void EnsureId()
        {
            if (string.IsNullOrEmpty(_id))
            {
                _id = Guid.NewGuid().ToString("N");
            }
        }
    }
}
