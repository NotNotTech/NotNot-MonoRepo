using System.Threading;
using System.Threading.Tasks;
using NotNot.Collections;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

public class RefSlotStoreTests
{
    [Fact]
    public async Task SafeCapacityReaders_WaitForOwningLock()
    {
        using var store = new RefSlotStore<int>(initialCapacity: 4);
        _ = store.AllocSlot();

        Task<int> countRead;
        Task<int> capacityRead;
        using var countStarted = new ManualResetEventSlim(false);
        using var capacityStarted = new ManualResetEventSlim(false);

        lock (store._Lock_Unsafe)
        {
            countRead = Task.Run(() =>
            {
                countStarted.Set();
                return store.Count;
            });
            capacityRead = Task.Run(() =>
            {
                capacityStarted.Set();
                return store.StorageCapacity;
            });

            Assert.True(countStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(capacityStarted.Wait(TimeSpan.FromSeconds(5)));
            Thread.Sleep(50);
            Assert.False(countRead.IsCompleted);
            Assert.False(capacityRead.IsCompleted);
        }

        Assert.Equal(1, await countRead.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await capacityRead.WaitAsync(TimeSpan.FromSeconds(5)) >= 4);
    }

    [Fact]
    public void SafeCapacityReaders_AfterDispose_ThrowObjectDisposedException()
    {
        var store = new RefSlotStore<int>();
        var slot = store.AllocSlot();
        store.Dispose();

        Assert.True(store.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = store.Count);
        Assert.Throws<ObjectDisposedException>(() => _ = store.AllocatedLength);
        Assert.Throws<ObjectDisposedException>(() => _ = store.FreeCount);
        Assert.Throws<ObjectDisposedException>(() => _ = store.StorageCapacity);
        Assert.Throws<ObjectDisposedException>(() => store.IsHandleAlive(slot));
        Assert.Throws<ObjectDisposedException>(() => store.GetMaxAllocatedIndex());
        Assert.Throws<ObjectDisposedException>(() => store.FindLastAllocatedSlot(slot.Index));

        // IDisposable implementations are expected to tolerate repeated terminal cleanup.
        store.Dispose();
    }
}
