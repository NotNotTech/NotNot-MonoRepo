// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Runtime.InteropServices;

namespace NotNot.Collections._unused;

/// <summary>
///    pretty similar to ResizableArray.   probably should be deleted and just use that.
/// </summary>
/// <typeparam name="T"></typeparam>
public class SlotList<T> : IDisposable where T : class
{
	public int _count;

	private List<int> _freeSlots = new();
	private List<T> _storage = new();

	/// <summary>
	///    get storage as a span.  reads are safe with AllocSlot() as long as slots being added are not access via this Span.
	/// </summary>
	public Span<T> Span => CollectionsMarshal.AsSpan(_storage);

	public bool IsDisposed { get; private set; }

	public void Dispose()
	{
		if (IsDisposed)
		{
			__.GetLogger()._EzError(false, "why already disposed?");
			return;
		}

		IsDisposed = true;
		_storage.Clear();
		_storage = null;
		_freeSlots.Clear();
		_freeSlots = null;
		_count = -1;
	}

	public int AllocSlot()
	{
		int slot;
		bool result;
		lock (_freeSlots)
		{
			result = _freeSlots._TryTakeLast(out slot);
		}

		if (!result)
		{
			lock (_storage)
			{
				slot = _storage.Count;
				_storage.Add(default);
				_count++;
			}
		}

#pragma warning disable PH_B003 // Unsynchronized Collection Access
		__.GetLogger()._EzError(_storage[slot] == default(T));
#pragma warning restore PH_B003 // Unsynchronized Collection Access
		return slot;
	}

	/// <summary>
	///    This will null the slot, no need to do so manually
	/// </summary>
	/// <param name="slot"></param>
	public void FreeSlot(int slot)
	{
		lock (_freeSlots)
		{
			_freeSlots.Add(slot);

			lock (_storage)
			{
				_count--;
				_storage[slot] = default;


				__.GetLogger()._EzError(_storage[slot] == default(T));
				//try to pack if possible
				if (slot == _storage.Count - 1)
				{
					//the slot we are freeing is the last slot in the _storage array.    
					lock (_freeSlots)
					{
						//now have exclusive lock on _freeSlots and _storage

						//sort free so highest at end
						_freeSlots.Sort();

						//while the last free slot is the last slot in storage, remove both
						while (_freeSlots.Count > 0 && _freeSlots[_freeSlots.Count - 1] == _storage.Count - 1)
						{
							var result = _freeSlots._TryTakeLast(out var removedFreeSlot);
							__.GetLogger()._EzError(result && removedFreeSlot == _storage.Count - 1);
							_storage._RemoveLast();
						}
					}
				}
			}
		}
	}
}
