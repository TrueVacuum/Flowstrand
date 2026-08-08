namespace Flowstrand
{
    public enum FlowJoinMode
    {
        All,
        Any
    }

    public interface IFlowForkNode
    {
    }

    public interface IFlowJoinNode
    {
        FlowJoinMode JoinMode { get; }
    }
}
