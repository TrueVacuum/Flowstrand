namespace Flowstrand
{
    public enum FlowNodeStatus
    {
        Running,
        Succeeded,
        Failed
    }

    public enum FlowExecutionStatus
    {
        Idle,
        Running,
        Succeeded,
        Failed,
        Stopped
    }

    public enum FlowNodeRuntimeState
    {
        NotVisited,
        Running,
        Waiting,
        Succeeded,
        Failed,
        Cancelled
    }
}
