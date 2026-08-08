using System;

namespace Flowstrand
{
    public enum FlowPortDirection
    {
        Input,
        Output
    }

    public enum FlowPortCapacity
    {
        Single,
        Multiple
    }

    public readonly struct FlowPort
    {
        public FlowPort(
            string id,
            string displayName,
            FlowPortDirection direction,
            FlowPortCapacity capacity = FlowPortCapacity.Single)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A flow port requires a stable ID.", nameof(id));
            }

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            Direction = direction;
            Capacity = capacity;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public FlowPortDirection Direction { get; }
        public FlowPortCapacity Capacity { get; }
    }

    public static class FlowPortIds
    {
        public const string Enter = "enter";
        public const string Completed = "completed";
        public const string Failed = "failed";
        public const string True = "true";
        public const string False = "false";
    }
}
