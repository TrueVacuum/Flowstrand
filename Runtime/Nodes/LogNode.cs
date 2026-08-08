using System;
using UnityEngine;

namespace Flowstrand
{
    [Serializable]
    [FlowNodeMenu("Debug/Log")]
    public sealed class LogNode : FlowNode
    {
        [SerializeField, TextArea] private string _message = "Flowstrand";

        public override FlowNodeStatus OnUpdate(FlowNodeContext context)
        {
            Debug.Log(_message, context.Owner);
            return FlowNodeStatus.Succeeded;
        }
    }
}
