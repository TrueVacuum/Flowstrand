using UnityEngine;

namespace Flowstrand
{
    [DisallowMultipleComponent]
    public sealed class FlowGraphRunner : MonoBehaviour
    {
        [SerializeField] private FlowGraph _graph;
        [SerializeField] private bool _playOnStart = true;
        [SerializeField, Min(1)] private int _maximumTransitionsPerFrame = 128;

        private FlowGraphExecution _execution;
        private FlowBlackboard _blackboard;

        public FlowGraph Graph => _graph;
        public FlowGraphExecution Execution => _execution;
        public FlowBlackboard Blackboard => _blackboard ??= new FlowBlackboard();

        private void Start()
        {
            if (_playOnStart)
            {
                StartFlow();
            }
        }

        private void Update()
        {
            _execution?.Tick(Time.deltaTime, _maximumTransitionsPerFrame);
        }

        private void OnDisable()
        {
            _execution?.Stop();
        }

        public bool StartFlow()
        {
            if (_graph == null)
            {
                Debug.LogError("Flow Graph Runner requires a Flow Graph.", this);
                return false;
            }

            _execution?.Stop();
            _execution = new FlowGraphExecution(_graph, Blackboard, this);
            return _execution.Start();
        }

        public void StopFlow()
        {
            _execution?.Stop();
        }

        public void ResetBlackboard()
        {
            if (_execution != null && _execution.Status == FlowExecutionStatus.Running)
            {
                Debug.LogWarning("Stop the active Flow Graph before resetting its Blackboard.", this);
                return;
            }

            Blackboard.Clear();
        }
    }
}
