// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using NotNot;
using NotNot.Internal;
using System.Runtime.CompilerServices;

namespace NotNot.Collections._unused;

/// <summary>
///    Functionality to automatically resize an array.
///    Grows to 2x when out of space.  shrinks to /2 when at /4 capacity.
/// </summary>
/// <typeparam name="TItem"></typeparam>
[ThreadSafety(ThreadSituation.Add, ThreadSituation.ReadExisting, ThreadSituation.Overwrite)]
public class ResizableArray<TItem>
{
   private object _lock = new();
   private TItem[] _raw;

   public ResizableArray(int length = 0)
   {
      _raw = new TItem[length];
      //_UNSAFE_raw = Unsafe.As<__UNSAFE_ArrayData<TItem>>(_raw);
      Length = length;
   }

   //public Span<TItem> Span { get => _raw; }
   /// <summary>
   /// must be recreated every array resize
   /// </summary>
   //private __UNSAFE_ArrayData<TItem> _UNSAFE_raw;
   /// <summary>
   ///    allocated slots
   /// </summary>
   public int Length { get; protected set; }

   public TItem this[int index]
   {
      [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
      get =>
         //_check.Poke();
         //__.CHECKED.Throw(index < Length, "out of bounds.  the index you are using is not allocated");
         //return _raw.DangerousGetReferenceAt(index);
         //return _raw[index];
         //return ref _raw[index];
         _raw[index];
      //return _UNSAFE_raw[index];
      //var _UNSAFE_raw2 = Unsafe.As<__UNSAFE_ArrayData>(_raw);
      //ref TItem r0 = ref Unsafe.As<byte, TItem>(ref _UNSAFE_raw.Data);
      ////ref TItem ri = ref Unsafe.Add(ref r0, index);
      ////return ref ri;
      //return Unsafe.Add(ref r0, index);
      //set
      //{
      //	__.CHECKED.Throw(index < Length, "out of bounds.  the index you are using is not allocated");
      //	_raw[index] = value;
      //}
   }

   public void Clear()
   {
      lock (_lock)
      {
         Array.Clear(_raw, 0, Length);
         Length = 0;
      }
   }

   public void Set(int index, TItem value)
   {
      if (index >= Length)
      {
         lock (_lock)
         {
            if (index >= Length)
            {
               Grow(index - (Length - 1));
            }
         }
      }

      _raw[index] = value;
   }

   public TItem GetOrSet(int index, Func<TItem> newCtor)
   {
      if (index >= Length)
      {
         lock (_lock)
         {
            if (index >= Length)
            {
               Grow(index - (Length - 1));
            }
         }
      }

      var toReturn = _raw[index];
      if (toReturn == null)
      {
         lock (_lock)
         {
            toReturn = _raw[index];
            if (toReturn == null)
            {
               toReturn = newCtor();
               _raw[index] = toReturn;
            }
         }
      }

      return toReturn;
   }


   /// <summary>
   ///    returns the next available index
   /// </summary>
   /// <returns></returns>
   public int Grow(int count)
   {
      lock (_lock)
      {
         //_check.Enter();
         if (count + Length > _raw.Length)
         {
            var newCapacity = Math.Max(_raw.Length * 2, Length + count);
            _SetCapacity(newCapacity);
         }

         var current = Length;
         Length += count;
         //_check.Exit();
         return current;
      }
   }

   public void Shrink(int count)
   {
      lock (_lock)
      {
         //_check.Enter();
         Length -= count;
         Array.Clear(_raw, Length, count);
         _TryPack();
         //_check.Exit();
      }
   }

   public void SetLength(int newLength)
   {
      lock (_lock)
      {
         //_check.Enter();
         if (newLength < _raw.Length)
         {
            _SetCapacity(newLength);
         }

         Length = newLength;
         //_check.Exit();
      }
   }

   private void _TryPack()
   {
      lock (_lock)
      {
         if (_raw.Length > NotNotBclConfig.ResizableArray_minShrinkSize && Length < _raw.Length / 4)
         {
            var newCapacity = Math.Max(Length * 2, NotNotBclConfig.ResizableArray_minShrinkSize);
            _SetCapacity(newCapacity);
         }
      }
   }


   /// <summary>
   ///    preallocates the capacity specified
   ///    <para>does NOT increment count, just pre-alloctes to avoid resizing the internal storage array</para>
   /// </summary>
   /// <param name="capacity"></param>
   private void _SetCapacity(int capacity)
   {
      lock (_lock)
      {
         Array.Resize(ref _raw, capacity);
         //_UNSAFE_raw = Unsafe.As<__UNSAFE_ArrayData<TItem>>(_raw);
      }
   }
}

//public class ResizableArray<TItem> where TItem : class
//{
//	public TItem[] _raw;
//	public int Length { get; protected set; }
//	private RaceCheck _check;

//	private object _lock = new();

//	public ResizableArray(int length = 0)
//	{
//		_raw = new TItem[length];
//		Length = length;
//	}

//	public void Clear()
//	{
//		_check.Enter();
//		Array.Clear(_raw, 0, Length);
//		Length = 0;
//		_check.Exit();
//	}

//	public ref TItem this[int index]
//	{
//		get
//		{
//			//_check.Poke();
//			__.CHECKED.Throw(index < Length, "out of bounds.  the index you are using is not allocated");
//			return ref _raw[index];
//		}
//		//set
//		//{
//		//	__.CHECKED.Throw(index < Length, "out of bounds.  the index you are using is not allocated");
//		//	_raw[index] = value;
//		//}
//	}

//	public TItem GetOrSet(int index, Func<TItem> newCtor)
//	{
//		if (index >= Length)
//		{
//			lock (_lock)
//			{
//				if (index >= Length)
//				{
//					//expand our storage to 2x to avoid thrashing
//					_SetCapacity(index * 2);
//					Grow(index - (Length - 1));
//				}
//			}
//		}

//		var toReturn = _raw[index];
//		if (toReturn == null)
//		{
//			lock (_lock)
//			{
//				toReturn = _raw[index];
//				if (toReturn == null)
//				{
//					toReturn = newCtor();
//					_raw[index] = toReturn;
//				}
//			}
//		}
//		return toReturn;
//	}

//	/// <summary>
//	/// returns the next available index
//	/// </summary>
//	/// <returns></returns>
//	public int Grow(int count)
//	{
//		lock (_lock)
//		{
//			_check.Enter();
//			if ((count + Length) > _raw.Length)
//			{
//				var newCapacity = Math.Max(_raw.Length * 2, Length + count);
//				this._SetCapacity(newCapacity);
//			}

//			var current = Length;
//			Length += count;
//			_check.Exit();
//			return current;
//		}
//	}

//	public void Shrink(int count)
//	{
//		lock (_lock)
//		{
//			_check.Enter();
//			Length -= count;
//			Array.Clear(_raw, Length, count);
//			this._TryPack();
//			_check.Exit();
//		}
//	}

//	public void SetLength(int newLength)
//	{
//		lock (_lock)
//		{
//			_check.Enter();
//			if (newLength < _raw.Length)
//			{
//				this._SetCapacity(newLength);
//			}
//			Length = newLength;
//			_check.Exit();
//		}
//	}

//	private void _TryPack()
//	{
//		lock (_lock)
//		{
//			if (_raw.Length > __Config.ResizableArray_minShrinkSize && (Length < _raw.Length / 4))
//			{
//				var newCapacity = Math.Max(Length * 2, __Config.ResizableArray_minShrinkSize);
//				this._SetCapacity(newCapacity);
//			}
//		}
//	}

//	/// <summary>
//	/// preallocates the capacity specified
//	/// <para>does NOT increment count, just pre-alloctes to avoid resizing the internal storage array</para>
//	/// </summary>
//	/// <param name="capacity"></param>
//	private void _SetCapacity(int capacity)
//	{
//		lock (_lock)
//		{
//			Array.Resize(ref _raw, capacity);
//		}
//	}

//}