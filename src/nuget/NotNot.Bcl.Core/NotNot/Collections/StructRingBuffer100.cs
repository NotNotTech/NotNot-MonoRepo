// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Runtime.CompilerServices;

namespace NotNot.Collections;



public struct StructRingBuffer100<T> where T : unmanaged
{

	public const int _SIZEOF = 100;

	[InlineArray(_SIZEOF)]
	public struct StructArray100<T> where T : unmanaged
	{
		private T _element0;
	}


	public StructArray100<T> _storage;
	public int _currentIndex = 0;
	public int _count = 0;

	public StructRingBuffer100()
	{
	}

	public void Insert(ref T item)
	{
		_storage[_currentIndex] = item;
		_currentIndex = (_currentIndex + 1) % _SIZEOF;
		if (_count < _SIZEOF) _count++;
	}

	public T this[int index]
	{
		get
		{
			if (index < 0 || index >= _count)
				throw new IndexOutOfRangeException("Index was out of range. Must be non-negative and less than the size of the collection.");
			int actualIndex = (_currentIndex - _count + index + _SIZEOF) % _SIZEOF;
			return _storage[actualIndex];
		}
	}

	public T First => this[0];
	public T Last => this[_count];

	public void Clear()
	{
		_currentIndex = 0;
		_count = 0;
	}
}
