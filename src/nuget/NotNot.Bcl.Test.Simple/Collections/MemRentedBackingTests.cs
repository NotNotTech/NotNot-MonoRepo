using Xunit;
using NotNot.Collections.SpanLike;
using NotNot._internal;

namespace NotNot.Bcl.Test.Simple.Collections;

/// <summary>
/// Tests for Mem{T} with rented backing storage (RentedArray and Rented{List}).
/// Covers construction, wrapping, Span access, disposal lifecycle, DangerousGetArray, slicing, and pool integration patterns.
/// </summary>
public class MemRentedBackingTests
{
	#region Basic Construction and Wrapping Tests

	[Fact]
	public void WrapRented_ObjectPoolRentedArray_CreatesValidMem()
	{
		// Arrange
		var rented = ObjectPool.Rent<int>(10);
		rented.Value[0] = 42;
		rented.Value[1] = 99;

		// Act
		var mem = Mem.WrapRented(rented);

		// Assert
		Assert.Equal(10, mem.Length);
		Assert.Equal(42, mem.Span[0]);
		Assert.Equal(99, mem.Span[1]);
	}

	[Fact]
	public void WrapRented_StaticPoolRentedArray_CreatesValidMem()
	{
		// Arrange
		var rented = StaticPool.Rent<int>(10);
		rented.Value[0] = 42;
		rented.Value[1] = 99;

		// Act
		var mem = Mem.WrapRented(rented);

		// Assert
		Assert.Equal(10, mem.Length);
		Assert.Equal(42, mem.Span[0]);
		Assert.Equal(99, mem.Span[1]);
	}

	[Fact]
	public void WrapRented_ObjectPoolRentedList_CreatesValidMem()
	{
		// Arrange
		var rented = ObjectPool.Rent<List<int>>();
		rented.Value.Add(42);
		rented.Value.Add(99);

		// Act
		var mem = Mem.Wrap(rented);

		// Assert
		Assert.Equal(2, mem.Length);
		Assert.Equal(42, mem.Span[0]);
		Assert.Equal(99, mem.Span[1]);
	}

	[Fact]
	public void WrapRented_StaticPoolRentedList_CreatesValidMem()
	{
		// Arrange
		var rented = StaticPool.Rent<List<int>>();
		rented.Value.Add(42);
		rented.Value.Add(99);

		// Act
		var mem = Mem.WrapRented(rented);

		// Assert
		Assert.Equal(2, mem.Length);
		Assert.Equal(42, mem.Span[0]);
		Assert.Equal(99, mem.Span[1]);
	}

	#endregion

	#region Span Access Tests

	[Fact]
	public void Span_RentedArray_ReturnsValidSpan()
	{
		// Arrange
		var rented = ObjectPool.Rent<int>(5);
		for (int i = 0; i < 5; i++)
		{
			rented.Value[i] = i * 10;
		}
		var mem = Mem.WrapRented(rented);

		// Act
		var span = mem.Span;

		// Assert
		Assert.Equal(5, span.Length);
		for (int i = 0; i < 5; i++)
		{
			Assert.Equal(i * 10, span[i]);
		}
	}

	[Fact]
	public void Span_RentedList_ReturnsValidSpan()
	{
		// Arrange
		var rented = ObjectPool.Rent<List<int>>();
		for (int i = 0; i < 5; i++)
		{
			rented.Value.Add(i * 10);
		}
		var mem = Mem.Wrap(rented);

		// Act
		var span = mem.Span;

		// Assert
		Assert.Equal(5, span.Length);
		for (int i = 0; i < 5; i++)
		{
			Assert.Equal(i * 10, span[i]);
		}
	}

	[Fact]
	public void Span_RentedArrayModification_ReflectsInOriginal()
	{
		// Arrange
		var rented = ObjectPool.Rent<int>(5);
		var mem = Mem.WrapRented(rented);

		// Act
		mem.Span[0] = 42;
		mem.Span[1] = 99;

		// Assert
		Assert.Equal(42, rented.Value[0]);
		Assert.Equal(99, rented.Value[1]);
	}

	[Fact]
	public void Span_RentedListModification_ReflectsInOriginal()
	{
		// Arrange
		var rented = ObjectPool.Rent<List<int>>();
		rented.Value.Add(1);
		rented.Value.Add(2);
		var mem = Mem.Wrap(rented);

		// Act
		mem.Span[0] = 42;
		mem.Span[1] = 99;

		// Assert
		Assert.Equal(42, rented.Value[0]);
		Assert.Equal(99, rented.Value[1]);
	}

	#endregion

	#region Disposal Lifecycle Tests

	[Fact]
	public void Dispose_ObjectPoolRentedArray_ReturnsToPool()
	{
		// Arrange
		var rented = ObjectPool.Rent<int>(10);
		var mem = Mem.WrapRented(rented);

		// Act
		mem.Dispose();

		// Assert - rented should be returned to pool (no exception on double dispose)
		Assert.Throws<ObjectDisposedException>(() => rented.Value);
	}

	[Fact]
	public void Dispose_StaticPoolRentedArray_ReturnsToPool()
	{
		// Arrange
		var rented = StaticPool.Rent<int>(10);
		var mem = Mem.WrapRented(rented);

		// Act
		mem.Dispose();

		// Assert - rented should be returned to pool (no exception on double dispose)
		Assert.Throws<ObjectDisposedException>(() => rented.Value);
	}

	[Fact]
	public void Dispose_ObjectPoolRentedList_ReturnsToPool()
	{
		// Arrange
		var rented = ObjectPool.Rent<List<int>>();
		rented.Value.Add(42);
		var mem = Mem.Wrap(rented);

		// Act
		mem.Dispose();

		// Assert - rented should be returned to pool (no exception on double dispose)
		Assert.Throws<ObjectDisposedException>(() => rented.Value);
	}

	[Fact]
	public void Dispose_StaticPoolRentedList_ReturnsToPool()
	{
		// Arrange
		var rented = StaticPool.Rent<List<int>>();
		rented.Value.Add(42);
		var mem = Mem.WrapRented(rented);

		// Act
		mem.Dispose();

		// Assert - rented should be returned to pool (no exception on double dispose)
		Assert.Throws<ObjectDisposedException>(() => rented.Value);
	}

	[Fact]
	public void Dispose_MultipleTimes_DoesNotThrow()
	{
		// Arrange
		var rented = ObjectPool.Rent<int>(10);
		var mem = Mem.WrapRented(rented);

		// Act & Assert
		mem.Dispose();
		mem.Dispose(); // Second dispose should not throw
	}

	[Fact]
	public void Dispose_Slice_DoesNotReturnToPool()
	{
		// Arrange
		var rented = ObjectPool.Rent<int>(10);
		rented.Value[5] = 42;
		var mem = Mem.WrapRented(rented);
		var slice = mem.Slice(2, 5);

		// Act
		slice.Dispose();

		// Assert - slice should not dispose parent, so parent's span still accessible
		Assert.Equal(42, mem.Span[5]);
		Assert.Equal(42, rented.Value[5]);
	}

	[Fact]
	public void UsingPattern_RentedArray_AutomaticallyReturnsToPool()
	{
		// Arrange
		var rented = ObjectPool.Rent<int>(10);

		// Act
		using (var mem = Mem.WrapRented(rented))
		{
			mem.Span[0] = 42;
		}

		// Assert - rented should be returned to pool
		Assert.Throws<ObjectDisposedException>(() => rented.Value);
	}

	[Fact]
	public void UsingPattern_RentedList_AutomaticallyReturnsToPool()
	{
		// Arrange
		var rented = ObjectPool.Rent<List<int>>();
		rented.Value.Add(42);

		// Act
		using (var mem = Mem.Wrap(rented))
		{
			mem.Span[0] = 99;
		}

		// Assert - rented should be returned to pool
		Assert.Throws<ObjectDisposedException>(() => rented.Value);
	}

	#endregion

	#region DangerousGetArray Tests

	[Fact]
	public void DangerousGetArray_RentedArray_ReturnsValidArraySegment()
	{
		// Arrange
		var rented = ObjectPool.Rent<int>(10);
		for (int i = 0; i < 10; i++)
		{
			rented.Value[i] = i;
		}
		var mem = Mem.WrapRented(rented);

		// Act
		var segment = mem.DangerousGetArray();

		// Assert
		Assert.NotNull(segment.Array);
		Assert.Equal(0, segment.Offset);
		Assert.Equal(10, segment.Count);
		Assert.Equal(5, segment.Array[5]);
	}

	[Fact]
	public void DangerousGetArray_RentedList_ReturnsValidArraySegment()
	{
		// Arrange
		var rented = ObjectPool.Rent<List<int>>();
		for (int i = 0; i < 10; i++)
		{
			rented.Value.Add(i);
		}
		var mem = Mem.Wrap(rented);

		// Act
		var segment = mem.DangerousGetArray();

		// Assert
		Assert.NotNull(segment.Array);
		Assert.Equal(0, segment.Offset);
		Assert.Equal(10, segment.Count);
		Assert.Equal(5, segment.Array[5]);
	}

	[Fact]
	public void DangerousGetArray_SlicedRentedArray_ReturnsCorrectSegment()
	{
		// Arrange
		var rented = ObjectPool.Rent<int>(10);
		for (int i = 0; i < 10; i++)
		{
			rented.Value[i] = i;
		}
		var mem = Mem.WrapRented(rented);
		var slice = mem.Slice(2, 5);

		// Act
		var segment = slice.DangerousGetArray();

		// Assert
		Assert.NotNull(segment.Array);
		Assert.Equal(2, segment.Offset);
		Assert.Equal(5, segment.Count);
		Assert.Equal(2, segment.Array[segment.Offset]);
		Assert.Equal(6, segment.Array[segment.Offset + 4]);
	}

	[Fact]
	public void DangerousGetArray_SlicedRentedList_ReturnsCorrectSegment()
	{
		// Arrange
		var rented = ObjectPool.Rent<List<int>>();
		for (int i = 0; i < 10; i++)
		{
			rented.Value.Add(i);
		}
		var mem = Mem.Wrap(rented);
		var slice = mem.Slice(2, 5);

		// Act
		var segment = slice.DangerousGetArray();

		// Assert
		Assert.NotNull(segment.Array);
		Assert.Equal(2, segment.Offset);
		Assert.Equal(5, segment.Count);
		Assert.Equal(2, segment.Array[segment.Offset]);
		Assert.Equal(6, segment.Array[segment.Offset + 4]);
	}

	#endregion

	#region Slicing and Segments Tests

	[Fact]
	public void Slice_RentedArray_CreatesValidSlice()
	{
		// Arrange
		var rented = ObjectPool.Rent<int>(10);
		for (int i = 0; i < 10; i++)
		{
			rented.Value[i] = i;
		}
		var mem = Mem.WrapRented(rented);

		// Act
		var slice = mem.Slice(2, 5);

		// Assert
		Assert.Equal(5, slice.Length);
		Assert.Equal(2, slice.Span[0]);
		Assert.Equal(6, slice.Span[4]);
	}

	[Fact]
	public void Slice_RentedList_CreatesValidSlice()
	{
		// Arrange
		var rented = ObjectPool.Rent<List<int>>();
		for (int i = 0; i < 10; i++)
		{
			rented.Value.Add(i);
		}
		var mem = Mem.Wrap(rented);

		// Act
		var slice = mem.Slice(2, 5);

		// Assert
		Assert.Equal(5, slice.Length);
		Assert.Equal(2, slice.Span[0]);
		Assert.Equal(6, slice.Span[4]);
	}

	[Fact]
	public void Slice_Modification_ReflectsInParent()
	{
		// Arrange
		var rented = ObjectPool.Rent<int>(10);
		var mem = Mem.WrapRented(rented);
		var slice = mem.Slice(2, 5);

		// Act
		slice.Span[0] = 42;

		// Assert
		Assert.Equal(42, mem.Span[2]);
		Assert.Equal(42, rented.Value[2]);
	}

	[Fact]
	public void Slice_NestedSlicing_WorksCorrectly()
	{
		// Arrange
		var rented = ObjectPool.Rent<int>(10);
		for (int i = 0; i < 10; i++)
		{
			rented.Value[i] = i * 10;
		}
		var mem = Mem.WrapRented(rented);
		var slice1 = mem.Slice(2, 6);
		var slice2 = slice1.Slice(1, 3);

		// Act & Assert
		Assert.Equal(3, slice2.Length);
		Assert.Equal(30, slice2.Span[0]); // Original index 3
		Assert.Equal(40, slice2.Span[1]); // Original index 4
		Assert.Equal(50, slice2.Span[2]); // Original index 5
	}

	#endregion

	#region Integration with Pool Usage Patterns Tests

	[Fact]
	public void RentUseReturn_ObjectPool_CompleteCycle()
	{
		// Arrange & Act
		using (var mem = Mem.WrapRented(ObjectPool.Rent<int>(10)))
		{
			mem.Span[0] = 42;
			Assert.Equal(42, mem.Span[0]);
		}

		// Assert - implicit: no exception on dispose, resource returned
	}

	[Fact]
	public void RentUseReturn_StaticPool_CompleteCycle()
	{
		// Arrange & Act
		using (var mem = Mem.WrapRented(StaticPool.Rent<int>(10)))
		{
			mem.Span[0] = 42;
			Assert.Equal(42, mem.Span[0]);
		}

		// Assert - implicit: no exception on dispose, resource returned
	}

	[Fact]
	public void MultipleSequentialRents_ReusePooledResources()
	{
		// Arrange & Act - rent, use, return multiple times
		for (int iteration = 0; iteration < 5; iteration++)
		{
			using (var mem = Mem.WrapRented(ObjectPool.Rent<int>(10)))
			{
				mem.Span[0] = iteration;
				Assert.Equal(iteration, mem.Span[0]);
			}
		}

		// Assert - implicit: all iterations complete without issue
	}

	[Fact]
	public void ConcurrentUsage_MultipleIndependentRents()
	{
		// Arrange
		var rented1 = ObjectPool.Rent<int>(10);
		var rented2 = ObjectPool.Rent<int>(10);
		var mem1 = Mem.WrapRented(rented1);
		var mem2 = Mem.WrapRented(rented2);

		// Act
		mem1.Span[0] = 11;
		mem2.Span[0] = 22;

		// Assert
		Assert.Equal(11, mem1.Span[0]);
		Assert.Equal(22, mem2.Span[0]);
		Assert.NotEqual(mem1.Span[0], mem2.Span[0]);

		// Cleanup
		mem1.Dispose();
		mem2.Dispose();
	}

	[Fact]
	public void PassMemToMethod_DisposalOwnership()
	{
		// Arrange
		var rented = ObjectPool.Rent<int>(10);
		var mem = Mem.WrapRented(rented);

		// Act
		ProcessMem(mem);

		// Assert - mem should still be accessible (method doesn't own disposal)
		mem.Span[0] = 42;
		Assert.Equal(42, mem.Span[0]);

		// Cleanup
		mem.Dispose();
	}

	private void ProcessMem(Mem<int> mem)
	{
		mem.Span[5] = 99;
	}

	#endregion
}
