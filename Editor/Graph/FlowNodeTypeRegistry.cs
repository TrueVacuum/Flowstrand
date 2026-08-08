using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Flowstrand.Editor
{
    internal static class FlowNodeTypeRegistry
    {
        internal sealed class Entry
        {
            public Entry(Type type, string path)
            {
                Type = type;
                Path = path;
            }

            public Type Type { get; }
            public string Path { get; }
            public string DisplayName => Path.Split('/').Last();
        }

        private static IReadOnlyList<Entry> _entries;

        public static IReadOnlyList<Entry> Entries => _entries ??= BuildEntries();

        private static IReadOnlyList<Entry> BuildEntries()
        {
            List<Entry> entries = new List<Entry>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<FlowNode>())
            {
                if (type.IsAbstract || type.IsGenericType ||
                    type.GetConstructor(Type.EmptyTypes) == null ||
                    type == typeof(EntryNode))
                {
                    continue;
                }

                FlowNodeMenuAttribute menu = type
                    .GetCustomAttributes(typeof(FlowNodeMenuAttribute), false)
                    .OfType<FlowNodeMenuAttribute>()
                    .FirstOrDefault();

                string path = menu != null ? menu.Path : GetFallbackPath(type);
                entries.Add(new Entry(type, path));
            }

            return entries
                .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string GetFallbackPath(Type type)
        {
            string name = type.Name.EndsWith("Node", StringComparison.Ordinal)
                ? type.Name.Substring(0, type.Name.Length - 4)
                : type.Name;
            return $"Custom/{ObjectNames.NicifyVariableName(name)}";
        }
    }
}
