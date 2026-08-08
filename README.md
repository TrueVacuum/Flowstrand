# Flowstrand Framework

Flowstrand is an extensible visual execution-flow framework for Unity. Developers implement
strongly typed flow nodes in C#, while designers compose and configure those nodes in a GraphView
editor.

## Features

- ScriptableObject graph assets with polymorphic `[SerializeReference]` nodes.
- Runtime and Editor assemblies kept separate.
- Execution-flow ports with stable node, port, and edge IDs.
- Sequential flow, Blackboard branching, delays, parallel branches, Join All, and Join Any.
- Dynamic ports with safe connection cleanup.
- Validation for broken references, invalid capacities, unreachable nodes, and incomplete joins.
- Play mode tracing with selectable Runner, active/waiting/succeeded/failed/cancelled states.
- Undo/redo, copy/paste, mouse-position paste, zoom, pan, and search-based node creation.
- AI-readable graph context exported from the live ScriptableObject through Unity's AssetDatabase.

## Requirements

- Unity 6.3 or newer.
- The editor UI uses `UnityEditor.Experimental.GraphView`; runtime code does not depend on it.

## Installation

In Unity Package Manager, choose **Add package from git URL** and enter:

```text
https://github.com/TrueVacuum/Flowstrand.git#v0.1.0
```

During local development, choose **Add package from disk** and select `package.json`.

## Quick start

1. Create a graph with **Assets > Create > Flowstrand > Flow Graph**. A protected Entry node is
   created automatically.
2. Double-click the graph asset to open the Flowstrand editor.
3. Right-click the canvas to create nodes and connect their execution ports.
4. Add `FlowGraphRunner` to a GameObject and assign the graph.
5. Enter Play mode. Select the Runner from the graph window's **Debug Runner** dropdown to inspect
   execution state.

An unconnected successful output ends that execution branch naturally; an End node is not needed.

## Custom nodes

Project assemblies can define nodes by referencing `Flowstrand.Runtime`:

```csharp
using System;
using UnityEngine;
using Flowstrand;

[Serializable]
[FlowNodeMenu("Game/Show Message")]
public sealed class ShowMessageNode : FlowNode
{
    [SerializeField] private string _message;

    public override FlowNodeStatus OnUpdate(FlowNodeContext context)
    {
        Debug.Log(_message, context.Owner);
        return FlowNodeStatus.Succeeded;
    }
}
```

Serializable fields are drawn automatically in the node and included automatically in AI Context.
Store per-execution mutable state in `FlowNodeContext` or `FlowBlackboard`, not in serialized node
fields, because a graph definition may be executed by multiple Runners simultaneously.

## Running without FlowGraphRunner

```csharp
FlowBlackboard blackboard = new FlowBlackboard();
blackboard.Set("hasKey", true);

FlowGraphExecution execution = new FlowGraphExecution(graph, blackboard, owner);
execution.Start();

// Call from the owning update loop.
execution.Tick(Time.deltaTime);
```

Each `FlowGraphExecution` has independent Blackboard, branch state, join rounds, runtime history,
and events.

## Parallel and join semantics

- `Parallel` starts one main-thread execution branch for each connected dynamic output.
- `Join All` emits once after every declared input has received one arrival for that round.
- `Join Any` emits for the first arrival in a round and absorbs later arrivals from the remaining
  inputs. It does not cancel upstream work.
- Use `+` and `-` on dynamic nodes to add or remove stable ports.

All branches are cooperatively advanced on Unity's main thread; Flowstrand does not create worker
threads.

## Validation and debugging

Use **Validate** in the graph toolbar. Graphs with validation errors are rejected before execution.
Warnings identify suspicious but executable structures, such as unreachable nodes or unused
Parallel branches.

Play mode colors:

- Cyan: running
- Yellow: Join All waiting
- Green: succeeded
- Red: failed
- Orange: cancelled

## AI integration

When the package initializes, it installs a project-local, read-only context bridge under
`Library/Flowstrand` and adds a managed Flowstrand section to the project root `AGENTS.md` without
overwriting existing instructions. An AI agent can then request a graph by name or asset path and
receive nodes, serialized configuration, ports, edges, and validation results from the live
ScriptableObject. Unity must have the project open and finished compiling.

## Samples

Import **Basic Flow** from the Package Manager Samples section. Its README explains how to run the
included sequential/parallel/join graph.

## License

MIT. See [LICENSE.md](LICENSE.md).

Flowstrand is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity is
a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and
elsewhere.
