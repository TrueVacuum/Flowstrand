using System;
using System.Collections.Generic;
using UnityEngine;

namespace Flowstrand
{
    [Serializable]
    [FlowNodeMenu("Flow/Branch")]
    public sealed class BranchNode : FlowNode
    {
        private static readonly FlowPort[] BranchOutputs =
        {
            new FlowPort(FlowPortIds.True, "True", FlowPortDirection.Output),
            new FlowPort(FlowPortIds.False, "False", FlowPortDirection.Output)
        };

        [SerializeField] private string _blackboardKey;
        [SerializeField] private bool _fallbackValue;

        public override IReadOnlyList<FlowPort> OutputPorts => BranchOutputs;

        public override FlowNodeStatus OnUpdate(FlowNodeContext context)
        {
            bool value = context.Blackboard.GetOrDefault(_blackboardKey, _fallbackValue);
            context.SelectOutput(value ? FlowPortIds.True : FlowPortIds.False);
            return FlowNodeStatus.Succeeded;
        }
    }
}
