// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]
using System;
using System.Runtime.CompilerServices;


/// <summary>
/// Represents a set of bit flags as a 32-bit unsigned integer.
/// can use + - operataors to add and remove flags
/// </summary>
public struct BitFlags
{
	/// <summary>
	/// The maximum number of flags supported, which is 32.
	/// </summary>
	public const int MaxFlags = 32;

	/// <summary>
	/// A BitFlags instance where all flags are set.
	/// </summary>
	public static readonly BitFlags All = new BitFlags(uint.MaxValue);

	/// <summary>
	/// A BitFlags instance where no flags are set.
	/// </summary>
	public static readonly BitFlags None = default;

	/// <summary>
	/// The raw 32-bit unsigned integer value of the flags.
	/// </summary>
	public uint RawBuffer;

	public BitFlags()
	{
	}

	/// <summary>
	/// Initializes a new instance of the BitFlags struct with a specific raw buffer.
	/// </summary>
	/// <param name="rawBuffer">The initial value of the flags.</param>
	public BitFlags(uint rawBuffer)
	{
		RawBuffer = rawBuffer;
	}

	/// <summary>
	/// Initializes a new instance of the BitFlags struct by copying another instance.
	/// </summary>
	/// <param name="other">The instance to copy.</param>
	public BitFlags(BitFlags other)
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
	public bool GetFlag(uint flag)
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
		return (RawBuffer & (1u << index)) != 0;
	}

	/// <summary>
	/// Sets or clears a specific flag.
	/// </summary>
	/// <param name="flag">The flag to set or clear.</param>
	/// <param name="value">True to set the flag, false to clear it.</param>
	public void SetFlag(uint flag, bool value)
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
			RawBuffer |= 1u << index;
		else
			RawBuffer &= ~(1u << index);
	}

	/// <summary>
	/// if flag already set to value, returns false.  otherwise sets and returns true.
	/// </summary>
	public bool TrySetFlagIndex(int index, bool value)
	{
		AssertIndexInRange(index);
		uint flagMask = 1u << index;
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
	public bool TrySetFlag(uint flagValue, bool value)
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
	public void FlipFlag(uint flag)
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
		RawBuffer ^= 1u << index;
	}

	/// <summary>
	/// Clears all flags.
	/// </summary>
	public void Clear() => RawBuffer = 0;

	/// <summary>
	/// Adds the specified flags to this instance.
	/// </summary>
	/// <param name="flags">The flags to add.</param>
	public void Add(uint flags) => RawBuffer |= flags;

	/// <summary>
	/// Adds the flags from another BitFlags instance to this one.
	/// </summary>
	/// <param name="flags">The other BitFlags instance.</param>
	public void Add(BitFlags flags) => RawBuffer |= flags.RawBuffer;

	/// <summary>
	/// Removes the specified flags from this instance.
	/// </summary>
	/// <param name="flags">The flags to remove.</param>
	public void Subtract(uint flags) => RawBuffer &= ~flags;

	/// <summary>
	/// Removes the flags from another BitFlags instance from this one.
	/// </summary>
	/// <param name="flags">The other BitFlags instance.</param>
	public void Subtract(BitFlags flags) => RawBuffer &= ~flags.RawBuffer;

	/// <summary>
	/// Toggles the specified flags in this instance.
	/// </summary>
	/// <param name="flags">The flags to toggle.</param>
	public void Flip(uint flags) => RawBuffer ^= flags;

	/// <summary>
	/// Toggles the flags from another BitFlags instance in this one.
	/// </summary>
	/// <param name="flags">The other BitFlags instance.</param>
	public void Flip(BitFlags flags) => RawBuffer ^= flags.RawBuffer;

	/// <summary>
	/// Determines if all specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if all specified flags are set, false otherwise.</returns>
	public bool IsAllOn(uint flags) => (RawBuffer & flags) == flags;

	/// <summary>
	/// Determines if all specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if all specified flags are set, false otherwise.</returns>
	public bool IsAllOn(BitFlags flags) => (RawBuffer & flags.RawBuffer) == flags.RawBuffer;

	/// <summary>
	/// Determines if any of the specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if any specified flags are set, false otherwise.</returns>
	public bool IsAnyOn(uint flags) => (RawBuffer & flags) != 0;

	/// <summary>
	/// Determines if any of the specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if any specified flags are set, false otherwise.</returns>
	public bool IsAnyOn(BitFlags flags) => (RawBuffer & flags.RawBuffer) != 0;

	/// <summary>
	/// Checks for equality with the specified flags.
	/// </summary>
	/// <param name="other">The flags to compare with.</param>
	/// <returns>True if this instance has exactly the same flags set, false otherwise.</returns>
	public bool Equals(uint other) => RawBuffer == other;

	/// <summary>
	/// Checks for equality with another BitFlags instance.
	/// </summary>
	/// <param name="other">The other instance to compare.</param>
	/// <returns>True if both instances have the same flags, false otherwise.</returns>
	public bool Equals(BitFlags other) => RawBuffer == other.RawBuffer;

	/// <summary>
	/// Overrides the default Equals method.
	/// </summary>
	/// <param name="obj">The object to compare with.</param>
	/// <returns>True if the object is a BitFlags instance with the same flags, false otherwise.</returns>
	public override bool Equals(object obj) => obj is BitFlags other && Equals(other);

	/// <summary>
	/// Returns the hash code for this instance.
	/// </summary>
	/// <returns>A 32-bit signed integer hash code.</returns>
	public override int GetHashCode() => (int)RawBuffer;

	/// <summary>
	/// Determines if two BitFlags instances are equal.
	/// </summary>
	public static bool operator ==(BitFlags left, BitFlags right) => left.RawBuffer == right.RawBuffer;

	/// <summary>
	/// Determines if two BitFlags instances are not equal.
	/// </summary>
	public static bool operator !=(BitFlags left, BitFlags right) => left.RawBuffer != right.RawBuffer;

	/// <summary>
	/// Implicitly converts a BitFlags instance to a uint.
	/// </summary>
	/// <param name="flags">The BitFlags instance to convert.</param>
	/// <returns>The raw 32-bit unsigned integer representation of the flags.</returns>
	public static implicit operator uint(BitFlags flags) => flags.RawBuffer;

	/// <summary>
	/// Implicitly converts a uint to a BitFlags instance.
	/// </summary>
	/// <param name="value">The uint value to convert.</param>
	/// <returns>A new BitFlags instance representing the uint value.</returns>
	public static implicit operator BitFlags(uint value) => new(value);

	/// <summary>
	/// Implicitly converts a BitFlags instance to an int.
	/// </summary>
	/// <param name="flags">The BitFlags instance to convert.</param>
	/// <returns>The int representation of the flags.</returns>
	public static implicit operator int(BitFlags flags) => (int)flags.RawBuffer;

	/// <summary>
	/// Implicitly converts an int to a BitFlags instance.
	/// </summary>
	/// <param name="value">The int value to convert.</param>
	/// <returns>A new BitFlags instance representing the int value.</returns>
	public static implicit operator BitFlags(int value) => new((uint)value);
}


/// <summary>
/// Represents a set of bit flags as a 32-bit unsigned integer, using a generic enum type for type-safe flag operations.
/// </summary>
/// <typeparam name="TFlagsEnum">The enum type representing the flags. Must be marked with [Flags] attribute and inherit from int.</typeparam>
public struct BitFlags<TFlagsEnum> where TFlagsEnum : struct, Enum
{
	/// <summary>
	/// The maximum number of flags supported, which is 32.
	/// </summary>
	public const int MaxFlags = 32;

	/// <summary>
	/// A BitFlags instance where all flags are set.
	/// </summary>
	public static readonly BitFlags<TFlagsEnum> All = new BitFlags<TFlagsEnum>(uint.MaxValue);

	/// <summary>
	/// A BitFlags instance where no flags are set.
	/// </summary>
	public static readonly BitFlags<TFlagsEnum> None = default;

	/// <summary>
	/// The raw 32-bit unsigned integer value of the flags.
	/// </summary>
	public uint RawBuffer;

	public BitFlags()
	{
	}


	/// <summary>
	/// Initializes a new instance of the BitFlags struct with a specific raw buffer.
	/// </summary>
	/// <param name="rawBuffer">The initial value of the flags.</param>
	public BitFlags(uint rawBuffer)
	{
		RawBuffer = rawBuffer;
	}

	/// <summary>
	/// Initializes a new instance of the BitFlags struct by copying another instance.
	/// </summary>
	/// <param name="other">The instance to copy.</param>
	public BitFlags(BitFlags<TFlagsEnum> other)
	{
		RawBuffer = other.RawBuffer;
	}

	public BitFlags(BitFlags other)
	{
		RawBuffer = other.RawBuffer;
	}

	/// <summary>
	/// Converts an enum value to its underlying uint representation without boxing.
	/// </summary>
	/// <param name="enumValue">The enum value to convert.</param>
	/// <returns>The uint representation of the enum value.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static ref uint EnumToUInt32(ref TFlagsEnum enumValue)
	{
		return ref Unsafe.As<TFlagsEnum, uint>(ref enumValue);
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
		return (RawBuffer & EnumToUInt32(ref flag)) != 0;
	}

	/// <summary>
	/// Gets the state of a specific flag by index.
	/// </summary>
	/// <param name="index">The index of the flag.</param>
	/// <returns>True if the flag is set, false otherwise.</returns>
	public bool GetFlagIndex(int index)
	{
		AssertIndexInRange(index);
		return (RawBuffer & (1u << index)) != 0;
	}

	/// <summary>
	/// Sets or clears a specific flag.
	/// </summary>
	/// <param name="flag">The flag to set or clear.</param>
	/// <param name="value">True to set the flag, false to clear it.</param>
	public void SetFlag(TFlagsEnum flag, bool value)
	{
		uint flagValue = EnumToUInt32(ref flag);
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
			RawBuffer |= 1u << index;
		else
			RawBuffer &= ~(1u << index);
	}
	/// <summary>
	/// if flag already set to value, returns false.  otherwise sets and returns true.
	/// </summary>
	public bool TrySetFlagIndex(int index, bool value)
	{
		AssertIndexInRange(index);
		uint flagMask = 1u << index;
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
		uint flagValue = EnumToUInt32(ref flag);
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
		RawBuffer ^= EnumToUInt32(ref flag);
	}



	/// <summary>
	/// Toggles the state of a specific flag by index.
	/// </summary>
	/// <param name="index">The index of the flag to toggle.</param>
	public void FlipFlagIndex(int index)
	{
		AssertIndexInRange(index);
		RawBuffer ^= 1u << index;
	}

	/// <summary>
	/// Clears all flags.
	/// </summary>
	public void Clear() => RawBuffer = 0;

	/// <summary>
	/// Adds the specified flags to this instance.
	/// </summary>
	/// <param name="flags">The flags to add.</param>
	public void Add(TFlagsEnum flags) => RawBuffer |= EnumToUInt32(ref flags);

	/// <summary>
	/// Adds the flags from another BitFlags instance to this one.
	/// </summary>
	/// <param name="flags">The other BitFlags instance.</param>
	public void Add(BitFlags<TFlagsEnum> flags) => RawBuffer |= flags.RawBuffer;

	/// <summary>
	/// Removes the specified flags from this instance.
	/// </summary>
	/// <param name="flags">The flags to remove.</param>
	public void Subtract(TFlagsEnum flags) => RawBuffer &= ~EnumToUInt32(ref flags);

	/// <summary>
	/// Removes the flags from another BitFlags instance from this one.
	/// </summary>
	/// <param name="flags">The other BitFlags instance.</param>
	public void Subtract(BitFlags<TFlagsEnum> flags) => RawBuffer &= ~flags.RawBuffer;

	/// <summary>
	/// Toggles the specified flags in this instance.
	/// </summary>
	/// <param name="flags">The flags to toggle.</param>
	public void Flip(TFlagsEnum flags) => RawBuffer ^= EnumToUInt32(ref flags);

	/// <summary>
	/// Toggles the flags from another BitFlags instance in this one.
	/// </summary>
	/// <param name="flags">The other BitFlags instance.</param>
	public void Flip(BitFlags<TFlagsEnum> flags) => RawBuffer ^= flags.RawBuffer;

	/// <summary>
	/// Determines if all specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if all specified flags are set, false otherwise.</returns>
	public bool IsAllOn(TFlagsEnum flags) => (RawBuffer & EnumToUInt32(ref flags)) == EnumToUInt32(ref flags);

	/// <summary>
	/// Determines if all specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if all specified flags are set, false otherwise.</returns>
	public bool IsAllOn(BitFlags<TFlagsEnum> flags) => (RawBuffer & flags.RawBuffer) == flags.RawBuffer;

	/// <summary>
	/// Determines if any of the specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if any specified flags are set, false otherwise.</returns>
	public bool IsAnyOn(TFlagsEnum flags) => (RawBuffer & EnumToUInt32(ref flags)) != 0;

	/// <summary>
	/// Determines if any of the specified flags are set.
	/// </summary>
	/// <param name="flags">The flags to check.</param>
	/// <returns>True if any specified flags are set, false otherwise.</returns>
	public bool IsAnyOn(BitFlags<TFlagsEnum> flags) => (RawBuffer & flags.RawBuffer) != 0;

	/// <summary>
	/// Checks for equality with the specified flags.
	/// </summary>
	/// <param name="other">The flags to compare with.</param>
	/// <returns>True if this instance has exactly the same flags set, false otherwise.</returns>
	public bool Equals(TFlagsEnum other) => RawBuffer == EnumToUInt32(ref other);

	/// <summary>
	/// Checks for equality with another BitFlags instance.
	/// </summary>
	/// <param name="other">The other instance to compare.</param>
	/// <returns>True if both instances have the same flags, false otherwise.</returns>
	public bool Equals(BitFlags<TFlagsEnum> other) => RawBuffer == other.RawBuffer;

	/// <summary>
	/// Overrides the default Equals method.
	/// </summary>
	/// <param name="obj">The object to compare with.</param>
	/// <returns>True if the object is a BitFlags instance with the same flags, false otherwise.</returns>
	public override bool Equals(object obj) => obj is BitFlags<TFlagsEnum> other && Equals(other);

	/// <summary>
	/// Returns the hash code for this instance.
	/// </summary>
	/// <returns>A 32-bit signed integer hash code.</returns>
	public override int GetHashCode() => (int)RawBuffer;

	/// <summary>
	/// Determines if two BitFlags instances are equal.
	/// </summary>
	public static bool operator ==(BitFlags<TFlagsEnum> left, BitFlags<TFlagsEnum> right) => left.RawBuffer == right.RawBuffer;

	/// <summary>
	/// Determines if two BitFlags instances are not equal.
	/// </summary>
	public static bool operator !=(BitFlags<TFlagsEnum> left, BitFlags<TFlagsEnum> right) => left.RawBuffer != right.RawBuffer;

	/// <summary>
	/// Explicitly converts a BitFlags instance to a uint.
	/// </summary>
	/// <param name="flags">The BitFlags instance to convert.</param>
	/// <returns>The raw 32-bit unsigned integer representation of the flags.</returns>
	public static implicit operator uint(BitFlags<TFlagsEnum> flags) => flags.RawBuffer;

	/// <summary>
	/// Explicitly converts a uint to a BitFlags instance.
	/// </summary>
	/// <param name="value">The uint value to convert.</param>
	/// <returns>A new BitFlags instance representing the uint value.</returns>
	public static implicit operator BitFlags<TFlagsEnum>(uint value) => new(value);

	public static implicit operator BitFlags(BitFlags<TFlagsEnum> flags) => new(flags.RawBuffer);

	public static implicit operator BitFlags<TFlagsEnum>(BitFlags flags) => new(flags.RawBuffer);



	/// <summary>
	/// Implicitly converts a BitFlags<T> instance to the enum type T.
	/// </summary>
	/// <param name="flags">The BitFlags<T> instance to convert.</param>
	/// <returns>The enum representation of the flags.</returns>
	public static implicit operator TFlagsEnum(BitFlags<TFlagsEnum> flags)
	{
		return Unsafe.As<uint, TFlagsEnum>(ref flags.RawBuffer);
	}

	/// <summary>
	/// Implicitly converts an enum of type T to a BitFlags<T> instance.
	/// </summary>
	/// <param name="enumValue">The enum value to convert.</param>
	/// <returns>A new BitFlags<T> instance representing the enum value.</returns>
	public static implicit operator BitFlags<TFlagsEnum>(TFlagsEnum enumValue)
	{
		return new BitFlags<TFlagsEnum>(EnumToUInt32(ref enumValue));
	}

	/// <summary>
	/// Implicitly converts a BitFlags<T> instance to an int.
	/// </summary>
	/// <param name="flags">The BitFlags<T> instance to convert.</param>
	/// <returns>The int representation of the flags.</returns>
	public static implicit operator int(BitFlags<TFlagsEnum> flags)
	{
		return (int)flags.RawBuffer;
	}

	/// <summary>
	/// Implicitly converts an int to a BitFlags<T> instance.
	/// </summary>
	/// <param name="value">The int value to convert.</param>
	/// <returns>A new BitFlags<T> instance representing the int value.</returns>
	public static implicit operator BitFlags<TFlagsEnum>(int value)
	{
		return new BitFlags<TFlagsEnum>((uint)value);
	}
}
