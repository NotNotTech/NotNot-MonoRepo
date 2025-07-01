//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using NotNot.SimStorm.Engine;
//using NotNot.SimStorm.Tests.Helpers;
//using Xunit;
//using Xunit.Abstractions;

//namespace NotNot.SimStorm.Tests;


//public class TestEngineBasic : IDisposable
//{

//	private readonly ITestOutputHelper _output;

//	public TestEngineBasic(ITestOutputHelper output)
//	{
//		_output = output;
//		__.Test.InitTest(output);
//	}

//	public void Dispose()
//	{
//		__.Test.DisposeTest();
//	}

//	private Engine.Engine _InitEngineHelper()
//	{
//		var engine = new Engine.Engine()
//		{
//			Updater = new HeadlessUpdater()
//		};
//		engine.Initialize();

//		engine.Updater.Start();

//		return engine;
//	}


//	[Fact]
//	public async Task Engine_StartStop()
//	{
//		var engine = _InitEngineHelper();


//		await Task.Delay(100);

//		await engine.Updater.Stop();



//		__.Assert(engine.RootNode._lastUpdate._timeStats._frameId > 30);
//		engine.Dispose();
//		__.Throw(engine.IsDisposed);

//	}

//	[Fact]
//	public async Task SimPipeline_e2e()
//	{

//		using var manager = new SimManager(null) { };


//		var start = Stopwatch.GetTimestamp();
//		long lastElapsed = 0;


//		//add some test nodes
//		manager.Register(new TimestepNodeTest { ParentName = "root", Name = "A", TargetFps = 60 });
//		manager.Register(new DelayTest { ParentName = "root", Name = "A2" });

//		manager.Register(new DelayTest { ParentName = "A", Name = "B", _updateBefore = { "A2" }, _registeredWriteLocks = { "taco" } });
//		manager.Register(new DelayTest { ParentName = "A", Name = "B2", _registeredReadLocks = { "taco" } });
//		manager.Register(new DelayTest { ParentName = "A", Name = "B3", _registeredReadLocks = { "taco" } });
//		manager.Register(new DelayTest { ParentName = "A", Name = "B4!", _updateAfter = { "A2" }, _registeredReadLocks = { "taco" } });

//		manager.Register(new DelayTest { ParentName = "B", Name = "C" });
//		manager.Register(new DelayTest { ParentName = "B", Name = "C2" });
//		manager.Register(new DelayTest { ParentName = "B", Name = "C3", _updateAfter = { "C" } });




//		manager.Register(new DebugPrint { ParentName = "C3", Name = "DebugPrint" });


//		//List<string[]> expectedOrder = new()
//		//{
//		//	new string[] { "root" },
//		//	new string[] { "A" },
//		//	new string[] { "B" },
//		//	new string[] { "A2", "B2", "B3" },
//		//	new string[] {  "B4!"  },
//		//	new string[] { "C", "C2", "C3" },
//		//	new string[] { "DebugPrint" }
//		//};




//		var loop = 0;
//		while (true)
//		{

//			loop++;
//			lastElapsed = Stopwatch.GetTimestamp() - start;
//			start = Stopwatch.GetTimestamp();
//			//Console.WriteLine($" ======================== {loop} ({Math.Round(TimeSpan.FromTicks(lastElapsed).TotalMilliseconds,1)}ms) ============================================== ");
//			var finishedNodes = await manager.Update(TimeSpan.FromTicks(lastElapsed));


//			//count skipped
//			var skippedCount = manager._nodeRegistry.Count(pair => pair.Value.IsSkipped == true);
//			__.Assert(skippedCount + finishedNodes.Count == manager._nodeRegistry.Count,"likely race condition during execution?", skippedCount, finishedNodes.Count, manager._nodeRegistry.Count);



//			//ensure executed in order
//			var order = "";
//			for (var i = 0; i < finishedNodes.Count; i++)
//			{
//				var current = finishedNodes[i];
//				var name = current._node.Name;
//				order +=" > " + name ;

//				var beforeNodes = finishedNodes[0..i];
//				var afterNodes = finishedNodes[(i+1)..];

//				var updateAfter = current._updateAfter;

//				//ensure updateAfters ran before this
//				foreach (var nfs in current._updateAfter)
//				{
//					__.Assert(beforeNodes.Contains(nfs));
//				}
//				//ensure parent ran before this
//				if (current._parent is not null)
//				{
//					__.Assert(beforeNodes.Contains(current._parent));
//				}
//			}


//			//Console.WriteLine($"last Elapsed = {lastElapsed}");
//			if (loop > 2000)
//			{
//				break;
//			}

//			if (loop % 10 == 0)
//			{
//				//if (frame._stats._frameId % 200 == 0)
//				{
//					__.GetLogger()._EzInfo($"{loop} : {order}" );
//					//Debug.Print($"{loop} : {order}");
//				}
//			}
//		}



//	}



//	[Fact]
//	public async Task Engine_WorldWithChild()
//	{

//		var engine = _InitEngineHelper();



//		var timestepNodeTest = new TimestepNodeTest() { TargetFps = 10 };
//		engine.RootNode.AddChild(timestepNodeTest);



//		//engine.Updater.Start();


//		await Task.Delay(100);

//		await engine.Updater.Stop();
//		__.Throw(timestepNodeTest._lastUpdate._timeStats._frameId > 30);
//		__.Throw(engine.RootNode._lastUpdate._timeStats._frameId > 30);
//		engine.Dispose();
//		__.Throw(engine.IsDisposed);

//		GC.Collect();
//		await Task.Delay(100);
//	}



//}
