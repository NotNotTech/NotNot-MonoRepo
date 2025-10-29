// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] By default, this file is licensed to you under the AGPL-3.0.
// [!!] However a Private Commercial License is available. 
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] ------------------------------------------------- 
// [!!] Contributions Guarantee Citizenship! 
// [!!] Would you like to know more? https://github.com/NotNotTech/NotNot 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using NotNot.SimStorm._scratch.Ecs.Allocation;
using System.Collections.Concurrent;
using System.Numerics;
using NotNot.Advanced;
using NotNot.Collections.SpanLike;

namespace NotNot.SimStorm._scratch.Rendering;

public class RenderPacket3d : IRenderPacketNew
{
	public ReadMem<EntityMetadata> entityMetadata;

	public ReadMem<Matrix4x4> instances;

	public IRenderTechnique3d technique;

	public RenderPacket3d(IRenderTechnique3d technique)
	{
		this.technique = technique;
		technique.ConstructPacketProperties(this);
	}

	public int RenderLayer { get; } = 0;

	public bool IsInitialized { get; set; }

	public bool IsEmpty
	{
		get
		{
			if (technique == null) { return true; }

			if (instances.Length > 0)
			{
				return false;
			}

			return true;
		}
	}

	public void Initialize()
	{
		if (IsInitialized)
		{
			return;
		}

		IsInitialized = true;

		__.GetLogger()._EzErrorThrow<SimStormException>(technique.IsInitialized == false);
		if (technique.IsInitialized == false)
		{
			technique.Initialize();
		}
	}

	public void DoDraw()
	{
		technique.DoDraw(this);
	}

	public int CompareTo(IRenderPacketNew? other)
	{
		return 0;
	}
}

/// <summary>
///    how to render something
/// </summary>
public interface IRenderTechnique3d
{
	public bool IsInitialized { get; set; }

	/// <summary>
	///    allows the renderTechnique to inform the renderPacket about things like what renderLayer, etc
	/// </summary>
	/// <param name="renderPacket"></param>
	public void ConstructPacketProperties(RenderPacket3d renderPacket);

	public void DoDraw(RenderPacket3d renderPacket);

	public void Initialize();
}

public class RenderFrame : FramePacketBase
{
	public Vector3 position = new(0.0f, 10.0f, 10.0f); // Camera3D position
	public ConcurrentQueue<IRenderPacketNew> renderPackets = new();
	public Vector3 target = new(0.0f, 0.0f, 0.0f); // Camera3D looking at point
	public Vector3 up = new(0.0f, 1.0f, 0.0f); // Camera3D up vector (rotation towards target)

	protected override void OnInitialize()
	{
	}

	protected override void OnRecycle()
	{
		renderPackets.Clear();
		position = default;
		target = default;
		up = default;
	}
}

public interface IRenderPacketNew : IComparable<IRenderPacketNew>
{
	/// <summary>
	///    lower numbers get rendered first
	/// </summary>
	public int RenderLayer { get; }

	public bool IsInitialized { get; }
	public bool IsEmpty { get; }

	/// <summary>
	///    called by the RenderWorker.  calls the rendertechnique
	/// </summary>
	public void DoDraw();

	public void Initialize();
}

public class RenderDescription
{
	public List<IRenderTechnique3d> techniques = new();
}
