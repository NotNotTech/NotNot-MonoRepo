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
/// - Bits 10-31: Index (22 bits, max 4,194,303)
/// - Bits 0-9: Version (10 bits, max 1,023, never 0)
/// IsAllocated is implicit: true when _packedValue != 0
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


	///// <summary>
	///// Creates a SlotHandle from a packed value
	///// </summary>
	//public SlotHandle(uint packed)
	//{
	//	// Validate that non-zero packed values don't have version=0
	//	if (packed != 0)
	//	{
	//		short extractedVersion = (short)(packed & 0x3FF);
	//		if (extractedVersion == 0)
	//		{
	//			throw new InvalidOperationException("Non-zero packed value with zero version is invalid");
	//		}
	//	}
	//	_packedValue = packed;
	//	_AssertOk();
	//}

	/// <summary>
	/// Creates a new SlotHandle with the specified index and version.
	/// Version must be non-zero for implicit allocation tracking.
	/// </summary>
	public SlotHandle(int index, short version)
	{
		// Validate version is non-zero (required for implicit allocation check)
		if (version == 0)
		{
			throw new ArgumentOutOfRangeException(nameof(version), "Version must be non-zero for implicit allocation tracking");
		}

		// Validate ranges
		__.AssertIfNot(index >= 0 && index <= 0x3FFFFF, "Index must fit in 22 bits");
		__.AssertIfNot(version > 0 && version <= 0x3FF, "Version must be 1-1023 (10 bits, non-zero)");

		// Pack: Index in bits 10-31, Version in bits 0-9
		_packedValue = ((uint)(index & 0x3FFFFF) << 10) |
					 ((uint)version & 0x3FF);

		_AssertOk();
	}

	/// <summary>
	/// for internal use only: internal reference to the array/list location of the data
	/// </summary>
	public int Index
	{
		get => (int)((_packedValue >> 10) & 0x3FFFFF);
	}

	/// <summary>
	/// for internal use only: ensures the handle is not reused after freeing
	/// </summary>
	public short Version
	{
		get => (short)(_packedValue & 0x3FF);
	}

	/// <summary>
	/// mostly for internal use: if the handle was properly allocated by a collection.
	/// True when _packedValue is non-zero (implicit allocation tracking).
	/// </summary>
	public bool IsAllocated
	{
		get => _packedValue != 0;
	}

	[Conditional("DEBUG")]
	private void _AssertOk()
	{
		// Allow _packedValue == 0 for unallocated (default) handles
		if (_packedValue == 0)
		{
			return;
		}

		// Allocated handles must have version > 0
		__.AssertIfNot(Version > 0, "Allocated handles must have non-zero version");
	}
}
