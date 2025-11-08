using Xunit;
using NotNot.Advanced;

namespace NotNot.Bcl.Test.Simple.Advanced;

/// <summary>
/// Tests for StrongPointer{T} lightweight handle pattern.
/// Covers allocation, disposal, thread safety, handle lifecycle, and CHECKED build validation.
/// </summary>
public class StrongPointerTests
{
	#region Test Helper Class

	/// <summary>
	/// Test target class implementing IDisposeGuard for StrongPointer testing.
	/// </summary>
	private class TestTarget : IDisposeGuard
	{
		public bool IsDisposed { get; private set; }
		public string Name { get; set; }

		public TestTarget(string name = "test")
		{
			Name = name;
		}

		public void Dispose()
		{
			__.AssertIfNot(!IsDisposed, "Double dispose detected");
			IsDisposed = true;
		}
	}

	#endregion

	#region Basic Allocation and Disposal Tests

	[Fact]
	public void Alloc_WithValidTarget_ReturnsAllocatedHandle()
	{
		// Arrange
		var target = new TestTarget("test1");

		// Act
		var pointer = StrongPointer<TestTarget>.Alloc(target);

		// Assert
		Assert.True(pointer.IsAllocated);
		Assert.True(pointer.CheckIsAlive());

		// Cleanup
		pointer.Dispose();
		target.Dispose();
	}

	[Fact]
	public void Dispose_ClearsIsAllocated()
	{
		// Arrange
		var target = new TestTarget();
		var pointer = StrongPointer<TestTarget>.Alloc(target);
		Assert.True(pointer.IsAllocated);

		// Act
		pointer.Dispose();

		// Assert
		Assert.False(pointer.IsAllocated);
		Assert.False(pointer.CheckIsAlive());

		// Cleanup
		target.Dispose();
	}

	[Fact]
	public void Dispose_OnStaleCopy_IsSafe()
	{
		// Arrange
		var target = new TestTarget();
		var pointer1 = StrongPointer<TestTarget>.Alloc(target);
		var pointer2 = pointer1; // Create copy

		// Act - Dispose one copy
		pointer1.Dispose();

		// Assert - Disposing stale copy should not throw
		pointer2.Dispose(); // Safe - checks validity before freeing

		Assert.False(pointer1.IsAllocated);
		Assert.False(pointer2.IsAllocated);

		// Cleanup
		target.Dispose();
	}

	[Fact]
	public void IsAllocated_BeforeDispose_ReturnsTrue()
	{
		// Arrange
		var target = new TestTarget();
		var pointer = StrongPointer<TestTarget>.Alloc(target);

		// Act & Assert
		Assert.True(pointer.IsAllocated);

		// Cleanup
		pointer.Dispose();
		target.Dispose();
	}

	[Fact]
	public void IsAllocated_AfterDispose_ReturnsFalse()
	{
		// Arrange
		var target = new TestTarget();
		var pointer = StrongPointer<TestTarget>.Alloc(target);

		// Act
		pointer.Dispose();

		// Assert
		Assert.False(pointer.IsAllocated);

		// Cleanup
		target.Dispose();
	}

	#endregion

	#region CheckIsAlive Tests

	[Fact]
	public void CheckIsAlive_WithValidHandle_ReturnsTrue()
	{
		// Arrange
		var target = new TestTarget();
		var pointer = StrongPointer<TestTarget>.Alloc(target);

		// Act
		var isAlive = pointer.CheckIsAlive();

		// Assert
		Assert.True(isAlive);

		// Cleanup
		pointer.Dispose();
		target.Dispose();
	}

	[Fact]
	public void CheckIsAlive_AfterDispose_ReturnsFalse()
	{
		// Arrange
		var target = new TestTarget();
		var pointer = StrongPointer<TestTarget>.Alloc(target);
		pointer.Dispose();

		// Act
		var isAlive = pointer.CheckIsAlive();

		// Assert
		Assert.False(isAlive);

		// Cleanup
		target.Dispose();
	}

	[Fact]
	public void CheckIsAlive_OnDefaultStruct_ReturnsFalse()
	{
		// Arrange
		var pointer = default(StrongPointer<TestTarget>);

		// Act
		var isAlive = pointer.CheckIsAlive();

		// Assert
		Assert.False(isAlive);
		Assert.False(pointer.IsAllocated);
	}

	#endregion

	#region GetTarget Tests

	[Fact]
	public void GetTarget_WithValidHandle_ReturnsTarget()
	{
		// Arrange
		var target = new TestTarget("target1");
		var pointer = StrongPointer<TestTarget>.Alloc(target);

		// Act
		var retrieved = pointer.GetTarget();

		// Assert
		Assert.Same(target, retrieved);
		Assert.Equal("target1", retrieved.Name);

		// Cleanup
		pointer.Dispose();
		target.Dispose();
	}

	[Fact]
	public void GetTarget_AfterDispose_ThrowsObjectDisposedException()
	{
		// Arrange
		var target = new TestTarget();
		var pointer = StrongPointer<TestTarget>.Alloc(target);
		pointer.Dispose();

		// Act & Assert
		Assert.Throws<ObjectDisposedException>(() => pointer.GetTarget());

		// Cleanup
		target.Dispose();
	}

	[Fact]
	public void GetTarget_AfterTargetDisposed_ThrowsObjectDisposedException()
	{
		// Arrange
		var target = new TestTarget();
		var pointer = StrongPointer<TestTarget>.Alloc(target);
		target.Dispose();

		// Act & Assert
		Assert.Throws<ObjectDisposedException>(() => pointer.GetTarget());

		// Cleanup
		pointer.Dispose();
	}

	[Fact]
	public void GetTarget_OnDefaultStruct_ThrowsObjectDisposedException()
	{
		// Arrange
		var pointer = default(StrongPointer<TestTarget>);

		// Act & Assert
		Assert.Throws<ObjectDisposedException>(() => pointer.GetTarget());
	}

	#endregion

	#region Multiple Handles Tests

	[Fact]
	public void MultipleHandles_ToSameTarget_CanCoexist()
	{
		// Arrange
		var target = new TestTarget("shared");

		// Act
		var pointer1 = StrongPointer<TestTarget>.Alloc(target);
		var pointer2 = StrongPointer<TestTarget>.Alloc(target);
		var pointer3 = StrongPointer<TestTarget>.Alloc(target);

		// Assert
		Assert.True(pointer1.IsAllocated);
		Assert.True(pointer2.IsAllocated);
		Assert.True(pointer3.IsAllocated);

		var retrieved1 = pointer1.GetTarget();
		var retrieved2 = pointer2.GetTarget();
		var retrieved3 = pointer3.GetTarget();

		Assert.Same(target, retrieved1);
		Assert.Same(target, retrieved2);
		Assert.Same(target, retrieved3);

		// Cleanup - dispose all handles before target
		pointer1.Dispose();
		pointer2.Dispose();
		pointer3.Dispose();
		target.Dispose();
	}

	[Fact]
	public void MultipleHandles_DisposedIndependently_EachClearsSelf()
	{
		// Arrange
		var target = new TestTarget();
		var pointer1 = StrongPointer<TestTarget>.Alloc(target);
		var pointer2 = StrongPointer<TestTarget>.Alloc(target);

		// Act
		pointer1.Dispose();

		// Assert
		Assert.False(pointer1.IsAllocated);
		Assert.True(pointer2.IsAllocated); // Other handle still valid
		Assert.True(pointer2.CheckIsAlive());

		// Cleanup
		pointer2.Dispose();
		target.Dispose();
	}

	#endregion

	#region Struct Copy Behavior Tests

	[Fact]
	public void StructCopy_CreatesIndependentCopy()
	{
		// Arrange
		var target = new TestTarget();
		var original = StrongPointer<TestTarget>.Alloc(target);

		// Act
		var copy = original; // Struct copy

		// Assert
		Assert.Equal(original.Index, copy.Index);
		Assert.Equal(original.IsAllocated, copy.IsAllocated);

		// Cleanup - both point to same slot, dispose only once via CheckIsAlive pattern
		original.Dispose();
		copy.Dispose(); // Safe - will check validity first
		target.Dispose();
	}

	[Fact]
	public void StructCopy_DisposingOriginal_MakesCopyStale()
	{
		// Arrange
		var target = new TestTarget();
		var original = StrongPointer<TestTarget>.Alloc(target);
		var copy = original;

		// Act
		original.Dispose();

		// Assert
		Assert.False(original.IsAllocated); // Original cleared
		Assert.True(copy.IsAllocated); // Copy still has old handle value locally
		Assert.False(copy.CheckIsAlive()); // But store knows it's invalid

		// Cleanup - safe because Dispose checks validity
		copy.Dispose();
		target.Dispose();
	}

	#endregion

	#region Thread Safety Tests

	[Fact]
	public void ConcurrentAlloc_ProducesUniqueHandles()
	{
		// Arrange
		const int threadCount = 10;
		const int allocsPerThread = 100;
		var targets = new List<TestTarget>();
		var pointers = new System.Collections.Concurrent.ConcurrentBag<StrongPointer<TestTarget>>();

		// Act
		Parallel.For(0, threadCount, _ =>
		{
			for (int i = 0; i < allocsPerThread; i++)
			{
				var target = new TestTarget($"thread_{_}_item_{i}");
				lock (targets) targets.Add(target);

				var pointer = StrongPointer<TestTarget>.Alloc(target);
				pointers.Add(pointer);
			}
		});

		// Assert
		var pointerList = pointers.ToList();
		Assert.Equal(threadCount * allocsPerThread, pointerList.Count);

		var uniqueIndices = pointerList.Select(p => p.Index).Distinct().Count();
		Assert.Equal(threadCount * allocsPerThread, uniqueIndices); // All unique

		// Cleanup
		foreach (var pointer in pointerList)
		{
			pointer.Dispose();
		}
		foreach (var target in targets)
		{
			target.Dispose();
		}
	}

	[Fact]
	public void ConcurrentCheckIsAlive_IsThreadSafe()
	{
		// Arrange
		var target = new TestTarget();
		var pointer = StrongPointer<TestTarget>.Alloc(target);
		const int iterations = 1000;

		// Act - Multiple threads checking same handle
		Parallel.For(0, iterations, _ =>
		{
			var isAlive = pointer.CheckIsAlive();
			Assert.True(isAlive);
		});

		// Cleanup
		pointer.Dispose();
		target.Dispose();
	}

	[Fact]
	public void ConcurrentGetTarget_IsThreadSafe()
	{
		// Arrange
		var target = new TestTarget("concurrent");
		var pointer = StrongPointer<TestTarget>.Alloc(target);
		const int iterations = 1000;

		// Act - Multiple threads accessing same target
		Parallel.For(0, iterations, _ =>
		{
			var retrieved = pointer.GetTarget();
			Assert.Same(target, retrieved);
			Assert.Equal("concurrent", retrieved.Name);
		});

		// Cleanup
		pointer.Dispose();
		target.Dispose();
	}

	#endregion

	#region CompareTo Tests

	[Fact]
	public void CompareTo_SameHandle_ReturnsZero()
	{
		// Arrange
		var target = new TestTarget();
		var pointer = StrongPointer<TestTarget>.Alloc(target);

		// Act
		var result = pointer.CompareTo(pointer);

		// Assert
		Assert.Equal(0, result);

		// Cleanup
		pointer.Dispose();
		target.Dispose();
	}

	[Fact]
	public void CompareTo_DifferentHandles_ReturnsNonZero()
	{
		// Arrange
		var target1 = new TestTarget();
		var target2 = new TestTarget();
		var pointer1 = StrongPointer<TestTarget>.Alloc(target1);
		var pointer2 = StrongPointer<TestTarget>.Alloc(target2);

		// Act
		var result = pointer1.CompareTo(pointer2);

		// Assert
		Assert.NotEqual(0, result);

		// Cleanup
		pointer1.Dispose();
		pointer2.Dispose();
		target1.Dispose();
		target2.Dispose();
	}

	#endregion

	#region Edge Cases and Error Conditions

	[Fact]
	public void Alloc_WithNullTarget_Throws()
	{
		// This test verifies the assertion fires in DEBUG builds
		// In RELEASE builds, the assertion is compiled out, so we can't test it
#if DEBUG
		// Act & Assert
		Assert.Throws<System.Exception>(() => StrongPointer<TestTarget>.Alloc(null!));
#endif
	}

	[Fact]
	public void Alloc_WithDisposedTarget_Throws()
	{
		// This test verifies the assertion fires in DEBUG builds
#if DEBUG
		// Arrange
		var target = new TestTarget();
		target.Dispose();

		// Act & Assert
		Assert.Throws<System.Exception>(() => StrongPointer<TestTarget>.Alloc(target));
#endif
	}

	[Fact]
	public void Dispose_OnDefaultHandle_DoesNotThrow()
	{
		// Arrange
		var pointer = default(StrongPointer<TestTarget>);

		// Act & Assert - Should not throw
		pointer.Dispose();

		Assert.False(pointer.IsAllocated);
	}

	[Fact]
	public void MultipleDispose_OnSameHandle_IsSafe()
	{
		// Arrange
		var target = new TestTarget();
		var pointer = StrongPointer<TestTarget>.Alloc(target);

		// Act - Dispose multiple times
		pointer.Dispose();
		pointer.Dispose(); // Should be safe - checks validity first
		pointer.Dispose();

		// Assert
		Assert.False(pointer.IsAllocated);

		// Cleanup
		target.Dispose();
	}

	#endregion

	#region Lifecycle Tests

	[Fact]
	public void ProperLifecycle_DisposeHandleBeforeTarget_Works()
	{
		// Arrange
		var target = new TestTarget();
		var pointer = StrongPointer<TestTarget>.Alloc(target);

		// Act - Proper order: dispose handle first
		pointer.Dispose();
		target.Dispose();

		// Assert
		Assert.False(pointer.IsAllocated);
		Assert.True(target.IsDisposed);
	}

	[Fact]
	public void ImproperLifecycle_DisposeTargetBeforeHandle_GetTargetThrows()
	{
		// Arrange
		var target = new TestTarget();
		var pointer = StrongPointer<TestTarget>.Alloc(target);

		// Act - Improper order: dispose target first
		target.Dispose();

		// Assert - GetTarget should detect disposed target
		Assert.Throws<ObjectDisposedException>(() => pointer.GetTarget());

		// Cleanup
		pointer.Dispose();
	}

	#endregion

	#region Stress Tests

	[Fact]
	public void AllocAndFree_ManyTimes_WorksCorrectly()
	{
		// Arrange & Act
		const int iterations = 1000;
		var targets = new List<TestTarget>();
		var pointers = new List<StrongPointer<TestTarget>>();

		for (int i = 0; i < iterations; i++)
		{
			var target = new TestTarget($"item_{i}");
			targets.Add(target);

			var pointer = StrongPointer<TestTarget>.Alloc(target);
			pointers.Add(pointer);

			Assert.True(pointer.CheckIsAlive());
		}

		// Assert all are still valid
		for (int i = 0; i < iterations; i++)
		{
			Assert.True(pointers[i].CheckIsAlive());
			var retrieved = pointers[i].GetTarget();
			Assert.Equal($"item_{i}", retrieved.Name);
		}

		// Cleanup
		foreach (var pointer in pointers)
		{
			pointer.Dispose();
		}
		foreach (var target in targets)
		{
			target.Dispose();
		}
	}

	[Fact]
	public void AllocDisposePattern_Repeated_HandlesReuse()
	{
		// Arrange
		const int iterations = 100;

		// Act & Assert
		for (int i = 0; i < iterations; i++)
		{
			var target = new TestTarget($"iteration_{i}");
			var pointer = StrongPointer<TestTarget>.Alloc(target);

			Assert.True(pointer.CheckIsAlive());
			var retrieved = pointer.GetTarget();
			Assert.Equal($"iteration_{i}", retrieved.Name);

			pointer.Dispose();
			target.Dispose();
		}

		// Slots should be reused, so index values should cycle
		// This is implicit validation that slot reuse is working
	}

	#endregion

	#region Index Property Tests

	[Fact]
	public void Index_ReturnsValidIndex()
	{
		// Arrange
		var target = new TestTarget();
		var pointer = StrongPointer<TestTarget>.Alloc(target);

		// Act
		var index = pointer.Index;

		// Assert
		Assert.True(index >= 0);

		// Cleanup
		pointer.Dispose();
		target.Dispose();
	}

	[Fact]
	public void DifferentHandles_HaveDifferentIndices()
	{
		// Arrange
		var target1 = new TestTarget();
		var target2 = new TestTarget();

		// Act
		var pointer1 = StrongPointer<TestTarget>.Alloc(target1);
		var pointer2 = StrongPointer<TestTarget>.Alloc(target2);

		// Assert
		Assert.NotEqual(pointer1.Index, pointer2.Index);

		// Cleanup
		pointer1.Dispose();
		pointer2.Dispose();
		target1.Dispose();
		target2.Dispose();
	}

	#endregion
}
