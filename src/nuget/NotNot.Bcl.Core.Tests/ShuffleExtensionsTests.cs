using System.Collections.Generic;
using NotNot.Collections.SpanLike;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

public class ShuffleExtensionsTests
{
	[Fact]
	public void SpanShuffle_Sorted_UsesCustomComparerForNonComparableItems()
	{
		var values = new[]
		{
			new SortItem(3, "c"),
			new SortItem(1, "a"),
			new SortItem(2, "b"),
		};

		values.AsSpan()._Shuffle(ShuffleType.Sorted, SortItemOrderComparer.Instance);

		Assert.Equal(new[] { 1, 2, 3 }, values.Select(v => v.Order).ToArray());
	}

	[Fact]
	public void SpanShuffle_ReverseSorted_SortsDescending()
	{
		var values = new[] { 3, 1, 4, 2 };

		values.AsSpan()._Shuffle(ShuffleType.ReverseSorted);

		Assert.Equal(new[] { 4, 3, 2, 1 }, values);
	}

	[Fact]
	public void SpanShuffle_BalancedDistribution_SpreadsDuplicatesWhenPossible()
	{
		var values = "AAABBBBCC".ToCharArray();
		var originalCounts = values.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());

		values.AsSpan()._Shuffle(ShuffleType.BalancedDistribution, randomInstance: new Random(1234));

		var shuffledCounts = values.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
		Assert.Equal(originalCounts.Count, shuffledCounts.Count);
		foreach (var (key, count) in originalCounts)
		{
			Assert.True(shuffledCounts.TryGetValue(key, out var shuffledCount));
			Assert.Equal(count, shuffledCount);
		}

		for (var i = 1; i < values.Length; i++)
		{
			Assert.NotEqual(values[i - 1], values[i]);
		}
	}

	[Fact]
	public void IListShuffle_Sorted_WorksForNonListImplementations()
	{
		var values = new[] { 4, 1, 3, 2 };
		IList<int> target = values;

		target._Shuffle(ShuffleType.Sorted);

		Assert.Equal(new[] { 1, 2, 3, 4 }, values);
	}

	private sealed record SortItem(int Order, string Name);

	private sealed class SortItemOrderComparer : IComparer<SortItem>
	{
		public static readonly SortItemOrderComparer Instance = new();

		public int Compare(SortItem? x, SortItem? y)
		{
			if (ReferenceEquals(x, y))
			{
				return 0;
			}

			if (x is null)
			{
				return -1;
			}

			if (y is null)
			{
				return 1;
			}

			return x.Order.CompareTo(y.Order);
		}
	}
}
