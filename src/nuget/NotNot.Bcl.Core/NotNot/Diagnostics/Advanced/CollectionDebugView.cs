// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Diagnostics;

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
		Items = collection?.ToArray();
	}

	public CollectionDebugView(Mem<T>? collection)
	{
		if (collection?.Length == 0)
		{
			Items = new T[0];
		}
		else
		{
			Items = collection?.DangerousGetArray().ToArray();
		}
	}

	public CollectionDebugView(ReadMem<T> collection)
	{
		//this.Items = new T[0];

		if (collection.Length == 0)
		{
			Items = new T[0];
		}
		else
		{
			Items = collection.DangerousGetArray().ToArray();
		}
	}

	[DebuggerBrowsable(DebuggerBrowsableState.Collapsed)]
	public T[]? Items { get; }

	public int Length { get; }
}
