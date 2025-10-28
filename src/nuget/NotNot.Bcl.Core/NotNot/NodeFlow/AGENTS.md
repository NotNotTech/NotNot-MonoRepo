# VIBEGUIDE

## NodeFlow Architecture Philosophy
- General-purpose hierarchical execution framework for coordinated updates
- Parent-child node relationships with lifecycle management
- Time-based tick state tracking for deterministic execution
- Async-first initialization and update patterns
- Memory-efficient child management using spans
- Built on AsyncDisposeGuard for proper cleanup
- Domain-agnostic design suitable for any hierarchical processing need

## Critical Design Decisions
- **SlimNode Base Class**: Minimal node implementation with only essential features
- **RootNode Entry Point**: Single root node manages tick state for entire tree
- **TickState Immutability**: Record class ensures thread-safe tick propagation
- **Lifecycle Guards**: Call counter validation ensures base methods are called
- **No Multithreading Yet**: Current implementation is single-threaded
- **No Reattachment**: Initialized nodes cannot be reattached to different parents

# VIBECACHE

**LastCommitHash**: Initial creation
**YYYYMMDD-HHmm**: 20250922-1010

## Primary Resources
- [Hierarchical Task Networks](https://en.wikipedia.org/wiki/Hierarchical_task_network) - Pattern reference
- [Composite Pattern](https://refactoring.guru/design-patterns/composite) - Design pattern foundation
- [Actor Model](https://en.wikipedia.org/wiki/Actor_model) - Concurrent computation model

## Related Topics
- [../CLAUDE.md](../CLAUDE.md) - NotNot.Bcl.Core library context
- [../../CLAUDE.md](../../CLAUDE.md) - NotNot.Bcl.Core package root

## Child Topics
None - NodeFlow is a leaf topic

## E2E Scenario Summary
- Workflow Orchestration: Multi-step business process execution
- Simulation Framework: Time-stepped scientific/engineering calculations
- Batch Processing Pipeline: ETL and data transformation workflows
- Microservice Coordination: Distributed system task management
- Test Automation: Hierarchical test suite execution

## Architecture Overview

### Core Components

**SlimNode** ([SlimNode.cs](SlimNode.cs#L17))
- Base class for all nodes in the tree
- Manages parent-child relationships
- Handles initialization and update lifecycle
- Provides async disposal pattern
- Guards against incorrect base method calls

**RootNode** ([RootNode.cs](RootNode.cs#L4))
- Entry point for the node tree
- Manages global tick state
- Pumps updates through the tree
- Controls execution timing and frequency

**TickState** ([TickState.cs](TickState.cs#L9))
- Immutable record of current tick information
- Tracks elapsed time, turn count, and update count
- Supports time speed modification for simulation control
- Links to previous tick for delta calculations

### Lifecycle Flow

1. **Initialization Phase**
   - RootNode.RootInitialize() starts the process
   - Recursively initializes all children
   - Sets lifecycle cancellation token
   - Guards ensure single initialization

2. **Update Phase**
   - RootNode.RootUpdate() called at controlled intervals
   - Computes new TickState from wall clock delta
   - Propagates update through entire tree
   - Children updated within parent's OnUpdate

3. **Disposal Phase**
   - AsyncDisposeGuard ensures proper cleanup
   - Children disposed before parents
   - Cancellation tokens respected

### Memory Management
- Children stored in List<SlimNode>
- ReadOnlySpan access for iteration efficiency
- MemoryOwnerCopy for safe iteration during modifications
- No allocation for empty children collections

## Implementation Guidelines

### Creating Custom Nodes
```csharp
public class WorkflowStepNode : SlimNode
{
    protected override async ValueTask OnInitialize()
    {
        await base.OnInitialize(); // Initialize children first
        // Initialize resources, connections, etc.
    }

    protected override async ValueTask OnUpdate(TickState tick)
    {
        // Pre-processing logic
        await base.OnUpdate(tick); // Update children
        // Post-processing logic
    }
}
```

### Typical Usage Pattern
```csharp
var root = new RootNode();
await root.AddChild(new WorkflowStepNode());
await root.RootInitialize(cancellationToken);

// Execution loop
var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0));
while (await timer.WaitForNextTickAsync(cancellationToken))
{
    await root.RootUpdate(TimeSpan.FromSeconds(1.0));
}
```

## Use Cases

### Business Process Orchestration
- Multi-step approval workflows
- Document processing pipelines
- Order fulfillment sequences
- Batch job coordination

### Scientific/Engineering Simulations
- Finite element analysis steps
- Weather modeling iterations
- Circuit simulation updates
- Chemical reaction modeling

### Data Processing Pipelines
- ETL transformations
- Stream processing stages
- Map-reduce operations
- Data validation chains

### Distributed Systems
- Microservice orchestration
- Saga pattern implementation
- Event sourcing coordination
- Message processing hierarchies

## Known Limitations
- No multithreaded execution support
- Cannot reattach initialized nodes
- No node pooling or recycling
- Simple linear child iteration (no priority/ordering)
- No built-in serialization support

## Testing Considerations
- Use deterministic tick states for reproducible tests
- Mock time progression through controlled deltas
- Verify base method calls via call counter
- Test disposal cascades through tree
