# SlimGraph / NodeFlow

Hierarchical execution framework for coordinated updates. Parent-child node relationships with lifecycle management.

## Core Components

| Component | Purpose |
|-----------|---------|
| `SlimNode` | Base class for all nodes - parent-child, init/update lifecycle |
| `RootNode` | Entry point, manages global tick state |
| `TickState` | Immutable record of current tick (time, count, delta) |
| `SlimMsDiHost` | MS Generic Host adapter (BackgroundService) |

## Critical Design Decisions

- **Single-threaded** - No multithreading support yet
- **No reattachment** - Initialized nodes cannot move to different parents
- **Lifecycle guards** - Call counter validates base methods called
- **TickState immutable** - Record class for thread-safe propagation

## Lifecycle Flow

1. **Initialize**: `RootNode.RootInitialize()` → recursive child init
2. **Update**: `RootNode.RootUpdate(delta)` → compute TickState → propagate tree
3. **Dispose**: AsyncDisposeGuard → children before parents

## Usage

```csharp
var root = new RootNode();
await root.AddChild(new WorkflowStepNode());
await root.RootInitialize(ct);

var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0));
while (await timer.WaitForNextTickAsync(ct))
    await root.RootUpdate(TimeSpan.FromSeconds(1.0));
```

## SlimMsDiHost (MS Generic Host)

```csharp
builder.Services.AddHostedService(_ => new SlimMsDiHost<MyRoot>(
    () => new MyRoot { MsDIContainer = container },
    tickInterval: TimeSpan.FromMilliseconds(50)  // default 50ms
));
```

## Known Limitations

- No multithreaded execution
- No node pooling/recycling
- Simple linear child iteration (no priority)
- No built-in serialization
