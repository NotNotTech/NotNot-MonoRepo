// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using NotNot.Advanced;
using NotNot.Collections;
using NotNot.Collections.SpanLike;

namespace NotNot.Collections;

/// <summary>
/// A read-only handle for referencing an allocated slot in a `RefSlotStore{T}` collection.
/// Packed into 32 bits for maximum performance:
/// - Bit 31: IsAllocated (1 bit)
/// - Bits 8-30: Index (23 bits, max 8,388,607)
/// - Bits 0-7: Version (8 bits, max 255)
/// </summary>
public readonly record struct SlotHandle : IComparable<SlotHandle>
{
	public int CompareTo(SlotHandle other)
	{
		return _packedValue.CompareTo(other._packedValue);
	}

	/// <summary>
	/// Direct access to the packed value for performance-critical scenarios
	/// </summary>
	public readonly uint _packedValue;

	public static SlotHandle Empty { get; } = default;


	/// <summary>
	/// Creates a SlotHandle from a packed value
	/// </summary>
	public SlotHandle(uint packed)
	{
		_packedValue = packed;
		_AssertOk();
	}

	/// <summary>
	/// Creates a new SlotHandle with the specified values
	/// </summary>
	public SlotHandle(int index, byte version, bool isAllocated)
	{
		// Validate ranges
		__.AssertIfNot(index >= 0 && index <= 0x7FFFFF, "Index must fit in 23 bits");
		__.AssertIfNot(version <= 0xFF, "Version must fit in 8 bits");

		// Pack the values
		_packedValue = ((uint)(isAllocated ? 1 : 0) << 31) |
					 ((uint)(index & 0x7FFFFF) << 8) |
					 ((uint)version & 0xFF);

		_AssertOk();
	}

	/// <summary>
	/// for internal use only: internal reference to the array/list location of the data
	/// </summary>
	public int Index
	{
		get => (int)((_packedValue >> 8) & 0x7FFFFF);
	}

	/// <summary>
	/// for internal use only: ensures the handle is not reused after freeing
	/// </summary>
	public byte Version
	{
		get => (byte)(_packedValue & 0xFF);
	}

	/// <summary>
	/// mostly for internal use: if the handle was properly allocated by a collection
	/// </summary>
	public bool IsAllocated
	{
		get => (_packedValue & 0x80000000U) != 0;
	}

	[Conditional("DEBUG")]
	private void _AssertOk()
	{
		__.AssertIfNot(IsAllocated);
		__.AssertIfNot(Version > 0, "assume version is >=1.  will remove IsAllocated bit to use that instead");

	}
}
