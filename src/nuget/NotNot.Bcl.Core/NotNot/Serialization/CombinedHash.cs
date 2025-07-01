// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

namespace NotNot.Serialization;

/// <summary>
///    support hashing of multiple items
/// </summary>
/// <remarks>
///    to support hashing for a container object, that might have an arbitrary number of things inside.
///    I want to generate a hash to check if two containers are the "same"  (meaning they store the same contents).
/// </remarks>
public unsafe struct CombinedHash : IComparable<CombinedHash>, IEquatable<CombinedHash>
{
	public const int SIZE = 8;

	/// <summary>
	///    some .GetHashCode() implementations are not psudoRandom, such as for int/long.
	///    so to help prevent hash collisions these values are spread across the int spectrum
	/// </summary>
	private const ulong SALT_INCREMENT = ulong.MaxValue / SIZE;

	/// <summary>
	/// </summary>
	private ulong _compressedHash;

	private fixed uint _storage[SIZE];


	public CombinedHash(Span<int> hashes)
	{
		hashes.Sort();
		var loopSalt = (uint)(uint.MaxValue / hashes.Length / SIZE);


		for (int i = 0; i < hashes.Length; i++)
		{
			uint salt = (uint)(loopSalt * (i / SIZE));
			uint value = (uint)(hashes[i] + salt);
			_storage[i % SIZE] += value;
		}

		_compressedHash = 0;
		for (var i = 0; i < SIZE; i++)
		{
			_compressedHash += _storage[i] + SALT_INCREMENT * (uint)i;
		}
	}

	public void AccumulateHash(int itemIndex, int hashCode)
	{
	}

	public int CompareTo(CombinedHash other)
	{
		var result = _compressedHash.CompareTo(other._compressedHash);
		if (result == 0)
		{
			for (var i = 0; i < SIZE; i++)
			{
				result = _storage[i].CompareTo(other._storage[i]);
				if (result == 0)
				{
					continue;
				}

				return result;
			}
		}

		return result;
	}

	public bool Equals(CombinedHash other)
	{
		return CompareTo(other) == 0;
	}

	public override int GetHashCode()
	{
		return (int)_compressedHash;
	}

	public ulong GetHashCode64()
	{
		return _compressedHash;
	}
}
