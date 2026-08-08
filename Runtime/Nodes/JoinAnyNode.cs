using System;
using System.Collections.Generic;

namespace Flowstrand
{
    [Serializable]
    [FlowNodeMenu("Flow/Join Any")]
    public sealed class JoinAnyNode : DynamicFlowPortNode, IFlowJoinNode
    {
        public override FlowPortDirection DynamicPortDirection => FlowPortDirection.Input;
        public override IReadOnlyList<FlowPort> InputPorts => BuildDynamicPorts();
        public FlowJoinMode JoinMode => FlowJoinMode.Any;

        public override FlowNodeStatus OnUpdate(FlowNodeContext context)
        {
            return FlowNodeStatus.Succeeded;
        }
    }
}
