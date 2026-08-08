using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Flowstrand.Editor
{
    public sealed class FlowGraphEditorWindow : EditorWindow
    {
        [SerializeField] private FlowGraph _graph;
        private FlowGraphView _graphView;
        private ObjectField _graphField;
        private VisualElement _validationPanel;
        private DropdownField _runnerField;
        private readonly List<FlowGraphRunner> _runnerOptions = new List<FlowGraphRunner>();
        private FlowGraphRunner _debugRunner;
        private double _nextRuntimeRefreshTime;

        [MenuItem("Window/Flowstrand/Flow Graph")]
        public static void OpenWindow()
        {
            GetWindow<FlowGraphEditorWindow>("Flowstrand");
        }

        public static void Open(FlowGraph graph)
        {
            FlowGraphEditorWindow window = GetWindow<FlowGraphEditorWindow>("Flowstrand");
            window.SetGraph(graph);
            window.Show();
            window.Focus();
        }

        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            if (EditorUtility.EntityIdToObject(instanceId) is not FlowGraph graph)
            {
                return false;
            }

            Open(graph);
            return true;
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += HandleUndoRedo;
            EditorApplication.update += HandleEditorUpdate;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
            EditorApplication.update -= HandleEditorUpdate;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();

            Toolbar toolbar = new Toolbar();
            _graphField = new ObjectField("Graph")
            {
                objectType = typeof(FlowGraph),
                allowSceneObjects = false,
                value = _graph
            };
            _graphField.RegisterValueChangedCallback(evt => SetGraph(evt.newValue as FlowGraph));
            toolbar.Add(_graphField);
            ToolbarButton validateButton = new ToolbarButton(ValidateGraph)
            {
                text = "Validate"
            };
            toolbar.Add(validateButton);

            _runnerField = new DropdownField(
                "Debug Runner",
                new List<string> { "None" },
                0);
            _runnerField.style.minWidth = 240f;
            _runnerField.RegisterValueChangedCallback(evt => SelectDebugRunner(evt.newValue));
            toolbar.Add(_runnerField);
            rootVisualElement.Add(toolbar);

            _validationPanel = new VisualElement();
            _validationPanel.style.display = DisplayStyle.None;
            rootVisualElement.Add(_validationPanel);

            _graphView = new FlowGraphView(this);
            rootVisualElement.Add(_graphView);
            _graphView.SetGraph(_graph);
            UpdateTitle();
        }

        private void ValidateGraph()
        {
            _validationPanel.Clear();
            _validationPanel.style.display = DisplayStyle.Flex;

            FlowGraphValidationResult result = FlowGraphValidator.Validate(_graph);
            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            Label title = new Label(result.Issues.Count == 0
                ? "Validation"
                : $"Validation ({result.Issues.Count})");
            title.style.flexGrow = 1f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginLeft = 4f;
            header.Add(title);

            Button closeButton = new Button(() =>
                _validationPanel.style.display = DisplayStyle.None)
            {
                text = "×",
                tooltip = "Close validation results"
            };
            header.Add(closeButton);
            _validationPanel.Add(header);

            if (result.Issues.Count == 0)
            {
                _validationPanel.Add(new HelpBox(
                    "Validation passed. No issues found.",
                    HelpBoxMessageType.Info));
                return;
            }

            for (int i = 0; i < result.Issues.Count; i++)
            {
                FlowGraphIssue issue = result.Issues[i];
                _validationPanel.Add(new HelpBox(
                    issue.Message,
                    issue.Severity == FlowGraphIssueSeverity.Error
                        ? HelpBoxMessageType.Error
                        : HelpBoxMessageType.Warning));
            }
        }

        private void SetGraph(FlowGraph graph)
        {
            _graph = graph;
            _debugRunner = null;
            if (_graphField != null)
            {
                _graphField.SetValueWithoutNotify(graph);
            }

            _graphView?.SetGraph(graph);
            UpdateTitle();
        }

        private void HandleUndoRedo()
        {
            _graphView?.SetGraph(_graph);
            Repaint();
        }

        private void HandleEditorUpdate()
        {
            if (_graphView == null || EditorApplication.timeSinceStartup < _nextRuntimeRefreshTime)
            {
                return;
            }

            _nextRuntimeRefreshTime = EditorApplication.timeSinceStartup + 0.1d;
            RefreshRunnerOptions();
            FlowGraphExecution debugExecution = null;
            if (Application.isPlaying &&
                _graph != null &&
                _debugRunner != null)
            {
                debugExecution = _debugRunner.Execution;
                if (_debugRunner.Graph == _graph &&
                    debugExecution != null)
                {
                    _graphView.SetRuntimeExecution(debugExecution);
                    return;
                }
            }

            _graphView.SetRuntimeExecution(null);
        }

        private void RefreshRunnerOptions()
        {
            if (_runnerField == null)
            {
                return;
            }

            FlowGraphRunner[] sceneRunners = Object.FindObjectsByType<FlowGraphRunner>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            List<FlowGraphRunner> matching = new List<FlowGraphRunner>();
            for (int i = 0; i < sceneRunners.Length; i++)
            {
                if (sceneRunners[i].Graph == _graph)
                {
                    matching.Add(sceneRunners[i]);
                }
            }

            if (_debugRunner == null && matching.Count == 1)
            {
                _debugRunner = matching[0];
            }
            else if (_debugRunner != null && !matching.Contains(_debugRunner))
            {
                _debugRunner = null;
            }

            _runnerOptions.Clear();
            _runnerOptions.AddRange(matching);
            List<string> choices = new List<string> { "None" };
            for (int i = 0; i < _runnerOptions.Count; i++)
            {
                choices.Add(GetRunnerDisplayName(_runnerOptions[i]));
            }

            _runnerField.choices = choices;
            int selectedIndex = _debugRunner != null
                ? _runnerOptions.IndexOf(_debugRunner) + 1
                : 0;
            _runnerField.SetValueWithoutNotify(choices[selectedIndex]);
        }

        private void SelectDebugRunner(string displayName)
        {
            int index = _runnerField?.choices.IndexOf(displayName) ?? 0;
            _debugRunner = index > 0 && index <= _runnerOptions.Count
                ? _runnerOptions[index - 1]
                : null;
        }

        private static string GetRunnerDisplayName(FlowGraphRunner runner)
        {
            string path = runner.gameObject.name;
            Transform parent = runner.transform.parent;
            while (parent != null)
            {
                path = $"{parent.name}/{path}";
                parent = parent.parent;
            }

            return $"{path} ({runner.GetInstanceID()})";
        }

        private void UpdateTitle()
        {
            titleContent = new GUIContent(
                _graph != null ? $"Flowstrand - {_graph.name}" : "Flowstrand");
        }
    }
}
