using System;
using System.Collections.Generic;

namespace Flowstrand
{
    [Serializable]
    [FlowNodeMenu("Flow/Parallel")]
    public sealed class ParallelNode : DynamicFlowPortNode, IFlowForkNode
    {
        public override FlowPortDirection DynamicPortDirection => FlowPortDirection.Output;
        public override IReadOnlyList<FlowPort> OutputPorts => BuildDynamicPorts();

        public override FlowNodeStatus OnUpdate(FlowNodeContext context)
        {
            return FlowNodeStatus.Succeeded;
        }
    }
}
