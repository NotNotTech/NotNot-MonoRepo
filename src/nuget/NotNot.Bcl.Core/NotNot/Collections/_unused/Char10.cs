// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

namespace NotNot.Collections._unused;

/// <summary>
///    allows embedding a string of length 10 into a struct/class without GC overhead
/// </summary>
public unsafe struct Char10
{
	public const int SIZEOF = 10;
	private fixed char _storage[10];
	private int _length;

	public ref char this[int index] => ref _storage[index];

	public void Clear()
	{
		fixed (char* ptr = _storage)
		{
			var target = new Span<char>(ptr, SIZEOF);
			target.Clear();
			_length = 0;
		}
	}

	public void From(ref Span<char> value, int length)
	{
		fixed (char* ptr = _storage)
		{
			__.GetLogger()._EzError(length <= SIZEOF, "warning, data length truncated when storing into this struct");
			_length = length < SIZEOF ? length : SIZEOF;
			//var target = new Span<char>(ptr, SIZEOF);
			var target = new Span<char>(ptr, _length);
			value.CopyTo(target);
		}
	}

	public void From(ref ReadOnlySpan<char> value, int length)
	{
		fixed (char* ptr = _storage)
		{
			__.GetLogger()._EzError(length <= SIZEOF, "warning, data length truncated when storing into this struct");
			_length = length < SIZEOF ? length : SIZEOF;
			//var target = new Span<char>(ptr, SIZEOF);
			var target = new Span<char>(ptr, _length);
			value.CopyTo(target);
		}
	}

	public void From(char* value, int length)
	{
		var span = new Span<char>(value, length);
		From(ref span, length);
	}

	public void From(string value)
	{
		var span = value.AsSpan(0, SIZEOF);
		From(ref span, value.Length < SIZEOF ? _length : SIZEOF);
	}

	public void ToSpan(ref Span<char> target)
	{
		fixed (char* ptr = _storage)
		{
			var span = new Span<char>(ptr, _length);
			span.CopyTo(target);
		}
	}

	public override string ToString()
	{
		fixed (char* ptr = _storage)
		{
			var toReturn = new string(ptr, 0, _length);
			return toReturn;
		}
	}
}
