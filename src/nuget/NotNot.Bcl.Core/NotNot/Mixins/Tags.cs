// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System;
using System.Collections.Generic;
using System.Text;

namespace NotNot.Mixins;

public interface ITags
{
	TValue _GetTagOrDefault<TValue>(object key, TValue defaultValue = default);
	void _SetTag<TValue>(object key, TValue value);
	bool _TryGetTag<TValue>(object key, out TValue value);
	bool _TryRemoveTag(object key);
}

public class Tags : ITags
{

	/// <summary>
	/// arbitrary tags associated with this node.  (user defined key/value pairs)
	/// </summary>
	public Dictionary<object, object?> _tags;
	/// <summary>
	/// accessor for arbitrary tags associated with this node.  (user defined key/value pairs)
	/// </summary>
	/// <typeparam name="TValue"></typeparam>
	/// <param name="key"></param>
	/// <param name="value"></param>
	/// <returns></returns>
	public bool _TryGetTag<TValue>(object key, out TValue value)
	{
		if (_tags is null)
		{
			value = default!;
			return false;
		}
		if (_tags.TryGetValue(key, out var objValue))
		{
			value = (TValue)objValue;
			return true;
		}
		value = default!;
		return false;
	}
	/// <summary>
	/// accessor for arbitrary tags associated with this node.  (user defined key/value pairs)
	/// </summary>
	/// <typeparam name="TValue"></typeparam>
	/// <param name="key"></param>
	/// <param name="defaultValue"></param>
	/// <returns></returns>
	public TValue _GetTagOrDefault<TValue>(object key, TValue defaultValue = default!)
	{
		if (_TryGetTag<TValue>(key, out var value))
		{
			return value;
		}
		return defaultValue;
	}

	public TValue _GetOrCreateTag<TValue>(object key, TValue defaultValue)
	{
		if (_TryGetTag<TValue>(key, out var value))
		{
			return value;
		}
		_SetTag<TValue>(key, defaultValue);
		return defaultValue;
	}
	//public TValue _GetOrCreateTag<TValue>(object key) where TValue : new()
	//{
	//	if (_TryGetTag<TValue>(key, out var value))
	//	{
	//		return value;
	//	}
	//	var defaultValue = new TValue();
	//	_SetTag<TValue>(key, defaultValue);
	//	return defaultValue;
	//}

	public void TestHello()
	{
		//noop

	}

	public TValue _GetOrCreateTag<TValue>(object key, Func<TValue> createDefaultValue)
	{
		if (_TryGetTag<TValue>(key, out var value))
		{
			return value;
		}
		var defaultValue = createDefaultValue();
		_SetTag<TValue>(key, defaultValue);
		return defaultValue;
	}

	/// <summary>
	/// accessor for arbitrary tags associated with this node.  (user defined key/value pairs)
	/// </summary>
	/// <typeparam name="TValue"></typeparam>
	/// <param name="key"></param>
	/// <param name="value"></param>
	public void _SetTag<TValue>(object key, TValue value)
	{
		if (value is null)
		{
			throw __.Throw("cannot set null tag value.  use .TryRemoeTag() instead");
		}
		if (_tags is null)
		{
			_tags = new();
		}
		_tags[key] = value;
	}
	/// <summary>
	/// accessor for arbitrary tags associated with this node.  (user defined key/value pairs)
	/// </summary>
	/// <param name="key"></param>
	/// <returns></returns>
	public bool _TryRemoveTag(object key)
	{
		if (_tags is null)
		{
			return false;
		}
		var toReturn = _tags.Remove(key);
		if (_tags.Count == 0)
		{
			_tags = null!;
		}
		return toReturn;
	}

}
