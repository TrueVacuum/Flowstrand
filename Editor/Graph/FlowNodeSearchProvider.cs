using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Flowstrand.Editor
{
    internal sealed class FlowNodeSearchProvider : ScriptableObject, ISearchWindowProvider
    {
        private static Texture2D _nodeIcon;

        private FlowGraphView _graphView;
        private Vector2 _graphPosition;

        public void Initialize(FlowGraphView graphView, Vector2 graphPosition)
        {
            _graphView = graphView;
            _graphPosition = graphPosition;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            List<SearchTreeEntry> tree = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Create Flow Node"), 0)
            };

            HashSet<string> groups = new HashSet<string>();
            foreach (FlowNodeTypeRegistry.Entry entry in FlowNodeTypeRegistry.Entries)
            {
                string[] segments = entry.Path.Split('/');
                string accumulatedPath = string.Empty;

                for (int i = 0; i < segments.Length - 1; i++)
                {
                    accumulatedPath = string.IsNullOrEmpty(accumulatedPath)
                        ? segments[i]
                        : $"{accumulatedPath}/{segments[i]}";

                    if (groups.Add(accumulatedPath))
                    {
                        tree.Add(new SearchTreeGroupEntry(
                            new GUIContent(segments[i]),
                            i + 1));
                    }
                }

                tree.Add(new SearchTreeEntry(new GUIContent(segments[^1], GetNodeIcon()))
                {
                    level = segments.Length,
                    userData = entry
                });
            }

            return tree;
        }

        private static Texture2D GetNodeIcon()
        {
            if (_nodeIcon == null)
            {
                _nodeIcon = EditorGUIUtility.IconContent("d_AnimatorState Icon").image as Texture2D;
            }

            if (_nodeIcon == null)
            {
                _nodeIcon = EditorGUIUtility.IconContent("cs Script Icon").image as Texture2D;
            }

            return _nodeIcon;
        }

        public bool OnSelectEntry(
            SearchTreeEntry searchTreeEntry,
            SearchWindowContext context)
        {
            if (searchTreeEntry.userData is not FlowNodeTypeRegistry.Entry entry)
            {
                return false;
            }

            _graphView.CreateNode(entry.Type, _graphPosition);
            return true;
        }
    }
}
