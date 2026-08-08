using System;

namespace Flowstrand
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class FlowNodeMenuAttribute : Attribute
    {
        public FlowNodeMenuAttribute(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A node menu path cannot be empty.", nameof(path));
            }

            Path = path.Trim();
        }

        public string Path { get; }
    }
}
