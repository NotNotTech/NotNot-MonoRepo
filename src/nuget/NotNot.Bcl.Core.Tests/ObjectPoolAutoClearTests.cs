using System;
using System.Collections.Generic;
using NotNot._internal;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Tests for ObjectPool and StaticPool auto-clear functionality.
/// Note: These tests intentionally use obsolete Get_Unsafe/Return methods to test the auto-clear behavior.
/// </summary>
#pragma warning disable CS0618 // Type or member is obsolete
public class ObjectPoolAutoClearTests
{
    [Fact]
    public void ObjectPool_Return_ClearsList()
    {
        var pool = new ObjectPool();
        var list = pool.Get_Unsafe<List<int>>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        pool.Return(list, skipAutoClear: false);

        var reused = pool.Get_Unsafe<List<int>>();
        Assert.Same(list, reused);
        Assert.Empty(reused);
    }

    [Fact]
    public void ObjectPool_Return_WithSkipAutoClear_PreservesState()
    {
        var pool = new ObjectPool();
        var list = pool.Get_Unsafe<List<int>>();
        list.Add(10);
        list.Add(20);

        pool.Return(list, skipAutoClear: true);

        var reused = pool.Get_Unsafe<List<int>>();
        Assert.Same(list, reused);
        Assert.Equal(2, reused.Count);
    }

    [Fact]
    public void ObjectPool_Rent_AutoClearsOnDispose()
    {
        var pool = new ObjectPool();
        Dictionary<string, int> dict;

        using (var rented = pool.Rent<Dictionary<string, int>>(out var item))
        {
            item.Add("a", 1);
            item.Add("b", 2);
            Assert.Equal(2, item.Count);
        }

        dict = pool.Get_Unsafe<Dictionary<string, int>>();
        Assert.Empty(dict);
    }

    [Fact]
    public void StaticPool_ReturnNew_ClearsHashSet()
    {
        var hashSet = StaticPool.Get<HashSet<string>>();
        hashSet.Add("test1");
        hashSet.Add("test2");

        StaticPool.Return_New(hashSet);

        var reusedSet = StaticPool.Get<HashSet<string>>();
        Assert.Same(hashSet, reusedSet);
        Assert.Empty(reusedSet);
    }

    [Fact]
    public void ObjectPool_Return_TypeWithoutClear_DoesNotThrow()
    {
        var pool = new ObjectPool();
        var obj = pool.Get_Unsafe<ClassWithoutClear>();
        obj.Value = 42;

        // Should not throw even though there's no Clear method
        pool.Return(obj, skipAutoClear: false);

        var reusedObj = pool.Get_Unsafe<ClassWithoutClear>();
        Assert.Same(obj, reusedObj);
        Assert.Equal(42, reusedObj.Value); // Value preserved since no Clear method
    }

    [Fact]
    public void ObjectPool_Return_ClearsArray()
    {
        var pool = new ObjectPool();
        var arr = pool.GetArray_Unsafe<int>(5);
        for (int i = 0; i < arr.Length; i++)
        {
            arr[i] = i + 100;
        }

        // Clear array before returning
        pool.Return(arr, skipAutoClear: false);

        // Get the same array back (since there's only one in the pool)
        var reusedArr = pool.GetArray_Unsafe<int>(5);

        // Verify it was cleared
        Assert.All(reusedArr, item => Assert.Equal(0, item));
    }

    private class ClassWithoutClear
    {
        public int Value { get; set; }
    }
}
