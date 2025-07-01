// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] By default, this file is licensed to you under the AGPL-3.0.
// [!!] However a Private Commercial License is available. 
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] ------------------------------------------------- 
// [!!] Contributions Guarantee Citizenship! 
// [!!] Would you like to know more? https://github.com/NotNotTech/NotNot 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

//namespace SampleApi
//{


//	public class World
//	{


//		/// <summary>
//		/// gets (or creates) the proper archetype and creates an entity on it.
//		/// Enqueued action:  execution is delayed until the engine Housekeeping phase (at start of update loop)
//		/// </summary>
//		public EntityHandle EnqueueCreateEntity<TComponent1, TComponent2, TComponent3, TComponent4>(
//			ref TComponent1 c1,
//			ref TComponent2 c2,
//			ref TComponent3 c3,
//			ref TComponent4 c4
//		)
//		{
//			throw new NotImplementedException();
//		}

//		/// <summary>
//		/// get an archetype that has a single component of your specified type
//		/// </summary>
//		/// <typeparam name="TComponent1"></typeparam>
//		/// <returns></returns>
//		public IEnumerable<Archetype> QueryArchetypes<TComponent1>()
//		{
//			throw new NotImplementedException();
//		}

//		/// <summary>
//		/// get an archetype that has both components of your specified types
//		/// </summary>
//		/// <typeparam name="TComponent1"></typeparam>
//		/// <typeparam name="TComponent2"></typeparam>
//		/// <returns></returns>
//		public IEnumerable<Archetype> QueryArchetypes<TComponent1, TComponent2>()
//		{
//			//IMPLEMENTATION NOTE:  example linq function to do this work.   it works but probably can be optimized
//			var query1 = from archetype in _archetypes
//						 from dataColumn in archetype._componentColumns
//						 where dataColumn._componentType == typeof(TComponent1)
//						 select archetype;

//			var query2 = from archetype in query1
//						 from dataColumn in archetype._componentColumns
//						 where dataColumn._componentType == typeof(TComponent2)
//						 select archetype;

//			return query2;
//		}

//		/// <summary>
//		/// useful for doing bulk calculations (memcpy) to other components
//		/// </summary>
//		/// <typeparam name="TComponent1"></typeparam>
//		/// <returns></returns>
//		public IEnumerable<(EntityHandle[] entities, TComponent1[] c1, TComponent2[] c2)> QueryComponentColumns<TComponent1, TComponent2>()
//		{
//			//IMPLEMENTATION NOTE:  need to break up entityHandle storage into DataChunk aligned pages also
//			throw new NotImplementedException();
//		}

//		/// <summary>
//		/// Easiest way to do work on components, this handles itteration so you can focus on your processing logic.   see HealthSystem example below.
//		/// </summary>
//		/// <typeparam name="TComponent1"></typeparam>
//		/// <param name="callback"></param>
//		public void QueryComponents<TComponent1>(ComponentQueryCallback<TComponent1> callback)
//		{
//			//var result = _world.QueryComponentColumns<TComponent1>();

//			//foreach (var (handleArray, healthArray) in result)
//			//{
//			//	for (var i = 0; i < handleArray.Length; i++)
//			//	{
//			//		if (handleArray[i].isAlive != true)
//			//		{
//			//			continue;
//			//		}
//			//	}
//			//}
//			throw new NotImplementedException();
//		}
//		public void QueryComponents<TComponent1, TComponent2>(ComponentQueryCallback<TComponent1, TComponent2> callback)
//		{
//			throw new NotImplementedException();
//		}
//		public TSystem GetSystem<TSystem>() where TSystem : SystemBase
//		{
//			throw new NotImplementedException();
//		}
//	}


//	/// <summary>
//	/// all game logic (regarding entities) should be performed via systems attached to a specific World.  
//	/// </summary>
//	public abstract class SystemBase
//	{
//		//IMPLEMENTATION NOTE:  for multithreading, need to provider for data access permissions (read/write) for each component type used
//		public World _world;

//		public abstract void Update();
//	}
//}
/////// <summary>
/////// example use of the above ECS api
/////// </summary>
////namespace ExampleUse
////{
////	/// <summary>
////	/// a component that handles 3d object position/transforms
////	/// </summary>
////	public struct TransformComponent : IComponent
////	{
////		public Matrix4x4 _transform;
////	}

////	/// <summary>
////	/// a game logic component for tracking health of the character
////	/// </summary>
////	public struct HealthComponent : IComponent
////	{
////		public float value;
////	}

////	/// <summary>
////	/// stores per-entity details required to render
////	/// </summary>
////	public struct ModelInfoComponent : IComponent
////	{
////		//while best for components to be unmanaged structs, they don't have to be.
////		public string modelName;
////	}


////	/// <summary>
////	/// example of class based component 
////	/// </summary>
////	public class TurretsComponent : IComponent
////	{
////		public List<TurretInfo> mounts;

////		public class TurretInfo
////		{
////			public bool isActive;
////			public Vector3 positionOffset;
////			public string type;
////			public float damageBuff;
////		}
////	}
////	/// <summary>
////	/// example gameplay system that spawns characters when there are less than 4.
////	/// </summary>
////	public class SpawnerSystem : SystemBase
////	{
////		Random _rand = new();

////		public int liveCount = 0;

////		public override void Update()
////		{
////			//Spawn units until there are 5
////			{
////				//function to generate a random position along X&Y axis
////				Func<Matrix4x4> getXform = () =>
////				{
////					var transform = Matrix4x4.Identity;
////					transform.Translation = new() { X = _rand.Next(-10, 10), Y = _rand.Next(-10, 10), Z = 0, };

////					return transform;
////				};

////				while (liveCount < 5)
////				{
////					var health = new HealthComponent { value = 100 };

////					var transform = new TransformComponent() { _transform = getXform(), };

////					var modelInfo = new ModelInfoComponent() { modelName = "myPath/model.gltf" };

////					var turrets = new TurretsComponent();
////					turrets.mounts.Add(new() { damageBuff = 22.2f, isActive = true, type = "laser", positionOffset = new(0.5f, 0, 0), });

////					var entityHandle = _world.EnqueueCreateEntity(ref health, ref transform, ref modelInfo, ref turrets); //entity creation will be delayed until start of next game loop
////					liveCount++;
////				}
////			}

////		}

////	}

////	/// <summary>
////	/// example gameplay system that will slowly kill characters over time.  Thus requiring the above SpawnerSystem to spawn more.
////	/// </summary>
////	public class HealthSystem : SystemBase
////	{
////		Random _rand = new();

////		SpawnerSystem _spawnerSystem;
////		public override void Update()
////		{
////			if (_spawnerSystem == null)
////			{
////				_spawnerSystem = _world.GetSystem<SpawnerSystem>();
////			}

////			_world.QueryComponents(
////				(ref EntityHandle entityHandle, ref HealthComponent health) =>
////				{
////					if (health.value > 0)
////					{
////						health.value -= _rand.NextSingle();

////						if (health.value <= 0)
////						{
////							entityHandle.EnqueueDispose(); //entity deletion will be delayed until start of next game loop
////							_spawnerSystem.liveCount--; //decrement liveCount so another can be respawned next update of the SpawnerSystem
////						}
////					}
////				}
////			);
////		}
////	}

////}

