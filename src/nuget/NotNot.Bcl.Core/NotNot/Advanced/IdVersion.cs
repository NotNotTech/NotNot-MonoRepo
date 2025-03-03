// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Runtime.InteropServices;

namespace NotNot.Advanced;

/// <summary>
/// allows versioning of a location, so that a single ulong can be used to reference a unique object
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public record struct IdVersion : IComparable<ulong>, IComparable<IdVersion>, IEquatable<ulong>, IEquatable<IdVersion>
{
	/// <summary>
	/// location+version as a 64bit value
	/// </summary>
	[FieldOffset(0)]
	public ulong id;
	[FieldOffset(0)]
	public required uint _location;
	[FieldOffset(4)]
	public required uint _version;

	int IComparable<ulong>.CompareTo(ulong other)
	{
		return this.id.CompareTo(other);
	}

	int IComparable<IdVersion>.CompareTo(IdVersion other)
	{
		return this.id.CompareTo(other.id);
	}
	public bool Equals(IdVersion other)
	{
		return id == other.id;
	}

	public bool Equals(ulong other)
	{
		return id == other;
	}

	//public override bool Equals(object? obj)
	//{
	//	return obj is IdVersion other && Equals(other);
	//}

	public override int GetHashCode()
	{
		return id.GetHashCode();
	}
}