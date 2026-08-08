using System;
using System.Collections.Generic;

namespace Flowstrand
{
    [Serializable]
    [FlowNodeMenu("Flow/Join All")]
    public sealed class JoinAllNode : DynamicFlowPortNode, IFlowJoinNode
    {
        public override FlowPortDirection DynamicPortDirection => FlowPortDirection.Input;
        public override IReadOnlyList<FlowPort> InputPorts => BuildDynamicPorts();
        public FlowJoinMode JoinMode => FlowJoinMode.All;

        public override FlowNodeStatus OnUpdate(FlowNodeContext context)
        {
            return FlowNodeStatus.Succeeded;
        }
    }
}
