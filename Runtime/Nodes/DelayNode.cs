using System;
using UnityEngine;

namespace Flowstrand
{
    [Serializable]
    [FlowNodeMenu("Flow/Delay")]
    public sealed class DelayNode : FlowNode
    {
        [SerializeField, Min(0f)] private float _seconds = 1f;

        public override FlowNodeStatus OnUpdate(FlowNodeContext context)
        {
            return context.ElapsedTime >= Mathf.Max(0f, _seconds)
                ? FlowNodeStatus.Succeeded
                : FlowNodeStatus.Running;
        }
    }
}
