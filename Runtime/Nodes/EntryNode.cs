using System;
using System.Collections.Generic;

namespace Flowstrand
{
    [Serializable]
    [FlowNodeMenu("Flow/Entry")]
    public sealed class EntryNode : FlowNode
    {
        private static readonly FlowPort[] NoInputs = Array.Empty<FlowPort>();

        public override IReadOnlyList<FlowPort> InputPorts => NoInputs;

        public override FlowNodeStatus OnUpdate(FlowNodeContext context)
        {
            return FlowNodeStatus.Succeeded;
        }
    }
}
