// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]
using NotNot.Internal;
using System.Runtime.CompilerServices;
using System.Collections.Generic; // **For EqualityComparer<TItem>**

namespace NotNot.Collections
{
	/// <summary>
	/// **ResizableArray** automatically resizes an array.
	/// - Grows to **2x** when out of space.
	/// - Shrinks to **/2** when at **/4** capacity.
	/// </summary>
	/// <typeparam name="TItem">Item type (supports both **reference** and **value** types)</typeparam>
	[ThreadSafety(ThreadSituation.Add, ThreadSituation.ReadExisting, ThreadSituation.Overwrite)]
	public class ResizableArray<TItem>
	{
		private readonly object _lock = new(); // **Lock object for thread safety**
		private TItem[] _raw; // **Internal storage array**

		public ResizableArray(int length = 0)
		{
			// **Initialize array with specified length**
			_raw = new TItem[length];
			Length = length;
		}

		/// <summary>
		/// **Indexer** with bounds check.
		/// </summary>
		public TItem this[int index]
		{
			[MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
			get
			{
				// **Bounds checking**: ensure index is valid.
				if (index < 0 || index >= Length)
				{
					throw new System.IndexOutOfRangeException("The index you are using is not allocated");
				}
				return _raw[index];
			}
		}

		/// <summary>
		/// **Clears** the array and resets Length.
		/// </summary>
		public void Clear()
		{
			lock (_lock)
			{
				// **Clear all elements** and reset length.
				Array.Clear(_raw, 0, Length);
				Length = 0;
			}
		}

		/// <summary>
		/// **Sets** the value at the specified index, growing the array if needed.
		/// </summary>
		public void Set(int index, TItem value)
		{
			lock (_lock)
			{
				// **Grow** the array if index is beyond current length.
				if (index >= Length)
				{
					Grow(index - (Length - 1));
				}
				// **Assign value** safely.
				_raw[index] = value;
			}
		}

		/// <summary>
		/// **Gets** the value at the specified index or uses **newCtor** to create and set it if the current value is default.
		/// - Uses EqualityComparer<TItem>.Default to check if value equals **default(TItem)**.
		///   (For **reference types**, default is **null**.)
		/// </summary>
		public TItem GetOrSet(int index, Func<TItem> newCtor)
		{
			lock (_lock)
			{
				// **Grow** if index is out of current range.
				if (index >= Length)
				{
					Grow(index - (Length - 1));
				}

				var toReturn = _raw[index];
				// **Check if current value is default** (for value types, default may be a valid value).
				if (EqualityComparer<TItem>.Default.Equals(toReturn, default(TItem)))
				{
					// **Create new value** and assign.
					toReturn = newCtor();
					_raw[index] = toReturn;
				}
				return toReturn;
			}
		}

		/// <summary>
		/// **Grows** the array by the specified count and returns the starting index.
		/// </summary>
		public int Grow(int count)
		{
			lock (_lock)
			{
				// **Ensure capacity**: if new required length exceeds current array length.
				if (count + Length > _raw.Length)
				{
					// **Calculate new capacity**: maximum of double the current capacity or required length.
					var newCapacity = Math.Max(_raw.Length * 2, Length + count);
					_SetCapacity(newCapacity);
				}
				var current = Length;
				Length += count; // **Update Length**
				return current;
			}
		}

		/// <summary>
		/// **Shrinks** the array by reducing Length and clearing removed elements.
		/// </summary>
		public void Shrink(int count)
		{
			lock (_lock)
			{
				// **Validate shrink**: cannot shrink more than available.
				if (count > Length)
				{
					throw new System.ArgumentOutOfRangeException(nameof(count), "Cannot shrink more than the current length");
				}
				Length -= count;
				Array.Clear(_raw, Length, count); // **Clear removed slots**
				_TryPack();
			}
		}

		/// <summary>
		/// **Sets** the Length and adjusts capacity if necessary.
		/// </summary>
		public void SetLength(int newLength)
		{
			lock (_lock)
			{
				if (newLength > _raw.Length)
				{
					// **Expand capacity** if newLength exceeds current array capacity.
					_SetCapacity(newLength);
				}
				else if (newLength < _raw.Length)
				{
					// **Shrink capacity** if newLength is less.
					_SetCapacity(newLength);
				}
				Length = newLength;
			}
		}

		/// <summary>
		/// **Attempts to pack** the array if it's largely underutilized.
		/// </summary>
		private void _TryPack()
		{
			// **No redundant locking**: called within locked context.
			if (_raw.Length > NotNotBclConfig.ResizableArray_minShrinkSize && Length < _raw.Length / 4)
			{
				// **Determine new capacity**: at least double the current length or the minimum shrink size.
				var newCapacity = Math.Max(Length * 2, NotNotBclConfig.ResizableArray_minShrinkSize);
				_SetCapacity(newCapacity);
			}
		}

		/// <summary>
		/// **Resizes** the underlying array to the specified capacity.
		/// </summary>
		private void _SetCapacity(int capacity)
		{
			// **Resize** without extra locking; called from locked methods.
			Array.Resize(ref _raw, capacity);
		}

		/// <summary>
		/// **Allocated slots count**.
		/// </summary>
		public int Length { get; protected set; }
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