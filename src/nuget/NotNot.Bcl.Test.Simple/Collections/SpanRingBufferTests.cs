using Xunit;
using NotNot.Collections.SpanLike;

namespace NotNot.Bcl.Test.Simple.Collections;

/// <summary>
/// Tests for SpanRingBuffer{T} stack-allocated circular buffer.
/// Covers basic operations, discard-oldest feature, wraparound edge cases, state consistency, and ref struct semantics.
/// </summary>
public class SpanRingBufferTests
{
    #region Basic Construction and Properties Tests

    [Fact]
    public void Constructor_WithSpan_InitializesEmpty()
    {
        // Arrange
        Span<int> buffer = stackalloc int[10];

        // Act
        var ring = new SpanRingBuffer<int>(buffer);

        // Assert
        Assert.Equal(10, ring.Capacity);
        Assert.Equal(0, ring.Count);
        Assert.True(ring.IsEmpty);
        Assert.False(ring.IsFull);
    }

    #endregion

    #region Basic Enqueue/Dequeue Tests

    [Fact]
    public void Enqueue_SingleItem_IncreasesCount()
    {
        // Arrange
        Span<int> buffer = stackalloc int[10];
        var ring = new SpanRingBuffer<int>(buffer);

        // Act
        ring.Enqueue(42);

        // Assert
        Assert.Equal(1, ring.Count);
        Assert.False(ring.IsEmpty);
        Assert.False(ring.IsFull);
    }

    [Fact]
    public void Dequeue_SingleItem_ReturnsCorrectValue()
    {
        // Arrange
        Span<int> buffer = stackalloc int[10];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(42);

        // Act
        var value = ring.Dequeue();

        // Assert
        Assert.Equal(42, value);
        Assert.Equal(0, ring.Count);
        Assert.True(ring.IsEmpty);
    }

    [Fact]
    public void EnqueueDequeue_MaintainsFifoOrder()
    {
        // Arrange
        Span<int> buffer = stackalloc int[10];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);

        // Act & Assert
        Assert.Equal(1, ring.Dequeue());
        Assert.Equal(2, ring.Dequeue());
        Assert.Equal(3, ring.Dequeue());
    }

    [Fact]
    public void Enqueue_FillToCapacity_SetsFull()
    {
        // Arrange
        Span<int> buffer = stackalloc int[3];
        var ring = new SpanRingBuffer<int>(buffer);

        // Act
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);

        // Assert
        Assert.Equal(3, ring.Count);
        Assert.True(ring.IsFull);
    }

    [Fact]
    public void Enqueue_BeyondCapacity_Throws()
    {
        // Arrange
        Span<int> buffer = stackalloc int[3];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);

        // Act & Assert - LoLoRoot in test mode throws DebugAssertException
        bool threw = false;
        try
        {
            ring.Enqueue(4);
        }
        catch (Exception)
        {
            threw = true;
        }
        Assert.True(threw, "Expected exception on overflow");
    }

    [Fact]
    public void TryEnqueue_BeyondCapacity_ReturnsFalse()
    {
        // Arrange
        Span<int> buffer = stackalloc int[3];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);

        // Act
        bool result = ring.TryEnqueue(4);

        // Assert
        Assert.False(result);
        Assert.Equal(3, ring.Count);
    }

    [Fact]
    public void Dequeue_EmptyBuffer_Throws()
    {
        // Arrange
        Span<int> buffer = stackalloc int[10];
        var ring = new SpanRingBuffer<int>(buffer);

        // Act & Assert - LoLoRoot in test mode throws DebugAssertException
        bool threw = false;
        try
        {
            ring.Dequeue();
        }
        catch (Exception)
        {
            threw = true;
        }
        Assert.True(threw, "Expected exception on underflow");
    }

    [Fact]
    public void TryDequeue_EmptyBuffer_ReturnsFalse()
    {
        // Arrange
        Span<int> buffer = stackalloc int[10];
        var ring = new SpanRingBuffer<int>(buffer);

        // Act
        bool result = ring.TryDequeue(out var value);

        // Assert
        Assert.False(result);
        Assert.Equal(default(int), value);
    }

    #endregion

    #region Discard-Oldest Feature Tests (Enqueue)

    [Fact]
    public void Enqueue_WithDiscardOldest_RemovesOldestItem()
    {
        // Arrange
        Span<int> buffer = stackalloc int[3];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);

        // Act
        ring.Enqueue(4, discardOldestOnFull: true);

        // Assert
        Assert.Equal(3, ring.Count);
        Assert.True(ring.IsFull);
        Assert.Equal(2, ring.Dequeue()); // 1 was discarded
        Assert.Equal(3, ring.Dequeue());
        Assert.Equal(4, ring.Dequeue());
    }

    [Fact]
    public void Enqueue_WithDiscardOldest_MultipleDiscards_MaintainsFifo()
    {
        // Arrange
        Span<int> buffer = stackalloc int[3];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);

        // Act - discard multiple times
        ring.Enqueue(4, discardOldestOnFull: true);
        ring.Enqueue(5, discardOldestOnFull: true);
        ring.Enqueue(6, discardOldestOnFull: true);

        // Assert
        Assert.Equal(3, ring.Count);
        Assert.Equal(4, ring.Dequeue()); // 1, 2, 3 were discarded
        Assert.Equal(5, ring.Dequeue());
        Assert.Equal(6, ring.Dequeue());
    }

    [Fact]
    public void Enqueue_WithDiscardOldest_OnNonFullBuffer_DoesNotDiscard()
    {
        // Arrange
        Span<int> buffer = stackalloc int[3];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);

        // Act
        ring.Enqueue(2, discardOldestOnFull: true);

        // Assert
        Assert.Equal(2, ring.Count);
        Assert.Equal(1, ring.Dequeue());
        Assert.Equal(2, ring.Dequeue());
    }

    [Fact]
    public void Enqueue_WithoutDiscardOldest_OnFullBuffer_Throws()
    {
        // Arrange
        Span<int> buffer = stackalloc int[3];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);

        // Act & Assert - default parameter should throw (DebugAssertException in test mode)
        bool threw = false;
        try
        {
            ring.Enqueue(4);
        }
        catch (Exception)
        {
            threw = true;
        }
        Assert.True(threw, "Expected exception on overflow");
    }

    [Fact]
    public void Enqueue_WithDiscardOldestFalse_OnFullBuffer_Throws()
    {
        // Arrange
        Span<int> buffer = stackalloc int[3];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);

        // Act & Assert - explicit false should throw (DebugAssertException in test mode)
        bool threw = false;
        try
        {
            ring.Enqueue(4, discardOldestOnFull: false);
        }
        catch (Exception)
        {
            threw = true;
        }
        Assert.True(threw, "Expected exception on overflow");
    }

    #endregion

    #region Discard-Oldest Feature Tests (TryEnqueue)

    [Fact]
    public void TryEnqueue_WithDiscardOldest_RemovesOldestItem()
    {
        // Arrange
        Span<int> buffer = stackalloc int[3];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);

        // Act
        bool result = ring.TryEnqueue(4, discardOldestOnFull: true);

        // Assert
        Assert.True(result);
        Assert.Equal(3, ring.Count);
        Assert.Equal(2, ring.Dequeue()); // 1 was discarded
        Assert.Equal(3, ring.Dequeue());
        Assert.Equal(4, ring.Dequeue());
    }

    [Fact]
    public void TryEnqueue_WithDiscardOldest_AlwaysSucceeds()
    {
        // Arrange
        Span<int> buffer = stackalloc int[3];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);

        // Act & Assert - multiple enqueues should all succeed
        Assert.True(ring.TryEnqueue(4, discardOldestOnFull: true));
        Assert.True(ring.TryEnqueue(5, discardOldestOnFull: true));
        Assert.True(ring.TryEnqueue(6, discardOldestOnFull: true));
        Assert.Equal(3, ring.Count);
    }

    [Fact]
    public void TryEnqueue_WithoutDiscardOldest_OnFullBuffer_ReturnsFalse()
    {
        // Arrange
        Span<int> buffer = stackalloc int[3];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);

        // Act
        bool result = ring.TryEnqueue(4);

        // Assert
        Assert.False(result);
        Assert.Equal(3, ring.Count);
        Assert.Equal(1, ring.Dequeue()); // Original items preserved
    }

    [Fact]
    public void TryEnqueue_WithDiscardOldestFalse_OnFullBuffer_ReturnsFalse()
    {
        // Arrange
        Span<int> buffer = stackalloc int[3];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);

        // Act
        bool result = ring.TryEnqueue(4, discardOldestOnFull: false);

        // Assert
        Assert.False(result);
        Assert.Equal(3, ring.Count);
    }

    #endregion

    #region Wraparound Edge Case Tests

    [Fact]
    public void Enqueue_WithDiscardOldest_AtWraparoundBoundary_WorksCorrectly()
    {
        // Arrange - fill buffer, dequeue some, enqueue to wraparound
        Span<int> buffer = stackalloc int[4];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);
        ring.Enqueue(4);
        ring.Dequeue(); // Remove 1
        ring.Dequeue(); // Remove 2
        ring.Enqueue(5); // Now at index 2
        ring.Enqueue(6); // Now at index 3 (tail wraps to 0)

        // Act - discard oldest (which is 3 at head position before wraparound)
        ring.Enqueue(7, discardOldestOnFull: true);

        // Assert
        Assert.Equal(4, ring.Count);
        Assert.Equal(4, ring.Dequeue()); // 3 was discarded
        Assert.Equal(5, ring.Dequeue());
        Assert.Equal(6, ring.Dequeue());
        Assert.Equal(7, ring.Dequeue());
    }

    [Fact]
    public void Enqueue_WithDiscardOldest_AfterMultipleWraparounds_MaintainsCorrectOrder()
    {
        // Arrange - simulate many wraparounds
        Span<int> buffer = stackalloc int[3];
        var ring = new SpanRingBuffer<int>(buffer);
        for (int i = 0; i < 100; i++)
        {
            ring.Enqueue(i, discardOldestOnFull: i >= 3);
        }

        // Assert - should have last 3 values
        Assert.Equal(3, ring.Count);
        Assert.Equal(97, ring.Dequeue());
        Assert.Equal(98, ring.Dequeue());
        Assert.Equal(99, ring.Dequeue());
    }

    #endregion

    #region State Consistency Tests

    [Fact]
    public void Count_AfterDiscardOldest_RemainsAtCapacity()
    {
        // Arrange
        Span<int> buffer = stackalloc int[5];
        var ring = new SpanRingBuffer<int>(buffer);
        for (int i = 0; i < 5; i++) ring.Enqueue(i);

        // Act
        ring.Enqueue(100, discardOldestOnFull: true);
        ring.Enqueue(101, discardOldestOnFull: true);

        // Assert
        Assert.Equal(5, ring.Count);
    }

    [Fact]
    public void IsFull_AfterDiscardOldest_RemainsTrue()
    {
        // Arrange
        Span<int> buffer = stackalloc int[5];
        var ring = new SpanRingBuffer<int>(buffer);
        for (int i = 0; i < 5; i++) ring.Enqueue(i);

        // Act
        ring.Enqueue(100, discardOldestOnFull: true);

        // Assert
        Assert.True(ring.IsFull);
    }

    [Fact]
    public void IsEmpty_AfterDiscardAndDequeueAll_ReturnsTrue()
    {
        // Arrange
        Span<int> buffer = stackalloc int[3];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);
        ring.Enqueue(4, discardOldestOnFull: true);

        // Act
        ring.Dequeue();
        ring.Dequeue();
        ring.Dequeue();

        // Assert
        Assert.True(ring.IsEmpty);
    }

    #endregion

    #region Additional Operations Tests

    [Fact]
    public void Peek_ReturnsOldestWithoutRemoving()
    {
        // Arrange
        Span<int> buffer = stackalloc int[10];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);

        // Act
        var value = ring.Peek();

        // Assert
        Assert.Equal(1, value);
        Assert.Equal(2, ring.Count);
    }

    [Fact]
    public void TryPeek_OnNonEmptyBuffer_ReturnsTrue()
    {
        // Arrange
        Span<int> buffer = stackalloc int[10];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(42);

        // Act
        bool result = ring.TryPeek(out var value);

        // Assert
        Assert.True(result);
        Assert.Equal(42, value);
    }

    [Fact]
    public void Clear_ResetsBufferToEmpty()
    {
        // Arrange
        Span<int> buffer = stackalloc int[10];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);

        // Act
        ring.Clear();

        // Assert
        Assert.Equal(0, ring.Count);
        Assert.True(ring.IsEmpty);
    }

    [Fact]
    public void Indexer_ReturnsItemsInFifoOrder()
    {
        // Arrange
        Span<int> buffer = stackalloc int[10];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(10);
        ring.Enqueue(20);
        ring.Enqueue(30);

        // Act & Assert
        Assert.Equal(10, ring[0]);
        Assert.Equal(20, ring[1]);
        Assert.Equal(30, ring[2]);
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        // Arrange
        Span<int> buffer = stackalloc int[10];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);

        // Act & Assert - LoLoRoot in test mode throws DebugAssertException
        bool threw = false;
        try
        {
            var _ = ring[5];
        }
        catch (Exception)
        {
            threw = true;
        }
        Assert.True(threw, "Expected exception for out of range access");
    }

    [Fact]
    public void CopyTo_LinearizesCircularBuffer()
    {
        // Arrange
        Span<int> buffer = stackalloc int[4];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);
        ring.Enqueue(2);
        ring.Enqueue(3);
        ring.Enqueue(4);
        ring.Dequeue(); // Remove 1
        ring.Enqueue(5); // Causes wraparound

        Span<int> dest = stackalloc int[4];

        // Act
        int copied = ring.CopyTo(dest);

        // Assert
        Assert.Equal(4, copied);
        Assert.Equal(2, dest[0]);
        Assert.Equal(3, dest[1]);
        Assert.Equal(4, dest[2]);
        Assert.Equal(5, dest[3]);
    }

    #endregion

    #region Ref Struct Semantics Tests

    [Fact]
    public void PassByValue_CreatesIndependentCopy()
    {
        // Arrange
        Span<int> buffer = stackalloc int[10];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);

        // Act - pass by value to helper method
        EnqueueByValue(ring, 2);

        // Assert - original ring unchanged (copy was modified)
        Assert.Equal(1, ring.Count);
    }

    [Fact]
    public void PassByRef_ModifiesOriginal()
    {
        // Arrange
        Span<int> buffer = stackalloc int[10];
        var ring = new SpanRingBuffer<int>(buffer);
        ring.Enqueue(1);

        // Act - pass by ref to helper method
        EnqueueByRef(ref ring, 2);

        // Assert - original ring modified
        Assert.Equal(2, ring.Count);
        Assert.Equal(1, ring.Dequeue());
        Assert.Equal(2, ring.Dequeue());
    }

    // Helper methods for ref struct semantics tests
    private static void EnqueueByValue(SpanRingBuffer<int> ring, int value)
    {
        ring.Enqueue(value); // Modifies copy, not original
    }

    private static void EnqueueByRef(ref SpanRingBuffer<int> ring, int value)
    {
        ring.Enqueue(value); // Modifies original
    }

    #endregion
}
