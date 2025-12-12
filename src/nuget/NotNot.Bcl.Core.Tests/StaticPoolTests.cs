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
    public void Rent_ReturnsNewInstance_WhenPoolIsEmpty()
    {
        using var rented = StaticPool.Rent<List<int>>();
        Assert.NotNull(rented.Value);
        Assert.Empty(rented.Value);
    }

    [Fact]
    public void Rent_WithSkipAutoClear_PreservesContents()
    {
        List<int> reusedList;
        List<int> originalRef;

        using (var rented = StaticPool.Rent<List<int>>(skipAutoClear: true))
        {
            rented.Value.Add(1);
            rented.Value.Add(2);
            originalRef = rented.Value;
        } // Auto-returns without clearing

        using var rented2 = StaticPool.Rent<List<int>>();
        reusedList = rented2.Value;
        Assert.Same(originalRef, reusedList);
        Assert.Equal(2, reusedList.Count); // Should preserve contents
    }

    [Fact]
    public void Rent_WithDefaultClear_ClearsObject()
    {
        List<int> originalRef;

        using (var rented = StaticPool.Rent<List<int>>())
        {
            rented.Value.Add(1);
            rented.Value.Add(2);
            originalRef = rented.Value;
        } // Auto-returns with clearing

        using var rented2 = StaticPool.Rent<List<int>>();
        Assert.Same(originalRef, rented2.Value);
        Assert.Empty(rented2.Value); // Should be cleared
    }

    [Fact]
    public void Rent_ReturnsRentedWrapper_ThatAutoReturns()
    {
        Dictionary<string, int> originalRef;

        using (var rented = StaticPool.Rent<Dictionary<string, int>>())
        {
            rented.Value.Add("a", 1);
            rented.Value.Add("b", 2);
            Assert.Equal(2, rented.Value.Count);
            originalRef = rented.Value;
        } // Auto-returns with clearing

        using var rented2 = StaticPool.Rent<Dictionary<string, int>>();
        Assert.Same(originalRef, rented2.Value);
        Assert.Empty(rented2.Value); // Should be cleared after disposal
    }

    [Fact]
    public void Rent_ClearsHashSet()
    {
        HashSet<string> originalRef;

        using (var rented = StaticPool.Rent<HashSet<string>>())
        {
            rented.Value.Add("test1");
            rented.Value.Add("test2");
            Assert.Equal(2, rented.Value.Count);
            originalRef = rented.Value;
        } // Auto-returns with clearing

        using var rented2 = StaticPool.Rent<HashSet<string>>();
        Assert.Same(originalRef, rented2.Value);
        Assert.Empty(rented2.Value); // Should be cleared
    }

    [Fact]
    public void RentArray_ReturnsArrayOfCorrectLength()
    {
        using var rented = StaticPool.RentArray<int>(10);
        Assert.NotNull(rented.Value);
        Assert.Equal(10, rented.Value.Length);
    }

    [Fact]
    public void RentArray_ClearsArray_WhenPreserveContentsIsFalse()
    {
        int[] originalRef;

        using (var rented = StaticPool.RentArray<int>(5, preserveContents: false))
        {
            for (int i = 0; i < rented.Value.Length; i++)
            {
                rented.Value[i] = i + 100;
            }
            originalRef = rented.Value;
        } // Auto-returns with clearing

        using var rented2 = StaticPool.RentArray<int>(5);
        Assert.Same(originalRef, rented2.Value);
        Assert.All(rented2.Value, item => Assert.Equal(0, item)); // Should be cleared
    }

    [Fact]
    public void RentArray_PreservesArray_WhenPreserveContentsIsTrue()
    {
        int[] originalRef;

        using (var rented = StaticPool.RentArray<int>(3, preserveContents: true))
        {
            rented.Value[0] = 10;
            rented.Value[1] = 20;
            rented.Value[2] = 30;
            originalRef = rented.Value;
        } // Auto-returns without clearing

        using var rented2 = StaticPool.RentArray<int>(3);
        Assert.Same(originalRef, rented2.Value);
        Assert.Equal(10, rented2.Value[0]);
        Assert.Equal(20, rented2.Value[1]);
        Assert.Equal(30, rented2.Value[2]);
    }

    [Fact]
    public void RentArray_ReturnsRentedArrayWrapper()
    {
        int[] originalRef;

        using (var rented = StaticPool.RentArray<int>(4))
        {
            rented.Value[0] = 100;
            rented.Value[3] = 400;
            Assert.Equal(4, rented.Value.Length);
            originalRef = rented.Value;
        } // Auto-returns with clearing

        using var rented2 = StaticPool.RentArray<int>(4);
        Assert.Same(originalRef, rented2.Value);
        Assert.All(rented2.Value, item => Assert.Equal(0, item)); // Should be cleared
    }

    [Fact]
    public void TypeWithoutClear_DoesNotThrow()
    {
        ClassWithoutClear originalRef;

        using (var rented = StaticPool.Rent<ClassWithoutClear>())
        {
            rented.Value.Value = 42;
            originalRef = rented.Value;
        } // Should not throw even though there's no Clear method

        using var rented2 = StaticPool.Rent<ClassWithoutClear>();
        Assert.Same(originalRef, rented2.Value);
        Assert.Equal(42, rented2.Value.Value); // Value preserved since no Clear method
    }

    [Fact]
    public void StaticPool_IsGloballyShared()
    {
        // Since StaticPool is static, this test verifies global sharing
        List<string> originalRef;

        using (var rented = StaticPool.Rent<List<string>>(skipAutoClear: true))
        {
            rented.Value.Add("shared");
            originalRef = rented.Value;
        }

        using var rented2 = StaticPool.Rent<List<string>>();
        Assert.Same(originalRef, rented2.Value); // Should be same instance from shared pool
        Assert.Single(rented2.Value);
        Assert.Equal("shared", rented2.Value[0]);
    }

    [Fact]
    public void Rent_UseAfterReturn_DetectsVersionMismatch()
    {
        // Rent object and keep both the wrapper and the object reference
        var rental1 = StaticPool.Rent<List<int>>();
        var obj = rental1.Value;
        obj.Add(100);

        // Return to pool (version N removed from tracking)
        rental1.Dispose();

        // Rent again - should get same object with NEW version (N+1)
        var rental2 = StaticPool.Rent<List<int>>();
        Assert.Same(obj, rental2.Value); // Same object instance

        // Try to dispose original rental again - should detect version mismatch
        // In DEBUG/CHECKED builds, this throws assertion exception
        // In RELEASE builds, this path doesn't exist (no version tracking)
#if CHECKED
        try
        {
            rental1.Dispose(); // This will detect version mismatch (expecting N, but object has N+1)
            Assert.Fail("Expected version mismatch assertion");
        }
        catch (Exception ex)
        {
            // Expected in CHECKED builds - verify it's the right assertion
            Assert.Contains("Use-after-return detected", ex.Message);
        }
#endif

        // Clean up rental2
        rental2.Dispose();
    }

    [Fact]
    public void RentArray_UseAfterReturn_DetectsVersionMismatch()
    {
        // Rent array and keep both the wrapper and the array reference
        var rental1 = StaticPool.RentArray<int>(5);
        var arr = rental1.Value;
        arr[0] = 200;

        // Return to pool (version N removed from tracking)
        rental1.Dispose();

        // Rent again - should get same array with NEW version (N+1)
        var rental2 = StaticPool.RentArray<int>(5);
        Assert.Same(arr, rental2.Value); // Same array instance

        // Try to dispose original rental again - should detect version mismatch
#if CHECKED
        try
        {
            rental1.Dispose(); // This will detect version mismatch
            Assert.Fail("Expected version mismatch assertion");
        }
        catch (Exception ex)
        {
            // Expected in CHECKED builds - verify it's the right assertion
            Assert.Contains("Use-after-return detected", ex.Message);
        }
#endif

        // Clean up rental2
        rental2.Dispose();
    }

    private class ClassWithoutClear
    {
        public int Value { get; set; }
    }
}
