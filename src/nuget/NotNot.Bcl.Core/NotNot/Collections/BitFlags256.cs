// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NotNot.Collections.Specialized;

/// <summary>
/// Represents a set of bit flags as a 256-bit value using Bytes32 storage.
/// </summary>
public struct BitFlags256
{
	/// <summary>
	/// The maximum number of flags supported, which is 256.
	/// </summary>
	public const int MaxFlags = 256;

	/// <summary>
	/// A BitFlags256 instance where all flags are set.
	/// </summary>
	public static readonly BitFlags256 All = CreateAll();

	/// <summary>
	/// A BitFlags256 instance where no flags are set.
	/// </summary>
	public static readonly BitFlags256 None = default;

	/// <summary>
	/// The raw 256-bit buffer value of the flags.
	/// </summary>
	public Bytes32 RawBuffer;

	public BitFlags256()
	{
	}

	/// <summary>
	/// Initializes a new instance of the BitFlags256 struct with a specific raw buffer.
	/// </summary>
	/// <param name="rawBuffer">The initial value of the flags.</param>
	public BitFlags256(Bytes32 rawBuffer)
	{
		RawBuffer = rawBuffer;
	}

	/// <summary>
	/// Initializes a new instance of the BitFlags256 struct by copying another instance.
	/// </summary>
	/// <param name="other">The instance to copy.</param>
	public BitFlags256(BitFlags256 other)
	{
		RawBuffer = other.RawBuffer;
	}

	/// <summary>
	/// CRITICAL: Re-fetch span per operation to avoid lifetime issues.
	/// Only call on ref-accessed instances to ensure span references correct memory.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Span<ulong> GetULongsUnsafe()
	{
		return RawBuffer.AsSpan()._AsULongs();
	}

	/// <summary>
	/// Creates a BitFlags256 with all bits set to 1.
	/// </summary>
	private static BitFlags256 CreateAll()
	{
		BitFlags256 result = default;
		result.GetULongsUnsafe().Fill(ulong.MaxValue);
		return result;
	}

	/// <summary>
	/// Asserts that a flag index is within the valid range.
	/// </summary>
	/// <param name="index">The index of the flag to check.</param>
	private static void AssertIndexInRange(int index)
	{
		if ((uint)index >= MaxFlags)
			throw new ArgumentOutOfRangeException(nameof(index), $"Index out of bounds. Expected range is 0 to {MaxFlags - 1}");
	}

	/// <summary>
	/// Gets the state of a specific flag by index.
	/// Sequential bit indexing: bits 0-63 → long[0], bits 64-127 → long[1], etc.
	/// </summary>
	/// <param name="index">The index of the flag.</param>
	/// <returns>True if the flag is set, false otherwise.</returns>
	public bool GetFlagIndex(int index)
	{
		AssertIndexInRange(index);
		var longs = GetULongsUnsafe();
		int longIndex = index >> 6;      // Divide by 64
		int bitOffset = index & 63;      // Modulo 64
		return (longs[longIndex] & (1UL << bitOffset)) != 0;
	}

	/// <summary>
	/// Sets or clears a specific flag by index.
	/// Sequential bit indexing: bits 0-63 → long[0], bits 64-127 → long[1], etc.
	/// </summary>
	/// <param name="index">The index of the flag.</param>
	/// <param name="value">True to set the flag, false to clear it.</param>
	public void SetFlagIndex(int index, bool value)
	{
		AssertIndexInRange(index);
		var longs = GetULongsUnsafe();
		int longIndex = index >> 6;      // Divide by 64
		int bitOffset = index & 63;      // Modulo 64

		if (value)
			longs[longIndex] |= 1UL << bitOffset;
		else
			longs[longIndex] &= ~(1UL << bitOffset);
	}

	/// <summary>
	/// if flag already set to value, returns false.  otherwise sets and returns true.
	/// </summary>
	public bool TrySetFlagIndex(int index, bool value)
	{
		AssertIndexInRange(index);
		var longs = GetULongsUnsafe();
		int longIndex = index >> 6;      // Divide by 64
		int bitOffset = index & 63;      // Modulo 64
		ulong flagMask = 1UL << bitOffset;
		bool currentValue = (longs[longIndex] & flagMask) != 0;

		if (currentValue == value)
		{
			return false; // Flag already has the desired value
		}

		if (value)
		{
			longs[longIndex] |= flagMask;
		}
		else
		{
			longs[longIndex] &= ~flagMask;
		}

		return true; // Flag was successfully set
	}

	/// <summary>
	/// Toggles the state of a specific flag by index.
	/// </summary>
	/// <param name="index">The index of the flag to toggle.</param>
	public void FlipFlagIndex(int index)
	{
		AssertIndexInRange(index);
		var longs = GetULongsUnsafe();
		int longIndex = index >> 6;      // Divide by 64
		int bitOffset = index & 63;      // Modulo 64
		longs[longIndex] ^= 1UL << bitOffset;
	}

	/// <summary>
	/// Clears all flags.
	/// </summary>
	public void Clear() => GetULongsUnsafe().Fill(0UL);

	/// <summary>
	/// Adds the flags from another BitFlags256 instance to this one.
	/// </summary>
	/// <param name="flags">The other BitFlags256 instance.</param>
	public void Add(BitFlags256 flags)
	{
		var myLongs = GetULongsUnsafe();
		var theirLongs = flags.RawBuffer.AsSpan()._AsULongs();
		myLongs[0] |= theirLongs[0];
		myLongs[1] |= theirLongs[1];
		myLongs[2] |= theirLongs[2];
		myLongs[3] |= theirLongs[3];
	}

	/// <summary>
	/// Removes the flags from another BitFlags256 instance from this one.
	/// </summary>
	/// <param name="flags">The other BitFlags256 instance.</param>
	public void Subtract(BitFlags256 flags)
	{
		var myLongs = GetULongsUnsafe();
		var theirLongs = flags.RawBuffer.AsSpan()._AsULongs();
		myLongs[0] &= ~theirLongs[0];
		myLongs[1] &= ~theirLongs[1];
		myLongs[2] &= ~theirLongs[2];
		myLongs[3] &= ~theirLongs[3];
	}

	/// <summary>
	/// Toggles the flags from another BitFlags256 instance in this one.
	/// </summary>
	/// <param name="flags">The other BitFlags256 instance.</param>
	public void Flip(BitFlags256 flags)
	{
		var myLongs = GetULongsUnsafe();
		var theirLongs = flags.RawBuffer.AsSpan()._AsULongs();
		myLongs[0] ^= theirLongs[0];
		myLongs[1] ^= theirLongs[1];
		myLongs[2] ^= theirLongs[2];
		myLongs[3] ^= theirLongs[3];
	}

	/// <summary>
	/// Determines if all flags in this instance are set.
	/// </summary>
	/// <returns>True if all 256 flags are set, false otherwise.</returns>
	public bool IsAllOn()
	{
		var longs = GetULongsUnsafe();
		return longs[0] == ulong.MaxValue
			&& longs[1] == ulong.MaxValue
			&& longs[2] == ulong.MaxValue
			&& longs[3] == ulong.MaxValue;
	}

	/// <summary>
	/// Determines if all specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if all specified flags are set, false otherwise.</returns>
	public bool IsAllOn(BitFlags256 flags)
	{
		var myLongs = GetULongsUnsafe();
		var theirLongs = flags.RawBuffer.AsSpan()._AsULongs();
		return (myLongs[0] & theirLongs[0]) == theirLongs[0]
			&& (myLongs[1] & theirLongs[1]) == theirLongs[1]
			&& (myLongs[2] & theirLongs[2]) == theirLongs[2]
			&& (myLongs[3] & theirLongs[3]) == theirLongs[3];
	}

	/// <summary>
	/// Determines if any flags in this instance are set.
	/// </summary>
	/// <returns>True if any flags are set, false if all flags are cleared.</returns>
	public bool IsAnyOn()
	{
		var longs = GetULongsUnsafe();
		return longs[0] != 0
			|| longs[1] != 0
			|| longs[2] != 0
			|| longs[3] != 0;
	}

	/// <summary>
	/// Determines if any of the specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if any specified flags are set, false otherwise.</returns>
	public bool IsAnyOn(BitFlags256 flags)
	{
		var myLongs = GetULongsUnsafe();
		var theirLongs = flags.RawBuffer.AsSpan()._AsULongs();
		return (myLongs[0] & theirLongs[0]) != 0
			|| (myLongs[1] & theirLongs[1]) != 0
			|| (myLongs[2] & theirLongs[2]) != 0
			|| (myLongs[3] & theirLongs[3]) != 0;
	}

	/// <summary>
	/// Checks for equality with another BitFlags256 instance.
	/// </summary>
	/// <param name="other">The other instance to compare.</param>
	/// <returns>True if both instances have the same flags, false otherwise.</returns>
	public bool Equals(BitFlags256 other)
	{
		var myLongs = GetULongsUnsafe();
		var theirLongs = other.RawBuffer.AsSpan()._AsULongs();
		return myLongs[0] == theirLongs[0]
			&& myLongs[1] == theirLongs[1]
			&& myLongs[2] == theirLongs[2]
			&& myLongs[3] == theirLongs[3];
	}

	/// <summary>
	/// Overrides the default Equals method.
	/// </summary>
	/// <param name="obj">The object to compare with.</param>
	/// <returns>True if the object is a BitFlags256 instance with the same flags, false otherwise.</returns>
	public override bool Equals(object obj) => obj is BitFlags256 other && Equals(other);

	/// <summary>
	/// Returns the hash code for this instance.
	/// </summary>
	/// <returns>A 32-bit signed integer hash code.</returns>
	public override int GetHashCode()
	{
		var longs = GetULongsUnsafe();
		return HashCode.Combine(longs[0], longs[1], longs[2], longs[3]);
	}

	/// <summary>
	/// Determines if two BitFlags256 instances are equal.
	/// </summary>
	public static bool operator ==(BitFlags256 left, BitFlags256 right) => left.Equals(right);

	/// <summary>
	/// Determines if two BitFlags256 instances are not equal.
	/// </summary>
	public static bool operator !=(BitFlags256 left, BitFlags256 right) => !left.Equals(right);
}
