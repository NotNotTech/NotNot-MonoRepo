using Xunit;
using NotNot.Collections;

namespace NotNot.Bcl.Test.Simple.Collections;

/// <summary>
/// Tests for RingBuffer{T} circular buffer.
/// Covers basic operations, discard-oldest feature, wraparound edge cases, and state consistency.
/// </summary>
public class RingBufferTests
{
    #region Basic Construction and Properties Tests

    [Fact]
    public void Constructor_WithCapacity_InitializesEmpty()
    {
        // Arrange & Act
        var ring = new RingBuffer<int>(10);

        // Assert
        Assert.Equal(10, ring.Capacity);
        Assert.Equal(0, ring.Count);
        Assert.True(ring.IsEmpty);
        Assert.False(ring.IsFull);
    }

    [Fact]
    public void Constructor_WithArray_InitializesEmpty()
    {
        // Arrange
        var buffer = new int[10];

        // Act
        var ring = new RingBuffer<int>(buffer);

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
        var ring = new RingBuffer<int>(10);

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
        var ring = new RingBuffer<int>(10);
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
        var ring = new RingBuffer<int>(10);
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
        var ring = new RingBuffer<int>(3);

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
        var ring = new RingBuffer<int>(3);
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
        var ring = new RingBuffer<int>(3);
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
        var ring = new RingBuffer<int>(10);

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
        var ring = new RingBuffer<int>(10);

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
        var ring = new RingBuffer<int>(3);
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
        var ring = new RingBuffer<int>(3);
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
        var ring = new RingBuffer<int>(3);
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
        var ring = new RingBuffer<int>(3);
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
        var ring = new RingBuffer<int>(3);
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
        var ring = new RingBuffer<int>(3);
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
        var ring = new RingBuffer<int>(3);
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
        var ring = new RingBuffer<int>(3);
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
        var ring = new RingBuffer<int>(3);
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
        var ring = new RingBuffer<int>(4);
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
        var ring = new RingBuffer<int>(3);
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
        var ring = new RingBuffer<int>(5);
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
        var ring = new RingBuffer<int>(5);
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
        var ring = new RingBuffer<int>(3);
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
        var ring = new RingBuffer<int>(10);
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
        var ring = new RingBuffer<int>(10);
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
        var ring = new RingBuffer<int>(10);
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
        var ring = new RingBuffer<int>(10);
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
        var ring = new RingBuffer<int>(10);
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

    #endregion

    #region String Type Tests

    [Fact]
    public void RingBuffer_WithStrings_WorksCorrectly()
    {
        // Arrange
        var ring = new RingBuffer<string>(3);

        // Act
        ring.Enqueue("first");
        ring.Enqueue("second");
        ring.Enqueue("third");
        ring.Enqueue("fourth", discardOldestOnFull: true);

        // Assert
        Assert.Equal("second", ring.Dequeue());
        Assert.Equal("third", ring.Dequeue());
        Assert.Equal("fourth", ring.Dequeue());
    }

    #endregion
}
