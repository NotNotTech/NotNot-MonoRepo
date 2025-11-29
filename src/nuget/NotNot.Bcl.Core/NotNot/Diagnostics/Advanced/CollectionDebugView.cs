// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Diagnostics;
using NotNot.Collections.SpanLike;

namespace NotNot.Diagnostics.Advanced;

/// <summary>
///    can be applied as part of [<see cref="DebuggerTypeProxyAttribute" />] to have a nice debug view of collections.
///    <para>
///       For Example:
///       <example>
///          [DebuggerTypeProxy(typeof(CollectionDebugView{}))]
///          [DebuggerDisplay("{ToString(),raw}")]
///          public sealed class MemoryOwner_Custom{T} : IMemoryOwner{T}
///       </example>
///    </para>
/// </summary>
public sealed class CollectionDebugView<T>
{
	public CollectionDebugView(IEnumerable<T>? collection)
	{
		_collection = Mem.Wrap(collection.ToArray());
		//Items = collection?.ToArray();
	}

	//public CollectionDebugView(Mem<T>? collection)
	//{
	//	if (collection?.Length == 0)
	//	{
	//		Items = new T[0];
	//	}
	//	else
	//	{
	//		Items = collection?.DangerousGetArray().ToArray();
	//	}
	//}

	public CollectionDebugView(Mem<T> collection)
	{
		//this.Items = new T[0];
		_collection = collection;
		//if (collection.Length == 0)
		//{
		//	Items = new T[0];
		//}
		//else
		//{
		//	Items = collection.DangerousGetArray().ToArray();
		//}
	}
	[DebuggerBrowsable(DebuggerBrowsableState.Never)]
	private Mem<T> _collection;

	[DebuggerBrowsable(DebuggerBrowsableState.Collapsed)]
	//public T[]? Items { get; }
	public Span<T> Items => _collection.GetSpan();

	public int Length { get; }
}
