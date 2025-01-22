// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NotNot.Advanced;

/// <summary>
///    holdover from Raylib work.  keeping cuz not sure if still needed?
/// </summary>
public abstract class FramePacketBase
{
   /// <summary>
   ///    internal helper used to track writes, to help catch race conditions (misuse)
   /// </summary>
   public int _version;

   public bool IsInitialized { get; private set; }

   public bool IsSealed { get; private set; }

   public void NotifyWrite()
   {
      __.GetLogger()._EzErrorThrow(IsInitialized && _version > 0 && IsSealed == false);
      _version++;
   }

   public void Seal()
   {
      IsSealed = true;
   }

   public void Recycle()
   {
      __.GetLogger()._EzErrorThrow(IsInitialized && _version > 0);
      IsInitialized = false;
      _version = -1;
      OnRecycle();
   }

   public void Initialize()
   {
      __.GetLogger()._EzErrorThrow(IsInitialized == false && _version <= 0);
      IsInitialized = true;
      _version = 1;
      IsSealed = false;
      OnInitialize();
   }

   protected abstract void OnRecycle();
   protected abstract void OnInitialize();
}

/// <summary>
///    efficiently get/set a value for a given type.
///    This should be used in singleton type lookups.
///    <para>similar use as a <see cref="ThreadLocal{T}" /></para>
/// </summary>
/// <remarks>because of implementation, should only be used for a max of about 100 types, otherwise storage gets large</remarks>
/// <typeparam name="TValue"></typeparam>
public struct TypeLocal<TValue>
{
   private static volatile int _typeCounter = -1;


   private static class TypeSlot<TType>
   {
      internal static readonly int _index = Interlocked.Increment(ref _typeCounter);
   }

   /// <summary>
   ///    A small inefficiency:  will have 1 slot for each TType ever used for a TypeLocal call, regardless of if it's used in
   ///    this instance or not
   /// </summary>
   private TValue[] _storage;

   public TypeLocal()
   {
      _storage = new TValue[Math.Max(10, _typeCounter + 1)];
   }

   private TValue[] EnsureStorageCapacity<TType>()
   {
      if (TypeSlot<TType>._index >= _storage.Length)
      {
         Array.Resize(ref _storage, (_typeCounter + 1) * 2);
      }

      return _storage;
   }

   public void Set<TType>(TValue value)
   {
      //Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(EnsureStorageCapacity<T>()), TypeSlot<T>.Index) = value;
      var storage = EnsureStorageCapacity<TType>();
      storage[TypeSlot<TType>._index] = value;
   }

   public TValue Get<TType>()
   {
      //return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(EnsureStorageCapacity<T>()), TypeSlot<T>.Index);
      //return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_storage), TypeSlot<T>.Index);
      return _storage[TypeSlot<TType>._index];
   }

   public ref TValue GetRef<TType>()
   {
      //return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_storage), TypeSlot<T>.Index);
      //return ref _storage[TypeSlot<TType>._index].value;

      return ref _storage[TypeSlot<TType>._index];
   }
}

/// <summary>
///    allows extraction of the Sign, Exponent, and Mantissa from a float.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
internal struct FloatInspector
{
   [FieldOffset(0)] public float val;


   [FieldOffset(0)] public int bitfield;


   // public FloatInspector(float value)
   // {
   // 	this = new FloatInspector();
   // 	val = value;
   // }

   public int Sign
   {
      get => bitfield >> 31;
      set => bitfield |= value << 31;
   }

   public int Exponent
   {
      get => (bitfield & 0x7f800000) >> 23;
      // var e = bitfield & 0x7f800000;
      // e >>= 23;
      // return e;
      set => bitfield |= value << 23 & 0x7f800000;
   }

   public int Mantissa
   {
      get => bitfield & 0x007fffff;
      set => bitfield |= value & 0x007fffff;
   }

   public override string ToString()
   {
      var floatBytes = BitConverter.GetBytes(val);
      var bitBytes = BitConverter.GetBytes(bitfield);
      var floatBytesStr = string.Join(",", floatBytes.Select(b => b.ToString("X")));
      var bitBytesStr = string.Join(",", bitBytes.Select(b => b.ToString("X")));


      //return String.Format("FLOAT={0:G9} RT={0:R} FIELD={1:G},  HEXTEST={1:X8}, fBytes={2}, bBytes={3}", this.val, this.bitfield, floatBytesStr, bitBytesStr); //HEXTEST={1:X}, 
      return string.Format("FLOAT={0:G9} BITFIELD={1:G},  HEX={1:X}, S={2}, E={3:X}, M={4:X}", val, bitfield, Sign,
         Exponent, Mantissa); //HEXTEST={1:X}, 
   }
}

[StructLayout(LayoutKind.Explicit)]
internal struct Vector3Inspector
{
   [FieldOffset(0)] public Vector3 val;
   [FieldOffset(0)] public FloatInspector x;
   [FieldOffset(4)] public FloatInspector y;
   [FieldOffset(8)] public FloatInspector z;

   public override string ToString()
   {
      return string.Format("x=[{0}], \n\ty=[{1}], \n\tz=[{2}]", x, y, z);
   }
}

/// <summary>
///    Pin managed objects
/// </summary>
/// <remarks>
///    from @Zombie on C# Discord #LowLevel channel.  2022-01-30
///    <para>
///       other "interesting" ideas such as Modifying an objects type can be pondered at
///       https://github.com/WhiteBlackGoose/VeryUnsafe/blob/master/VeryUnsafe/VeryUnsafe.cs#L36
///    </para>
/// </remarks>
public static class Unsafe2
{
   /// <summary>
   ///    use to obtain raw access to a managed object.  allowing `fixed` pinning.
   ///    <para>
   ///       Usage:<code>fixed (byte* data = [AND_OPERATOR]GetRawObjectData(managed)){  }</code>
   ///    </para>
   /// </summary>
   public static ref byte GetRawObjectData(object o)
   {
      //usage:  fixed (byte* data = &GetRawObjectData(managed)) { }
      return ref new PinnableUnion(o).Pinnable.Data;
   }

   /// <summary>
   ///    list has an array located inside it. this method will return you a reference to it.
   ///    Keep in mind that if the list is modified, this reference may nolonger be valid.
   /// </summary>
   /// <typeparam name="T"></typeparam>
   /// <param name="list"></param>
   /// <returns></returns>
   public static T[] GetArray<T>(List<T> list)
   {
      //get span from list
      var span = CollectionsMarshal.AsSpan(list);
      //ref to first element of the array
      ref T r0 = ref MemoryMarshal.GetReference(span);
      //pointer to location 2 references backward in memory.   This is the location of the method table  (what object references point to).
      ref T r1 = ref Unsafe.SubtractByteOffset(ref r0, (nuint)(nint.Size * 2));


      return Unsafe.As<T, T[]>(ref r1);
      //fancy coersion if the above fails at runtime
      //var cast = (delegate*<ref T, T[]>)(delegate*<ref byte, ref byte>)&Unsafe.As<byte, byte>;
      //return cast(ref r1);
   }

   [StructLayout(LayoutKind.Sequential)]
   private sealed class Pinnable
   {
      public byte Data;
   }

   [StructLayout(LayoutKind.Explicit)]
   private struct PinnableUnion
   {
      [FieldOffset(0)] public object Object;

      [FieldOffset(0)] public Pinnable Pinnable;

      public PinnableUnion(object o)
      {
         Unsafe.SkipInit(out this);
         Object = o;
      }
   }
}

///// <summary>Contains generic, low-level functionality for manipulating pointers.</summary>
//public static class SRC_Unsafe
//{
//	/// <summary>Reads a value of type <typeparamref name="T" /> from the given location.</summary>
//	/// <param name="source">The location to read from.</param>
//	/// <typeparam name="T">The type to read.</typeparam>
//	/// <returns>An object of type <typeparamref name="T" /> read from the given location.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe T Read<T>(void* source) => *(T*)source;

//	/// <summary>Reads a value of type <typeparamref name="T" /> from the given location without assuming architecture dependent alignment of the addresses.</summary>
//	/// <param name="source">The location to read from.</param>
//	/// <typeparam name="T">The type to read.</typeparam>
//	/// <returns>An object of type <typeparamref name="T" /> read from the given location.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe T ReadUnaligned<T>(void* source) => *(T*)source;

//	/// <summary>Reads a value of type <typeparamref name="T" /> from the given location without assuming architecture dependent alignment of the addresses.</summary>
//	/// <param name="source">The location to read from.</param>
//	/// <typeparam name="T">The type to read.</typeparam>
//	/// <returns>An object of type <typeparamref name="T" /> read from the given location.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static T ReadUnaligned<T>(ref byte source) => ^(T &) ref source;

//    /// <summary>Writes a value of type <typeparamref name="T" /> to the given location.</summary>
//    /// <param name="destination">The location to write to.</param>
//    /// <param name="value">The value to write.</param>
//    /// <typeparam name="T">The type of value to write.</typeparam>
//    [NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe void Write<T>(void* destination, T value) => *(T*)destination = value;

//	/// <summary>Writes a value of type <typeparamref name="T" /> to the given location without assuming architecture dependent alignment of the addresses.</summary>
//	/// <param name="destination">The location to write to.</param>
//	/// <param name="value">The value to write.</param>
//	/// <typeparam name="T">The type of value to write.</typeparam>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe void WriteUnaligned<T>(void* destination, T value) => *(T*)destination = value;

//	/// <summary>Writes a value of type <typeparamref name="T" /> to the given location without assuming architecture dependent alignment of the addresses.</summary>
//	/// <param name="destination">The location to write to.</param>
//	/// <param name="value">The value to write.</param>
//	/// <typeparam name="T">The type of value to write.</typeparam>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static void WriteUnaligned<T>(ref byte destination, T value) => ^(T &) ref destination = value;

//    /// <summary>Copies a value of type <typeparamref name="T" /> to the given location.</summary>
//    /// <param name="destination">The location to copy to.</param>
//    /// <param name="source">A reference to the value to copy.</param>
//    /// <typeparam name="T">The type of value to copy.</typeparam>
//    [NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe void Copy<T>(void* destination, ref T source) => *(T*)destination = source;

//	/// <summary>Copies a value of type <typeparamref name="T" /> to the given location.</summary>
//	/// <param name="destination">The location to copy to.</param>
//	/// <param name="source">A pointer to the value to copy.</param>
//	/// <typeparam name="T">The type of value to copy.</typeparam>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe void Copy<T>(ref T destination, void* source) => destination = *(T*)source;

//	/// <summary>Returns a pointer to the given by-ref parameter.</summary>
//	/// <param name="value">The object whose pointer is obtained.</param>
//	/// <typeparam name="T">The type of object.</typeparam>
//	/// <returns>A pointer to the given value.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe void* AsPointer<T>(ref T value) => (void*)ref value;

//	/// <summary>Bypasses definite assignment rules for a given value.</summary>
//	/// <param name="value">The uninitialized object.</param>
//	/// <typeparam name="T">The type of the uninitialized object.</typeparam>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static void SkipInit<T>(out T value)
//	{
//	}

//	/// <summary>Returns the size of an object of the given type parameter.</summary>
//	/// <typeparam name="T">The type of object whose size is retrieved.</typeparam>
//	/// <returns>The size of an object of type <typeparamref name="T" />.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static int SizeOf<T>() => sizeof(T);

//	/// <summary>Copies bytes from the source address to the destination address.</summary>
//	/// <param name="destination">The destination address to copy to.</param>
//	/// <param name="source">The source address to copy from.</param>
//	/// <param name="byteCount">The number of bytes to copy.</param>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe void CopyBlock(void* destination, void* source, uint byteCount)
//	{
//		// ISSUE: cpblk instruction
//		__memcpy((IntPtr)destination, (IntPtr)source, (int)byteCount);
//	}

//	/// <summary>Copies bytes from the source address to the destination address.</summary>
//	/// <param name="destination">The destination address to copy to.</param>
//	/// <param name="source">The source address to copy from.</param>
//	/// <param name="byteCount">The number of bytes to copy.</param>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static void CopyBlock(ref byte destination, ref byte source, uint byteCount)
//	{
//		// ISSUE: cpblk instruction
//		__memcpy(ref destination, ref source, (int)byteCount);
//	}

//	/// <summary>Copies bytes from the source address to the destination address without assuming architecture dependent alignment of the addresses.</summary>
//	/// <param name="destination">The destination address to copy to.</param>
//	/// <param name="source">The source address to copy from.</param>
//	/// <param name="byteCount">The number of bytes to copy.</param>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe void CopyBlockUnaligned(void* destination, void* source, uint byteCount)
//	{
//		// ISSUE: cpblk instruction
//		__memcpy((IntPtr)destination, (IntPtr)source, (int)byteCount);
//	}

//	/// <summary>Copies bytes from the source address to the destination address without assuming architecture dependent alignment of the addresses.</summary>
//	/// <param name="destination">The destination address to copy to.</param>
//	/// <param name="source">The source address to copy from.</param>
//	/// <param name="byteCount">The number of bytes to copy.</param>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static void CopyBlockUnaligned(ref byte destination, ref byte source, uint byteCount)
//	{
//		// ISSUE: cpblk instruction
//		__memcpy(ref destination, ref source, (int)byteCount);
//	}

//	/// <summary>Initializes a block of memory at the given location with a given initial value.</summary>
//	/// <param name="startAddress">The address of the start of the memory block to initialize.</param>
//	/// <param name="value">The value to initialize the block to.</param>
//	/// <param name="byteCount">The number of bytes to initialize.</param>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe void InitBlock(void* startAddress, byte value, uint byteCount)
//	{
//		// ISSUE: initblk instruction
//		__memset((IntPtr)startAddress, (int)value, (int)byteCount);
//	}

//	/// <summary>Initializes a block of memory at the given location with a given initial value.</summary>
//	/// <param name="startAddress">The address of the start of the memory block to initialize.</param>
//	/// <param name="value">The value to initialize the block to.</param>
//	/// <param name="byteCount">The number of bytes to initialize.</param>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static void InitBlock(ref byte startAddress, byte value, uint byteCount)
//	{
//		// ISSUE: initblk instruction
//		__memset(ref startAddress, (int)value, (int)byteCount);
//	}

//	/// <summary>Initializes a block of memory at the given location with a given initial value without assuming architecture dependent alignment of the address.</summary>
//	/// <param name="startAddress">The address of the start of the memory block to initialize.</param>
//	/// <param name="value">The value to initialize the block to.</param>
//	/// <param name="byteCount">The number of bytes to initialize.</param>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe void InitBlockUnaligned(void* startAddress, byte value, uint byteCount)
//	{
//		// ISSUE: initblk instruction
//		__memset((IntPtr)startAddress, (int)value, (int)byteCount);
//	}

//	/// <summary>Initializes a block of memory at the given location with a given initial value without assuming architecture dependent alignment of the address.</summary>
//	/// <param name="startAddress">The address of the start of the memory block to initialize.</param>
//	/// <param name="value">The value to initialize the block to.</param>
//	/// <param name="byteCount">The number of bytes to initialize.</param>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static void InitBlockUnaligned(ref byte startAddress, byte value, uint byteCount)
//	{
//		// ISSUE: initblk instruction
//		__memset(ref startAddress, (int)value, (int)byteCount);
//	}

//	/// <summary>Casts the given object to the specified type.</summary>
//	/// <param name="o">The object to cast.</param>
//	/// <typeparam name="T">The type which the object will be cast to.</typeparam>
//	/// <returns>The original object, casted to the given type.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static T As<T>(object o) where T : class => (T)o;

//	/// <summary>Reinterprets the given location as a reference to a value of type <typeparamref name="T" />.</summary>
//	/// <param name="source">The location of the value to reference.</param>
//	/// <typeparam name="T">The type of the interpreted location.</typeparam>
//	/// <returns>A reference to a value of type <typeparamref name="T" />.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe ref T AsRef<T>(void* source) => ref (*(T*)source);

//	/// <summary>Reinterprets the given read-only reference as a reference.</summary>
//	/// <param name="source">The read-only reference to reinterpret.</param>
//	/// <typeparam name="T">The type of reference.</typeparam>
//	/// <returns>A reference to a value of type <typeparamref name="T" />.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static ref T AsRef<T>(in T source) => ref source;

//	/// <summary>Reinterprets the given reference as a reference to a value of type <typeparamref name="TTo" />.</summary>
//	/// <param name="source">The reference to reinterpret.</param>
//	/// <typeparam name="TFrom">The type of reference to reinterpret.</typeparam>
//	/// <typeparam name="TTo">The desired type of the reference.</typeparam>
//	/// <returns>A reference to a value of type <typeparamref name="TTo" />.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static ref TTo As<TFrom, TTo>(ref TFrom source) => (TTo &) ref source;

//    /// <summary>Returns a <see langword="mutable ref" /> to a boxed value.</summary>
//    /// <param name="box">The value to unbox.</param>
//    /// <typeparam name="T">The type to be unboxed.</typeparam>
//    /// <exception cref="T:System.NullReferenceException">
//    /// <paramref name="box" /> is <see langword="null" />, and <typeparamref name="T" /> is a non-nullable value type.</exception>
//    /// <exception cref="T:System.InvalidCastException">
//    ///         <paramref name="box" /> is not a boxed value type.
//    /// 
//    /// -or-
//    /// 
//    /// <paramref name="box" /> is not a boxed <typeparamref name="T" />.</exception>
//    /// <exception cref="T:System.TypeLoadException">
//    /// <typeparamref name="T" /> cannot be found.</exception>
//    /// <returns>A <see langword="mutable ref" /> to the boxed value <paramref name="box" />.</returns>
//    [NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static ref T Unbox<T>(object box) where T : struct => @(T) box;

//	/// <summary>Adds an element offset to the given reference.</summary>
//	/// <param name="source">The reference to add the offset to.</param>
//	/// <param name="elementOffset">The offset to add.</param>
//	/// <typeparam name="T">The type of reference.</typeparam>
//	/// <returns>A new reference that reflects the addition of offset to pointer.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static ref T Add<T>(ref T source, int elementOffset) => (T &)((IntPtr)ref source + elementOffset * (IntPtr)sizeof(T));

//	/// <summary>Adds an element offset to the given void pointer.</summary>
//	/// <param name="source">The void pointer to add the offset to.</param>
//	/// <param name="elementOffset">The offset to add.</param>
//	/// <typeparam name="T">The type of void pointer.</typeparam>
//	/// <returns>A new void pointer that reflects the addition of offset to the specified pointer.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe void* Add<T>(void* source, int elementOffset) => (void*)((IntPtr)source + elementOffset * (IntPtr)sizeof(T));

//	/// <summary>Adds an element offset to the given reference.</summary>
//	/// <param name="source">The reference to add the offset to.</param>
//	/// <param name="elementOffset">The offset to add.</param>
//	/// <typeparam name="T">The type of reference.</typeparam>
//	/// <returns>A new reference that reflects the addition of offset to pointer.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe ref T Add<T>(ref T source, IntPtr elementOffset) => @((T*) ref source)[elementOffset.ToInt64()];

//    [MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe ref T Add<T>(ref T source, [NonVersionable, NativeInteger] UIntPtr elementOffset) => @((T*) ref source)[elementOffset];

//    /// <summary>Adds a byte offset to the given reference.</summary>
//    /// <param name="source">The reference to add the offset to.</param>
//    /// <param name="byteOffset">The offset to add.</param>
//    /// <typeparam name="T">The type of reference.</typeparam>
//    /// <returns>A new reference that reflects the addition of byte offset to pointer.</returns>
//    [NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static ref T AddByteOffset<T>(ref T source, IntPtr byteOffset) => (T &)((IntPtr)ref source + byteOffset);

//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static ref T AddByteOffset<T>(ref T source, [NativeInteger, NonVersionable] UIntPtr byteOffset) => (T &)((IntPtr)ref source + (IntPtr)byteOffset);

//	/// <summary>Subtracts an element offset from the given reference.</summary>
//	/// <param name="source">The reference to subtract the offset from.</param>
//	/// <param name="elementOffset">The offset to subtract.</param>
//	/// <typeparam name="T">The type of reference.</typeparam>
//	/// <returns>A new reference that reflects the subtraction of offset from pointer.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static ref T Subtract<T>(ref T source, int elementOffset) => (T &)((IntPtr)ref source - elementOffset * (IntPtr)sizeof(T));

//	/// <summary>Subtracts an element offset from the given void pointer.</summary>
//	/// <param name="source">The void pointer to subtract the offset from.</param>
//	/// <param name="elementOffset">The offset to subtract.</param>
//	/// <typeparam name="T">The type of the void pointer.</typeparam>
//	/// <returns>A new void pointer that reflects the subtraction of offset from the specified pointer.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe void* Subtract<T>(void* source, int elementOffset) => (void*)((IntPtr)source - elementOffset * (IntPtr)sizeof(T));

//	/// <summary>Subtracts an element offset from the given reference.</summary>
//	/// <param name="source">The reference to subtract the offset from.</param>
//	/// <param name="elementOffset">The offset to subtract.</param>
//	/// <typeparam name="T">The type of reference.</typeparam>
//	/// <returns>A new reference that reflects the subtraction of offset from pointer.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe ref T Subtract<T>(ref T source, IntPtr elementOffset) => ref (*((T*)ref source - elementOffset.ToInt64()));

//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static unsafe ref T Subtract<T>(ref T source, [NativeInteger, NonVersionable] UIntPtr elementOffset) => ref (*((T*)ref source - elementOffset));

//	/// <summary>Subtracts a byte offset from the given reference.</summary>
//	/// <param name="source">The reference to subtract the offset from.</param>
//	/// <param name="byteOffset">The offset to subtract.</param>
//	/// <typeparam name="T">The type of reference.</typeparam>
//	/// <returns>A new reference that reflects the subtraction of byte offset from pointer.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static ref T SubtractByteOffset<T>(ref T source, IntPtr byteOffset) => (T &)((IntPtr)ref source - byteOffset);

//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static ref T SubtractByteOffset<T>(ref T source, [NonVersionable, NativeInteger] UIntPtr byteOffset) => (T &)((IntPtr)ref source - (IntPtr)byteOffset);

//	/// <summary>Determines the byte offset from origin to target from the given references.</summary>
//	/// <param name="origin">The reference to origin.</param>
//	/// <param name="target">The reference to target.</param>
//	/// <typeparam name="T">The type of reference.</typeparam>
//	/// <returns>Byte offset from origin to target i.e. <paramref name="target" /> - <paramref name="origin" />.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static IntPtr ByteOffset<T>(ref T origin, ref T target) => (IntPtr)ref target - (IntPtr)ref origin;

//	/// <summary>Determines whether the specified references point to the same location.</summary>
//	/// <param name="left">The first reference to compare.</param>
//	/// <param name="right">The second reference to compare.</param>
//	/// <typeparam name="T">The type of reference.</typeparam>
//	/// <returns>
//	/// <see langword="true" /> if <paramref name="left" /> and <paramref name="right" /> point to the same location; otherwise, <see langword="false" />.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static bool AreSame<T>(ref T left, ref T right) => ref left == ref right;

//	/// <summary>Returns a value that indicates whether a specified reference is greater than another specified reference.</summary>
//	/// <param name="left">The first value to compare.</param>
//	/// <param name="right">The second value to compare.</param>
//	/// <typeparam name="T">The type of the reference.</typeparam>
//	/// <returns>
//	/// <see langword="true" /> if <paramref name="left" /> is greater than <paramref name="right" />; otherwise, <see langword="false" />.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static bool IsAddressGreaterThan<T>(ref T left, ref T right) => ref left > ref right;

//	/// <summary>Returns a value that indicates whether a specified reference is less than another specified reference.</summary>
//	/// <param name="left">The first value to compare.</param>
//	/// <param name="right">The second value to compare.</param>
//	/// <typeparam name="T">The type of the reference.</typeparam>
//	/// <returns>
//	/// <see langword="true" /> if <paramref name="left" /> is less than <paramref name="right" />; otherwise, <see langword="false" />.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static bool IsAddressLessThan<T>(ref T left, ref T right) => ref left < ref right;

//	/// <summary>Determines if a given reference to a value of type <typeparamref name="T" /> is a null reference.</summary>
//	/// <param name="source">The reference to check.</param>
//	/// <typeparam name="T">The type of the reference.</typeparam>
//	/// <returns>
//	/// <see langword="true" /> if <paramref name="source" /> is a null reference; otherwise, <see langword="false" />.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static bool IsNullRef<T>(ref T source)
//	{
//		// ISSUE: unable to decompile the method.
//	}

//	/// <summary>Returns a reference to a value of type <typeparamref name="T" /> that is a null reference.</summary>
//	/// <typeparam name="T">The type of the reference.</typeparam>
//	/// <returns>A reference to a value of type <typeparamref name="T" /> that is a null reference.</returns>
//	[NonVersionable]
//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
//	public static ref T NullRef<T>() => (T &) IntPtr.Zero;
//  }

//internal class NonVersionableAttribute : Attribute
//{
//}