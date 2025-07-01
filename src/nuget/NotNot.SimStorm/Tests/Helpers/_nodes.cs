using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NotNot.SimStorm.Tests.Helpers;

public class DebugPrint : SimNode
{
	ILogger _log = __.GetLogger();
	//public DebugPrint(string name, SimManager manager, string parentName) : base(name, manager, parentName)
	//{
	//}
	//public DebugPrint(string name, SimManager manager, SimNode parent) : base(name, manager, parent)
	//{
	//}
	private long avgMs = 0;
	protected override async Task OnUpdate(Frame frame, NodeFrameState nodeState)
	{
		//if (frame._stats._frameId % 200 == 0)
		{
			//_log._EzInfo($"{Name} frame {frame} stats={_lastUpdate}");
		}

	}
}
public class TimestepNodeTest : FixedTimestepNode
{
	ILogger _log = __.GetLogger();
	//public TimestepNodeTest(string name, SimManager manager, string parentName) : base(name, manager, parentName)
	//{
	//}
	//public TimestepNodeTest(string name, SimManager manager, SimNode parent) : base(name, manager, parent)
	//{
	//}

	protected override async Task OnUpdate(Frame frame, NodeFrameState nodeState)
	{
		await Task.Delay(0);
		//Console.WriteLine("WHUT");
		//if (frame._stats._frameId % 200 == 0)
		{
			var indent = HierarchyDepth * 3;
			//_log._EzInfo($"{Name.PadLeft(indent + Name.Length)}");
		}
		//await Task.Delay(100000);
	}
}
public class DelayTest : SimNode
{
	ILogger _log = __.GetLogger();
	//public DelayTest(string name, SimManager manager, string parentName) : base(name, manager, parentName)
	//{
	//}
	//public DelayTest(string name, SimManager manager, SimNode parent) : base(name, manager, parent)
	//{
	//}
	private Random _rand = new();
	protected override async Task OnUpdate(Frame frame, NodeFrameState nodeState)
	{
		if (Name == "C")
		{
			//_log._EzInfo($"{Name} frame {frame} stats={_lastUpdate}");
		}
		////Console.WriteLine("WHUT");
		if (frame._stats._frameId % 200 == 0)
		{
			var indent = HierarchyDepth * 3;
			__.Assert();
			//_log._EzInfo($"{Name.PadLeft(indent + Name.Length)}       START");
			//await Task.Delay(_rand.Next(10,100));
			//__DEBUG.Assert(false);
			//_log._EzInfo($"{Name.PadLeft(indent + Name.Length)}       END");
		}
	}
}
