# ECS Allocation

The Page is an important feature because in the ECS system these things need to be created and managed for each entity created:
- EntityHandle
- Each component the entity will use
  - Metadata
  - WorldXform
  - Physics info
  - rendering info
  - etc.... (whatever arbitrary data you want)

As part of managing this data, you need to:
- Query one, or multiple entities (it's components) through the course of teh game
- create/destroy entities and their components
- Systems need to efficiently query component data

To do this, the Allocation system was created.  It is comprised of the following main parts:
- `Page`: one per archetype.  This manages allocation and tracking of all that archetypes components  
- `Chunk<TComponent>`: An array holding components of a certain type, for some of the entities of the archetype.  Imagine an archetype with 10,000 entities.  Instead of putting all the Xform components in a single huge array, we split it into smaller arrays, called chunks.  
- `PageHandle`: tracks and provides direct access to each component for a given Entity.  If the Page is set to `autoPack==true` (the default) this handle is only good for the current frame.  After that it needs to be reaquired.  This is provided for flexibility but for builk component query/write operations it is much more efficient to use the `Page.Query()` methods


## Layout

### Chunk
A chunk holds a "chunk" of the components of a specific type for a given Page (for a given Archetype).   
Effectively `Chunk == TComponent[]` array.


### Column
A Column is a collection of all chunks for the given TComponent for the Page.  
Effectively a `Column == List<Chunk>`.

### Page 
Is a class that holds a collection of columns, one per `TComponent`.  
Effectively a `Page == Dictionary<typeof(TComponent),Column>`

### Slot
Is a single component for a single entity.  These can reverse-lookup their owner entity by inspecting a special `PageMeta` component that is added to each


Here is an example showing how thigs are laid out logically in memory:


```
 ┌──PAGE01─────────────────────────────────────────────────────────────────────────┐
 │                                                                                 │
 │    ┌───COLUMN<T1>────────────────────┐  ┌───COLUMN<T2>────────────────────┐     │
 │    │                                 │  │                                 │     │
 │    │  ┌──CHUNK0─────┬──────┬──────┐  │  │  ┌──CHUNK0─────┬──────┬──────┐  │     │
 │    │  │ Slot0│ Slot1│ Slot2│ Slot3│  │  │  │ Slot0│ Slot1│ Slot2│ Slot3│  │     │
 │    │  └──────┴──────┴──────┴──────┘  │  │  └──────┴──────┴──────┴──────┘  │     │
 │    │                                 │  │                                 │     │
 │    │  ┌──CHUNK1─────┬──────┬──────┐  │  │  ┌──CHUNK1─────┬──────┬──────┐  │     │
 │    │  │ Slot0│ Slot1│ Slot2│ Slot3│  │  │  │ Slot0│ Slot1│ Slot2│ Slot3│  │     │
 │    │  └──────┴──────┴──────┴──────┘  │  │  └──────┴──────┴──────┴──────┘  │     │
 │    │                                 │  │                                 │     │
 │    └─────────────────────────────────┘  └─────────────────────────────────┘     │
 │                                                                                 │
 └─────────────────────────────────────────────────────────────────────────────────┘

 ┌──PAGE02─────────────────────────────────────────────────────────────────────────┐
 │                                                                                 │
 │                                         ┌───COLUMN<T2>────────────────────┐     │
 │                                         │                                 │     │
 │                                         │  ┌──CHUNK0─────┬──────┐         │     │
 │                                         │  │ Slot0│ Slot1│ Slot2│         │     │
 │                                         │  └──────┴──────┴──────┘         │     │
 │                                         │                                 │     │
 │                                         │                                 │     │
 │                                         │                                 │     │
 │                                         │                                 │     │
 │                                         │                                 │     │
 │                                         └─────────────────────────────────┘     │
 │                                                                                 │
 └─────────────────────────────────────────────────────────────────────────────────┘
```

In the above, Page01 has two types of `TComponent` assigned to it.  Each TComponent has it's own column, and each chunk is set to have 4 slots each.   (The Page sets how long each chunk it contains will be).

Page2 only has one component assigned to it, and it's chunk size is 3.  Regardless of how many entities each Page tracks, the chunk size will be the same.   As more entities are added/removed, more chunks will be added/removed


