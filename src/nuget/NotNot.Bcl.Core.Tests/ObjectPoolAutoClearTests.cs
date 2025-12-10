using System;
using System.Collections.Generic;
using NotNot._internal;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Tests for ObjectPool and StaticPool auto-clear functionality.
/// Uses the Rent() pattern which is the current API.
/// </summary>
public class ObjectPoolAutoClearTests
{
    [Fact]
    public void ObjectPool_Rent_ClearsList()
    {
        var pool = new ObjectPool();
        List<int> originalRef;

        using (pool.Rent<List<int>>(out var list))
        {
            list.Add(1);
            list.Add(2);
            list.Add(3);
            originalRef = list;
        } // Auto-returns with clearing (default)

        using (pool.Rent<List<int>>(out var reused))
        {
            Assert.Same(originalRef, reused);
            Assert.Empty(reused);
        }
    }

    [Fact]
    public void ObjectPool_Rent_WithSkipAutoClear_PreservesState()
    {
        var pool = new ObjectPool();
        List<int> originalRef;

        using (pool.Rent<List<int>>(out var list, skipAutoClear: true))
        {
            list.Add(10);
            list.Add(20);
            originalRef = list;
        } // Auto-returns without clearing

        using (pool.Rent<List<int>>(out var reused))
        {
            Assert.Same(originalRef, reused);
            Assert.Equal(2, reused.Count);
        }
    }

    [Fact]
    public void ObjectPool_Rent_AutoClearsOnDispose()
    {
        var pool = new ObjectPool();
        Dictionary<string, int> originalRef;

        using (pool.Rent<Dictionary<string, int>>(out var item))
        {
            item.Add("a", 1);
            item.Add("b", 2);
            Assert.Equal(2, item.Count);
            originalRef = item;
        } // Auto-returns with clearing

        using (pool.Rent<Dictionary<string, int>>(out var reused))
        {
            Assert.Same(originalRef, reused);
            Assert.Empty(reused);
        }
    }

    [Fact]
    public void StaticPool_Rent_ClearsHashSet()
    {
        HashSet<string> originalRef;

        using (var rented = StaticPool.Rent<HashSet<string>>())
        {
            rented.Value.Add("test1");
            rented.Value.Add("test2");
            originalRef = rented.Value;
        } // Auto-returns with clearing

        using var rented2 = StaticPool.Rent<HashSet<string>>();
        Assert.Same(originalRef, rented2.Value);
        Assert.Empty(rented2.Value);
    }

    [Fact]
    public void ObjectPool_Rent_TypeWithoutClear_DoesNotThrow()
    {
        var pool = new ObjectPool();
        ClassWithoutClear originalRef;

        using (pool.Rent<ClassWithoutClear>(out var obj))
        {
            obj.Value = 42;
            originalRef = obj;
        } // Should not throw even though there's no Clear method

        using (pool.Rent<ClassWithoutClear>(out var reused))
        {
            Assert.Same(originalRef, reused);
            Assert.Equal(42, reused.Value); // Value preserved since no Clear method
        }
    }

    [Fact]
    public void ObjectPool_RentArray_ClearsArray()
    {
        var pool = new ObjectPool();
        int[] originalRef;

        using (pool.RentArray<int>(5, out var arr))
        {
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = i + 100;
            }
            originalRef = arr;
        } // Auto-returns with clearing (default)

        using (pool.RentArray<int>(5, out var reused))
        {
            Assert.Same(originalRef, reused);
            Assert.All(reused, item => Assert.Equal(0, item)); // Verify it was cleared
        }
    }

    private class ClassWithoutClear
    {
        public int Value { get; set; }
    }
}
