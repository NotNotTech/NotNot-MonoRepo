using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NotNot;
using NotNot.Collections.Specialized;

namespace NotNot.Collections.Specialized;

public ref struct UnionSpan
{
	public UnionSpan(ref Span<byte> storage)
	{
		bytes = storage;
		ints = storage._CastAs<byte, int>();
		floats = storage._CastAs<byte, float>();

	}
	public Span<byte> bytes;
	public Span<int> ints;
	public Span<float> floats;



}
[StructLayout(LayoutKind.Explicit, Size = 16)]
public unsafe struct CustomData16
{
	[FieldOffset(0)] public Floats4 floats;
	[FieldOffset(0)] public Bytes16 bytes;


}


public unsafe struct Floats4
{
	public const int SIZE = 4;
	public fixed float data[SIZE];
	public ref float this[int index] => ref data[index];
	public int Length => SIZE;
}

public unsafe struct Bytes16
{
	public const int SIZE = 16;
	public fixed byte data[SIZE];
	public ref byte this[int index] => ref data[index];
	public int Length => SIZE;
}

/// <summary>
/// a custom struct with 64 bytes accessable storage.  be aware that these indicies overlap
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public unsafe struct CustomData64
{
	[FieldOffset(0)] public Bytes64 bytes;
	[FieldOffset(0)] public Floats16 floats;
	[FieldOffset(0)] public Ints16 ints;


}

public unsafe struct Bytes64
{
	/// <summary>
	/// size in bytes
	/// </summary>
	public const int SIZE = 64;
	public fixed byte data[SIZE];
	public ref byte this[int index] => ref data[index];
	public int Length => SIZE;

	//public unsafe ref  Span<byte> GetSpan()
	//{
	//	Span<byte> 
	//	var toReturn  = new Span<byte>((void*) data[0], SIZE);
	//	return ref toReturn;
	//}
}
public unsafe struct Floats16
{
	/// <summary>
	/// size in bytes
	/// </summary>
	public const int SIZE = 16;
	public fixed float data[SIZE];
	public ref float this[int index] => ref data[index];
	public int Length => SIZE;
}
public unsafe struct Ints16
{
	/// <summary>
	/// size in bytes
	/// </summary>
	public const int SIZE = 16;
	public fixed int data[SIZE];
	public ref int this[int index] => ref data[index];
	public int Length => SIZE;
}


