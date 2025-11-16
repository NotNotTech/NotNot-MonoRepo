using System;
using System.Collections.Generic;
using NotNot._internal;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Tests for StaticPool wrapper around ObjectPool
/// </summary>
public class StaticPoolTests
{
    [Fact]
    public void Get_ReturnsNewInstance_WhenPoolIsEmpty()
    {
        var item = StaticPool.Get<List<int>>();

        Assert.NotNull(item);
        Assert.Empty(item);
    }

    [Fact]
    public void Return_DoesNotClear_WhenUsingLegacyReturn()
    {
        var list = StaticPool.Get<List<int>>();
        list.Add(1);
        list.Add(2);

        // Legacy Return - should NOT clear
        StaticPool.Return(list);

        var reused = StaticPool.Get<List<int>>();
        Assert.Same(list, reused);
        Assert.Equal(2, reused.Count); // Should preserve contents
    }

    [Fact]
    public void ReturnNew_ClearsObject_WhenSkipAutoClearIsFalse()
    {
        var list = StaticPool.Get<List<int>>();
        list.Add(1);
        list.Add(2);

        // Return_New with default skipAutoClear=false
        StaticPool.Return_New(list);

        var reused = StaticPool.Get<List<int>>();
        Assert.Same(list, reused);
        Assert.Empty(reused); // Should be cleared
    }

    [Fact]
    public void ReturnNew_PreservesObject_WhenSkipAutoClearIsTrue()
    {
        var list = StaticPool.Get<List<int>>();
        list.Add(10);
        list.Add(20);

        // Return_New with skipAutoClear=true
        StaticPool.Return_New(list, skipAutoClear: true);

        var reused = StaticPool.Get<List<int>>();
        Assert.Same(list, reused);
        Assert.Equal(2, reused.Count); // Should preserve contents
    }

    [Fact]
    public void Rent_ReturnsRentedWrapper_ThatAutoReturns()
    {
        Dictionary<string, int> reusedDict;

        using (var rented = StaticPool.Rent<Dictionary<string, int>>())
        {
            rented.Value.Add("a", 1);
            rented.Value.Add("b", 2);
            Assert.Equal(2, rented.Value.Count);
        } // Auto-returns with clearing

        reusedDict = StaticPool.Get<Dictionary<string, int>>();
        Assert.Empty(reusedDict); // Should be cleared after disposal
    }

    [Fact]
    public void Rent_WithSkipAutoClear_PreservesContents()
    {
        Dictionary<string, int> reusedDict;

        using (var rented = StaticPool.Rent<Dictionary<string, int>>(skipAutoClear: true))
        {
            rented.Value.Add("x", 10);
            rented.Value.Add("y", 20);
        } // Auto-returns without clearing

        reusedDict = StaticPool.Get<Dictionary<string, int>>();
        Assert.Equal(2, reusedDict.Count); // Should preserve contents
    }

    [Fact]
    public void GetUsing_ReturnsUsingDisposableWrapper()
    {
        HashSet<string> reusedSet;

        using (var wrapper = StaticPool.GetUsing<HashSet<string>>(out var set))
        {
            set.Add("test1");
            set.Add("test2");
            Assert.Equal(2, set.Count);
            Assert.Same(set, wrapper.Item);
        } // Auto-returns with clearing

        reusedSet = StaticPool.Get<HashSet<string>>();
        Assert.Empty(reusedSet); // Should be cleared
    }

    [Fact]
    public void GetArray_ReturnsArrayOfCorrectLength()
    {
        var array = StaticPool.GetArray<int>(10);

        Assert.NotNull(array);
        Assert.Equal(10, array.Length);
    }

    [Fact]
    public void ReturnArray_ClearsArray_WhenPreserveContentsIsFalse()
    {
        var array = StaticPool.GetArray<int>(5);
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = i + 100;
        }

        StaticPool.ReturnArray(array, preserveContents: false);

        var reused = StaticPool.GetArray<int>(5);
        Assert.Same(array, reused);
        Assert.All(reused, item => Assert.Equal(0, item)); // Should be cleared
    }

    [Fact]
    public void ReturnArray_PreservesArray_WhenPreserveContentsIsTrue()
    {
        var array = StaticPool.GetArray<int>(3);
        array[0] = 10;
        array[1] = 20;
        array[2] = 30;

        StaticPool.ReturnArray(array, preserveContents: true);

        var reused = StaticPool.GetArray<int>(3);
        Assert.Same(array, reused);
        Assert.Equal(10, reused[0]);
        Assert.Equal(20, reused[1]);
        Assert.Equal(30, reused[2]);
    }

    [Fact]
    public void RentArray_ReturnsRentedArrayWrapper()
    {
        int[] reusedArray;

        using (var rented = StaticPool.RentArray<int>(4))
        {
            rented.Value[0] = 100;
            rented.Value[3] = 400;
            Assert.Equal(4, rented.Value.Length);
        } // Auto-returns with clearing

        reusedArray = StaticPool.GetArray<int>(4);
        Assert.All(reusedArray, item => Assert.Equal(0, item)); // Should be cleared
    }

    [Fact]
    public void RentArray_PreservesContents_WhenRequested()
    {
        int[] reusedArray;

        using (var rented = StaticPool.RentArray<int>(3, preserveContents: true))
        {
            rented.Value[0] = 111;
            rented.Value[1] = 222;
            rented.Value[2] = 333;
        } // Auto-returns without clearing

        reusedArray = StaticPool.GetArray<int>(3);
        Assert.Equal(111, reusedArray[0]);
        Assert.Equal(222, reusedArray[1]);
        Assert.Equal(333, reusedArray[2]);
    }

    [Fact]
    public void TypeWithoutClear_DoesNotThrow()
    {
        var obj = StaticPool.Get<ClassWithoutClear>();
        obj.Value = 42;

        // Should not throw even though there's no Clear method
        StaticPool.Return_New(obj);

        var reused = StaticPool.Get<ClassWithoutClear>();
        Assert.Same(obj, reused);
        Assert.Equal(42, reused.Value); // Value preserved since no Clear method
    }

    [Fact]
    public void StaticPool_IsGloballyShared()
    {
        // Since StaticPool is now static, this test verifies global sharing
        var list = StaticPool.Get<List<string>>();
        list.Add("shared");
        StaticPool.Return(list);

        var retrieved = StaticPool.Get<List<string>>();
        Assert.Same(list, retrieved); // Should be same instance from shared pool
        Assert.Single(retrieved);
        Assert.Equal("shared", retrieved[0]);
    }

    private class ClassWithoutClear
    {
        public int Value { get; set; }
    }
}