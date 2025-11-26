// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]
using System;
using System.Runtime.CompilerServices;

/// <summary>
/// Represents a set of bit flags as a 64-bit unsigned integer.
/// can use + - operators to add and remove flags
/// </summary>
public struct BitFlags64
{
	/// <summary>
	/// The maximum number of flags supported, which is 64.
	/// </summary>
	public const int MaxFlags = 64;

	/// <summary>
	/// A BitFlags64 instance where all flags are set.
	/// </summary>
	public static readonly BitFlags64 All = new BitFlags64(ulong.MaxValue);

	/// <summary>
	/// A BitFlags64 instance where no flags are set.
	/// </summary>
	public static readonly BitFlags64 None = default;

	/// <summary>
	/// The raw 64-bit unsigned integer value of the flags.
	/// </summary>
	public ulong RawBuffer;

	public BitFlags64()
	{
	}

	/// <summary>
	/// Initializes a new instance of the BitFlags64 struct with a specific raw buffer.
	/// </summary>
	/// <param name="rawBuffer">The initial value of the flags.</param>
	public BitFlags64(ulong rawBuffer)
	{
		RawBuffer = rawBuffer;
	}

	/// <summary>
	/// Initializes a new instance of the BitFlags64 struct by copying another instance.
	/// </summary>
	/// <param name="other">The instance to copy.</param>
	public BitFlags64(BitFlags64 other)
	{
		RawBuffer = other.RawBuffer;
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
	/// Gets the state of a specific flag.
	/// </summary>
	/// <param name="flag">The flag to check.</param>
	/// <returns>True if the flag is set, false otherwise.</returns>
	public bool GetFlag(ulong flag)
	{
		return (RawBuffer & flag) != 0;
	}

	/// <summary>
	/// Gets the state of a specific flag by index.
	/// </summary>
	/// <param name="index">The index of the flag.</param>
	/// <returns>True if the flag is set, false otherwise.</returns>
	public bool GetFlagIndex(int index)
	{
		AssertIndexInRange(index);
		return (RawBuffer & (1UL << index)) != 0;
	}

	/// <summary>
	/// Sets or clears a specific flag.
	/// </summary>
	/// <param name="flag">The flag to set or clear.</param>
	/// <param name="value">True to set the flag, false to clear it.</param>
	public void SetFlag(ulong flag, bool value)
	{
		if (value)
			RawBuffer |= flag;
		else
			RawBuffer &= ~flag;
	}

	/// <summary>
	/// Sets or clears a specific flag by index.
	/// </summary>
	/// <param name="index">The index of the flag.</param>
	/// <param name="value">True to set the flag, false to clear it.</param>
	public void SetFlagIndex(int index, bool value)
	{
		AssertIndexInRange(index);
		if (value)
			RawBuffer |= 1UL << index;
		else
			RawBuffer &= ~(1UL << index);
	}

	/// <summary>
	/// if flag already set to value, returns false.  otherwise sets and returns true.
	/// </summary>
	public bool TrySetFlagIndex(int index, bool value)
	{
		AssertIndexInRange(index);
		ulong flagMask = 1UL << index;
		bool currentValue = (RawBuffer & flagMask) != 0;

		if (currentValue == value)
		{
			return false; // Flag already has the desired value
		}

		if (value)
		{
			RawBuffer |= flagMask;
		}
		else
		{
			RawBuffer &= ~flagMask;
		}

		return true; // Flag was successfully set
	}

	/// <summary>
	/// if flag already set to value, returns false.  otherwise sets and returns true.
	/// </summary>
	public bool TrySetFlag(ulong flagValue, bool value)
	{
		bool currentValue = (RawBuffer & flagValue) != 0;

		if (currentValue == value)
		{
			return false; // Flag already has the desired value
		}

		if (value)
		{
			RawBuffer |= flagValue;
		}
		else
		{
			RawBuffer &= ~flagValue;
		}

		return true; // Flag was successfully set
	}


	/// <summary>
	/// Toggles the state of a specific flag.
	/// </summary>
	/// <param name="flag">The flag to toggle.</param>
	public void FlipFlag(ulong flag)
	{
		RawBuffer ^= flag;
	}

	/// <summary>
	/// Toggles the state of a specific flag by index.
	/// </summary>
	/// <param name="index">The index of the flag to toggle.</param>
	public void FlipFlagIndex(int index)
	{
		AssertIndexInRange(index);
		RawBuffer ^= 1UL << index;
	}

	/// <summary>
	/// Clears all flags.
	/// </summary>
	public void Clear() => RawBuffer = 0;

	/// <summary>
	/// Adds the specified flags to this instance.
	/// </summary>
	/// <param name="flags">The flags to add.</param>
	public void Add(ulong flags) => RawBuffer |= flags;

	/// <summary>
	/// Adds the flags from another BitFlags64 instance to this one.
	/// </summary>
	/// <param name="flags">The other BitFlags64 instance.</param>
	public void Add(BitFlags64 flags) => RawBuffer |= flags.RawBuffer;

	/// <summary>
	/// Removes the specified flags from this instance.
	/// </summary>
	/// <param name="flags">The flags to remove.</param>
	public void Subtract(ulong flags) => RawBuffer &= ~flags;

	/// <summary>
	/// Removes the flags from another BitFlags64 instance from this one.
	/// </summary>
	/// <param name="flags">The other BitFlags64 instance.</param>
	public void Subtract(BitFlags64 flags) => RawBuffer &= ~flags.RawBuffer;

	/// <summary>
	/// Toggles the specified flags in this instance.
	/// </summary>
	/// <param name="flags">The flags to toggle.</param>
	public void Flip(ulong flags) => RawBuffer ^= flags;

	/// <summary>
	/// Toggles the flags from another BitFlags64 instance in this one.
	/// </summary>
	/// <param name="flags">The other BitFlags64 instance.</param>
	public void Flip(BitFlags64 flags) => RawBuffer ^= flags.RawBuffer;

	/// <summary>
	/// Determines if all flags in this instance are set.
	/// </summary>
	/// <returns>True if all 64 flags are set, false otherwise.</returns>
	public bool IsAllOn() => RawBuffer == ulong.MaxValue;

	/// <summary>
	/// Determines if all specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if all specified flags are set, false otherwise.</returns>
	public bool IsAllOn(ulong flags) => (RawBuffer & flags) == flags;

	/// <summary>
	/// Determines if all specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if all specified flags are set, false otherwise.</returns>
	public bool IsAllOn(BitFlags64 flags) => (RawBuffer & flags.RawBuffer) == flags.RawBuffer;

	/// <summary>
	/// Determines if any flags in this instance are set.
	/// </summary>
	/// <returns>True if any flags are set, false if all flags are cleared.</returns>
	public bool IsAnyOn() => RawBuffer != 0;

	/// <summary>
	/// Determines if any of the specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if any specified flags are set, false otherwise.</returns>
	public bool IsAnyOn(ulong flags) => (RawBuffer & flags) != 0;

	/// <summary>
	/// Determines if any of the specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if any specified flags are set, false otherwise.</returns>
	public bool IsAnyOn(BitFlags64 flags) => (RawBuffer & flags.RawBuffer) != 0;

	/// <summary>
	/// Checks for equality with the specified flags.
	/// </summary>
	/// <param name="other">The flags to compare with.</param>
	/// <returns>True if this instance has exactly the same flags set, false otherwise.</returns>
	public bool Equals(ulong other) => RawBuffer == other;

	/// <summary>
	/// Checks for equality with another BitFlags64 instance.
	/// </summary>
	/// <param name="other">The other instance to compare.</param>
	/// <returns>True if both instances have the same flags, false otherwise.</returns>
	public bool Equals(BitFlags64 other) => RawBuffer == other.RawBuffer;

	/// <summary>
	/// Overrides the default Equals method.
	/// </summary>
	/// <param name="obj">The object to compare with.</param>
	/// <returns>True if the object is a BitFlags64 instance with the same flags, false otherwise.</returns>
	public override bool Equals(object obj) => obj is BitFlags64 other && Equals(other);

	/// <summary>
	/// Returns the hash code for this instance.
	/// </summary>
	/// <returns>A 32-bit signed integer hash code.</returns>
	public override int GetHashCode() => RawBuffer.GetHashCode();

	/// <summary>
	/// Determines if two BitFlags64 instances are equal.
	/// </summary>
	public static bool operator ==(BitFlags64 left, BitFlags64 right) => left.RawBuffer == right.RawBuffer;

	/// <summary>
	/// Determines if two BitFlags64 instances are not equal.
	/// </summary>
	public static bool operator !=(BitFlags64 left, BitFlags64 right) => left.RawBuffer != right.RawBuffer;

	/// <summary>
	/// Implicitly converts a BitFlags64 instance to a ulong.
	/// </summary>
	/// <param name="flags">The BitFlags64 instance to convert.</param>
	/// <returns>The raw 64-bit unsigned integer representation of the flags.</returns>
	public static implicit operator ulong(BitFlags64 flags) => flags.RawBuffer;

	/// <summary>
	/// Implicitly converts a ulong to a BitFlags64 instance.
	/// </summary>
	/// <param name="value">The ulong value to convert.</param>
	/// <returns>A new BitFlags64 instance representing the ulong value.</returns>
	public static implicit operator BitFlags64(ulong value) => new(value);

	/// <summary>
	/// Implicitly converts a BitFlags64 instance to a long.
	/// </summary>
	/// <param name="flags">The BitFlags64 instance to convert.</param>
	/// <returns>The long representation of the flags.</returns>
	public static implicit operator long(BitFlags64 flags) => (long)flags.RawBuffer;

	/// <summary>
	/// Implicitly converts a long to a BitFlags64 instance.
	/// </summary>
	/// <param name="value">The long value to convert.</param>
	/// <returns>A new BitFlags64 instance representing the long value.</returns>
	public static implicit operator BitFlags64(long value) => new((ulong)value);
}


/// <summary>
/// Represents a set of bit flags as a 64-bit unsigned integer, using a generic enum type for type-safe flag operations.
/// </summary>
/// <typeparam name="TFlagsEnum">The enum type representing the flags. Must be marked with [Flags] attribute and inherit from long.</typeparam>
public struct BitFlags64<TFlagsEnum> where TFlagsEnum : struct, Enum
{
	/// <summary>
	/// The maximum number of flags supported, which is 64.
	/// </summary>
	public const int MaxFlags = 64;

	/// <summary>
	/// A BitFlags64 instance where all flags are set.
	/// </summary>
	public static readonly BitFlags64<TFlagsEnum> All = new BitFlags64<TFlagsEnum>(ulong.MaxValue);

	/// <summary>
	/// A BitFlags64 instance where no flags are set.
	/// </summary>
	public static readonly BitFlags64<TFlagsEnum> None = default;

	/// <summary>
	/// The raw 64-bit unsigned integer value of the flags.
	/// </summary>
	public ulong RawBuffer;

	public BitFlags64()
	{
	}


	/// <summary>
	/// Initializes a new instance of the BitFlags64 struct with a specific raw buffer.
	/// </summary>
	/// <param name="rawBuffer">The initial value of the flags.</param>
	public BitFlags64(ulong rawBuffer)
	{
		RawBuffer = rawBuffer;
	}

	/// <summary>
	/// Initializes a new instance of the BitFlags64 struct by copying another instance.
	/// </summary>
	/// <param name="other">The instance to copy.</param>
	public BitFlags64(BitFlags64<TFlagsEnum> other)
	{
		RawBuffer = other.RawBuffer;
	}

	public BitFlags64(BitFlags64 other)
	{
		RawBuffer = other.RawBuffer;
	}

	/// <summary>
	/// Converts an enum value to its underlying ulong representation without boxing.
	/// </summary>
	/// <param name="enumValue">The enum value to convert.</param>
	/// <returns>The ulong representation of the enum value.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static ref ulong EnumToUInt64(ref TFlagsEnum enumValue)
	{
		return ref Unsafe.As<TFlagsEnum, ulong>(ref enumValue);
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
	/// Gets the state of a specific flag.
	/// </summary>
	/// <param name="flag">The flag to check.</param>
	/// <returns>True if the flag is set, false otherwise.</returns>
	public bool GetFlag(TFlagsEnum flag)
	{
		return (RawBuffer & EnumToUInt64(ref flag)) != 0;
	}

	/// <summary>
	/// Gets the state of a specific flag by index.
	/// </summary>
	/// <param name="index">The index of the flag.</param>
	/// <returns>True if the flag is set, false otherwise.</returns>
	public bool GetFlagIndex(int index)
	{
		AssertIndexInRange(index);
		return (RawBuffer & (1UL << index)) != 0;
	}

	/// <summary>
	/// Sets or clears a specific flag.
	/// </summary>
	/// <param name="flag">The flag to set or clear.</param>
	/// <param name="value">True to set the flag, false to clear it.</param>
	public void SetFlag(TFlagsEnum flag, bool value)
	{
		ulong flagValue = EnumToUInt64(ref flag);
		if (value)
			RawBuffer |= flagValue;
		else
			RawBuffer &= ~flagValue;
	}

	/// <summary>
	/// Sets or clears a specific flag by index.
	/// </summary>
	/// <param name="index">The index of the flag.</param>
	/// <param name="value">True to set the flag, false to clear it.</param>
	public void SetFlagIndex(int index, bool value)
	{
		AssertIndexInRange(index);
		if (value)
			RawBuffer |= 1UL << index;
		else
			RawBuffer &= ~(1UL << index);
	}
	/// <summary>
	/// if flag already set to value, returns false.  otherwise sets and returns true.
	/// </summary>
	public bool TrySetFlagIndex(int index, bool value)
	{
		AssertIndexInRange(index);
		ulong flagMask = 1UL << index;
		bool currentValue = (RawBuffer & flagMask) != 0;

		if (currentValue == value)
		{
			return false; // Flag already has the desired value
		}

		if (value)
		{
			RawBuffer |= flagMask;
		}
		else
		{
			RawBuffer &= ~flagMask;
		}

		return true; // Flag was successfully set
	}

	/// <summary>
	/// if flag already set to value, returns false.  otherwise sets and returns true.
	/// </summary>
	public bool TrySetFlag(TFlagsEnum flag, bool value)
	{
		ulong flagValue = EnumToUInt64(ref flag);
		bool currentValue = (RawBuffer & flagValue) != 0;

		if (currentValue == value)
		{
			return false; // Flag already has the desired value
		}

		if (value)
		{
			RawBuffer |= flagValue;
		}
		else
		{
			RawBuffer &= ~flagValue;
		}

		return true; // Flag was successfully set
	}


	/// <summary>
	/// Toggles the state of a specific flag.
	/// </summary>
	/// <param name="flag">The flag to toggle.</param>
	public void FlipFlag(TFlagsEnum flag)
	{
		RawBuffer ^= EnumToUInt64(ref flag);
	}



	/// <summary>
	/// Toggles the state of a specific flag by index.
	/// </summary>
	/// <param name="index">The index of the flag to toggle.</param>
	public void FlipFlagIndex(int index)
	{
		AssertIndexInRange(index);
		RawBuffer ^= 1UL << index;
	}

	/// <summary>
	/// Clears all flags.
	/// </summary>
	public void Clear() => RawBuffer = 0;

	/// <summary>
	/// Adds the specified flags to this instance.
	/// </summary>
	/// <param name="flags">The flags to add.</param>
	public void Add(TFlagsEnum flags) => RawBuffer |= EnumToUInt64(ref flags);

	/// <summary>
	/// Adds the flags from another BitFlags64 instance to this one.
	/// </summary>
	/// <param name="flags">The other BitFlags64 instance.</param>
	public void Add(BitFlags64<TFlagsEnum> flags) => RawBuffer |= flags.RawBuffer;

	/// <summary>
	/// Removes the specified flags from this instance.
	/// </summary>
	/// <param name="flags">The flags to remove.</param>
	public void Subtract(TFlagsEnum flags) => RawBuffer &= ~EnumToUInt64(ref flags);

	/// <summary>
	/// Removes the flags from another BitFlags64 instance from this one.
	/// </summary>
	/// <param name="flags">The other BitFlags64 instance.</param>
	public void Subtract(BitFlags64<TFlagsEnum> flags) => RawBuffer &= ~flags.RawBuffer;

	/// <summary>
	/// Toggles the specified flags in this instance.
	/// </summary>
	/// <param name="flags">The flags to toggle.</param>
	public void Flip(TFlagsEnum flags) => RawBuffer ^= EnumToUInt64(ref flags);

	/// <summary>
	/// Toggles the flags from another BitFlags64 instance in this one.
	/// </summary>
	/// <param name="flags">The other BitFlags64 instance.</param>
	public void Flip(BitFlags64<TFlagsEnum> flags) => RawBuffer ^= flags.RawBuffer;

	/// <summary>
	/// Determines if all flags in this instance are set.
	/// </summary>
	/// <returns>True if all 64 flags are set, false otherwise.</returns>
	public bool IsAllOn() => RawBuffer == ulong.MaxValue;

	/// <summary>
	/// Determines if all specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if all specified flags are set, false otherwise.</returns>
	public bool IsAllOn(TFlagsEnum flags) => (RawBuffer & EnumToUInt64(ref flags)) == EnumToUInt64(ref flags);

	/// <summary>
	/// Determines if all specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if all specified flags are set, false otherwise.</returns>
	public bool IsAllOn(BitFlags64<TFlagsEnum> flags) => (RawBuffer & flags.RawBuffer) == flags.RawBuffer;

	/// <summary>
	/// Determines if any flags in this instance are set.
	/// </summary>
	/// <returns>True if any flags are set, false if all flags are cleared.</returns>
	public bool IsAnyOn() => RawBuffer != 0;

	/// <summary>
	/// Determines if any of the specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if any specified flags are set, false otherwise.</returns>
	public bool IsAnyOn(TFlagsEnum flags) => (RawBuffer & EnumToUInt64(ref flags)) != 0;

	/// <summary>
	/// Determines if any of the specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if any specified flags are set, false otherwise.</returns>
	public bool IsAnyOn(BitFlags64<TFlagsEnum> flags) => (RawBuffer & flags.RawBuffer) != 0;

	/// <summary>
	/// Checks for equality with the specified flags.
	/// </summary>
	/// <param name="other">The flags to compare with.</param>
	/// <returns>True if this instance has exactly the same flags set, false otherwise.</returns>
	public bool Equals(TFlagsEnum other) => RawBuffer == EnumToUInt64(ref other);

	/// <summary>
	/// Checks for equality with another BitFlags64 instance.
	/// </summary>
	/// <param name="other">The other instance to compare.</param>
	/// <returns>True if both instances have the same flags, false otherwise.</returns>
	public bool Equals(BitFlags64<TFlagsEnum> other) => RawBuffer == other.RawBuffer;

	/// <summary>
	/// Overrides the default Equals method.
	/// </summary>
	/// <param name="obj">The object to compare with.</param>
	/// <returns>True if the object is a BitFlags64 instance with the same flags, false otherwise.</returns>
	public override bool Equals(object obj) => obj is BitFlags64<TFlagsEnum> other && Equals(other);

	/// <summary>
	/// Returns the hash code for this instance.
	/// </summary>
	/// <returns>A 32-bit signed integer hash code.</returns>
	public override int GetHashCode() => RawBuffer.GetHashCode();

	/// <summary>
	/// Determines if two BitFlags64 instances are equal.
	/// </summary>
	public static bool operator ==(BitFlags64<TFlagsEnum> left, BitFlags64<TFlagsEnum> right) => left.RawBuffer == right.RawBuffer;

	/// <summary>
	/// Determines if two BitFlags64 instances are not equal.
	/// </summary>
	public static bool operator !=(BitFlags64<TFlagsEnum> left, BitFlags64<TFlagsEnum> right) => left.RawBuffer != right.RawBuffer;

	/// <summary>
	/// Explicitly converts a BitFlags64 instance to a ulong.
	/// </summary>
	/// <param name="flags">The BitFlags64 instance to convert.</param>
	/// <returns>The raw 64-bit unsigned integer representation of the flags.</returns>
	public static implicit operator ulong(BitFlags64<TFlagsEnum> flags) => flags.RawBuffer;

	/// <summary>
	/// Explicitly converts a ulong to a BitFlags64 instance.
	/// </summary>
	/// <param name="value">The ulong value to convert.</param>
	/// <returns>A new BitFlags64 instance representing the ulong value.</returns>
	public static implicit operator BitFlags64<TFlagsEnum>(ulong value) => new(value);

	public static implicit operator BitFlags64(BitFlags64<TFlagsEnum> flags) => new(flags.RawBuffer);

	public static implicit operator BitFlags64<TFlagsEnum>(BitFlags64 flags) => new(flags.RawBuffer);



	/// <summary>
	/// Implicitly converts a BitFlags64<T> instance to the enum type T.
	/// </summary>
	/// <param name="flags">The BitFlags64<T> instance to convert.</param>
	/// <returns>The enum representation of the flags.</returns>
	public static implicit operator TFlagsEnum(BitFlags64<TFlagsEnum> flags)
	{
		return Unsafe.As<ulong, TFlagsEnum>(ref flags.RawBuffer);
	}

	/// <summary>
	/// Implicitly converts an enum of type T to a BitFlags64<T> instance.
	/// </summary>
	/// <param name="enumValue">The enum value to convert.</param>
	/// <returns>A new BitFlags64<T> instance representing the enum value.</returns>
	public static implicit operator BitFlags64<TFlagsEnum>(TFlagsEnum enumValue)
	{
		return new BitFlags64<TFlagsEnum>(EnumToUInt64(ref enumValue));
	}

	/// <summary>
	/// Implicitly converts a BitFlags64<T> instance to a long.
	/// </summary>
	/// <param name="flags">The BitFlags64<T> instance to convert.</param>
	/// <returns>The long representation of the flags.</returns>
	public static implicit operator long(BitFlags64<TFlagsEnum> flags)
	{
		return (long)flags.RawBuffer;
	}

	/// <summary>
	/// Implicitly converts a long to a BitFlags64<T> instance.
	/// </summary>
	/// <param name="value">The long value to convert.</param>
	/// <returns>A new BitFlags64<T> instance representing the long value.</returns>
	public static implicit operator BitFlags64<TFlagsEnum>(long value)
	{
		return new BitFlags64<TFlagsEnum>((ulong)value);
	}
}