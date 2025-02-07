


# FAQ

### a few questions:
- i guess the entity creation in the spawner is just an example? So in the end the definition of which components an entity has will be an editable asset file?
- why did you delay new entities to the end of the tick? It will result in the entity to live and update in the next tick, meaning that the current creation component wont be able to do something with the entity while creating it.
- Health system does a query components every tick? I guess when having a lot of components, it ends up in a lot of iterations over the same collections?
- Is there any particular reason to split a component into a system and a component? I guess its better for performance? Anyway does that mean that all components will always tick and query for all entities with a specific system and only handle these?

### answers
1) yeah all those systems are just simple examples showing how to use the api.     You can create an entity from a file, no problem with this pattern.

As it impacts the answers to your other questions, I'll describe a bit on how this ecs works.
This ecs is a "data first" approach.  All entities are defined by their components, which are just data.   All game computations should be done via Systems.  Systems will itterate through components they care about and do whatever work on them they need.  If you have external systems (maybe physics or rendering or networking) these can be synced with the game state via systems.

The value in this approach is performance because:
- (By default) No objects are allocated for game objects (entities).  Meaning you can have thousands of entities and no additional pressure is put on the GarbageCollector.  (This was/is a big problem with C# games.  you'll see the game freeze for a moment every few seconds).  I said "by default" because this is true if your entities components are unmanaged structs.   structs with references to objects are more costly, and classes as components are even more costly.  This is fine for specialized entities such as players which there are few of, but should be avoided for entities where you will have hundreds or thousands of.
- cache locality via packed data:  All components of the same type are stored in arrays, which makes itterating them as fast as is litterally possible, as the next item is in the same cpu cache line and the fetch prediction is easy
- Fast data copy.   copying data from one component to another (such as position from physics into the transform position)  is as fast as possible:  just a memcpy (Array copy).  This lets sync points between multithreaded (see below) systems be as short as possible.

There are other benefits too:
- a Systems approach simplifies game logic.  For example all physics code can be written in a single system and all entities that include a Physics component will be processed automatically.    No need to register an item for rendering or otherwise track lifecycle as it builtin to  the ecs system.   I didn't include it in the api example, but there will be ways to filter out entities too, and change an entities components dynamically
- Easy to multithread.  Systems will specify what components they want Read access to, and what they want Write access to.  with only this information, the ecs system can determine what systems can be run in parallel, and what need to run one-at-a-time.  Thus all systems will auto-magically be scheduled to run on multiple threads with no risk of race-conditions (and no need for writing thread-safe code!) 
- Easy to understand the data layout.  It is layed out like an excel spreadsheet.  The Spreadsheet is an archetype,   Entity is a row, Component is a column,   the component data is a cell.   Systems gain Read/Write locks over column(s), and loop over the cells of the column.


2) Why delete at end of tick:   I am not sure we "have to" do add/removes between updates, but I think that we maybe have to.        Because of how the ecs system is designed, A system has a Read/Write lock on a component (column), not on the entity (row).  A delete would delete the entire row, and doing a delete within a system might step on other Systems that hold a lock on another column.  Another example is with a hierarchy, you would want to delete all children when the parent is deleted.  A child might have different components than a parent and so the System would not be aware of the children.
To address your question about "do something with the entity when it's created" I think we could allow for a callback delegate to be called when the entity is created, so that defaults can be populated.

3) HealthSystem query every tick:  yes health system does iterate through all HealthComponents (for all entities in the game that have a health component).  Later we can add an update-frequency setting for systems so that we can have some systems like Rendering update at a certain rate, Physics at a different rate.      Regarding "lots of iteration over the same collections", I'm not sure if you were talking about the HealthSystem being fired every update, but that could be solved right now by adding a if in the Update() function that  just checking the game time and exit if less than 0.5 seconds have passed.

4) splitting system and component:  yes, you can read the "perf benefits" and "other benefits" above.  Regarding your question
 | Anyway does that mean that all components will always tick and query for all entities with a specific system and only handle these?
That's not the usage pattern.  The pattern would be to query for all of 1 or more Component types.   All entities that have all the specified components would be given.
For example, if you have 100 entities with Transform components , 20 entities with Transform+Turret, and 5 entities with Transform+Turret+Health components, if you query for:
- Health = 5 Results
- Turret+Health = 5 results
- Transform+Turret+Health = 5 results
-  Transform+Turret = 25 results.
- Transform = 125 results.
Later the system will also allow for negative filters.  such as Transform+Turret-Health = 120 results



 a great article explaining "Entity Component" architectures vs "Entity Component System" architectures: https://medium.com/ingeniouslysimple/entities-components-and-systems-89c31464240d


 ## How the SimNode execution framework works:
 The basic features for multithreaded "SimNode" execution framework.  `SimNode` will be the base type of a `System`.  You  attach them in a tree hierarchy, with the parent executing first, children executing in parallel.      
To control execution order you can specify both "updateAfter" and "updateBefore" lists, so that you can make sure a system like `PhysicsWorldUpdate` finishes before `Rendering` starts.   
Each node can specify resources they need Read or Write access to.  This is how multithreading is made "invisible" to the developer.  So that a system that needs Write access to Transform components will run singularly (no other systems that read/write Transform will be allowed to run at the same time), while Systems that need Read access to Transform can run in parallel.


