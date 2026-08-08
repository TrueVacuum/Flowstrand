namespace Flowstrand
{
    public interface IDynamicFlowPortNode
    {
        FlowPortDirection DynamicPortDirection { get; }
        int DynamicPortCount { get; }
        int MinimumDynamicPortCount { get; }

        string AddDynamicPort();
        bool TryRemoveDynamicPort(out string removedPortId);
    }
}
