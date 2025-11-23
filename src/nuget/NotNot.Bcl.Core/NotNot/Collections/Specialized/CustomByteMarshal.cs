using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NotNot;
using NotNot.Collections.Specialized;

namespace NotNot.Collections.Specialized;


public unsafe struct Bytes16
{
	public const int SIZE = 16;
	public fixed byte data[SIZE];
	public ref byte this[int index] => ref data[index];
	public int Length => SIZE;
}


/// <summary>
/// Represents a fixed-size, 32-byte buffer for storing binary data. (256bit)
/// </summary>
public unsafe struct Bytes32
{
	/// <summary>
	/// size in items
	/// </summary>
	public const int SIZE = 32;
	public fixed byte data[SIZE];
	public ref byte this[int index] => ref data[index];
	public int Length => SIZE;
}


/// <summary>
/// Represents a fixed-size buffer containing 64 bytes of data (512bit)
/// </summary>
/// <remarks>This struct is typically used for scenarios that require a block of memory with a fixed size, such as
/// cryptographic operations, serialization, or interop with unmanaged code. The buffer is stack-allocated and provides
/// direct access to its underlying bytes.</remarks>
public unsafe struct Bytes64
{
	/// <summary>
	/// size in items
	/// </summary>
	public const int SIZE = 64;
	public fixed byte data[SIZE];
	public ref byte this[int index] => ref data[index];
	public int Length => SIZE;

	public unsafe Span<byte> GetSpan()
	{
		return new Span<byte>((void*)data[0], SIZE);
		//Span<byte>
		//var toReturn = new Span<byte>((void*)data[0], SIZE);
		//return ref toReturn;
	}
}