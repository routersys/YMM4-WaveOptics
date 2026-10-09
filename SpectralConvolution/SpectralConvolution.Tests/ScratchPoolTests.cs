namespace SpectralConvolution.Tests;

public sealed class ScratchPoolTests
{
    [Fact]
    public void AReturnedItemIsHandedOutAgain()
    {
        var pool = new ScratchPool<Item>(2);
        Item first;
        using (var lease = pool.Rent())
            first = lease.Value;

        using var again = pool.Rent();

        Assert.Same(first, again.Value);
    }

    [Fact]
    public void ItemsRentedAtTheSameTimeAreDistinct()
    {
        var pool = new ScratchPool<Item>(2);
        using (pool.Rent())
        {
        }

        using var first = pool.Rent();
        using var second = pool.Rent();

        Assert.NotSame(first.Value, second.Value);
    }

    [Fact]
    public void AnItemReturnedBeyondTheCapacityIsDropped()
    {
        var pool = new ScratchPool<Item>(1);
        var first = pool.Rent();
        var second = pool.Rent();
        var kept = first.Value;
        var dropped = second.Value;
        first.Dispose();
        second.Dispose();

        using var rentedKept = pool.Rent();
        using var rentedNew = pool.Rent();

        Assert.Same(kept, rentedKept.Value);
        Assert.NotSame(kept, rentedNew.Value);
        Assert.NotSame(dropped, rentedNew.Value);
    }

    [Fact]
    public void ARentedItemIsNeverSharedBetweenThreads()
    {
        var pool = new ScratchPool<Item>(Environment.ProcessorCount);
        var collisions = 0;

        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            for (var round = 0; round < 20000; round++)
            {
                using var lease = pool.Rent();
                if (Interlocked.Exchange(ref lease.Value.Busy, 1) != 0)
                    Interlocked.Increment(ref collisions);
                Thread.SpinWait(20);
                Volatile.Write(ref lease.Value.Busy, 0);
            }
        });

        Assert.Equal(0, collisions);
    }

    sealed class Item
    {
        public int Busy;
    }
}
