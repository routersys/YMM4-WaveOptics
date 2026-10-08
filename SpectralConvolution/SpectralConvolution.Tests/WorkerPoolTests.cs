namespace SpectralConvolution.Tests;

public sealed class WorkerPoolTests
{
    sealed class CountingJob(int items) : IParallelJob
    {
        public readonly int[] Runs = new int[items];
        public readonly HashSet<int> Workers = [];
        public readonly List<int> Begun = [];
        public int Delay;

        public void Begin(int worker)
        {
            lock (Begun)
                Begun.Add(worker);
        }

        public void Execute(int index, int worker)
        {
            Interlocked.Increment(ref Runs[index]);
            lock (Workers)
                Workers.Add(worker);
            if (Delay > 0)
                Thread.Sleep(Delay);
        }
    }

    sealed class FailingJob(int failing) : IParallelJob
    {
        public int Completed;

        public void Execute(int index, int worker)
        {
            if (index == failing)
                throw new InvalidOperationException();
            Interlocked.Increment(ref Completed);
        }
    }

    [Fact]
    public void TheCallerAndTheHelpersAreAtLeastOneWorker()
    {
        Assert.InRange(WorkerPool.Shared.Parallelism, 1, WorkerPool.MaximumHelpers + 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(15)]
    [InlineData(1000)]
    public void EveryItemRunsExactlyOnceOnAWorkerWithinRange(int items)
    {
        var job = new CountingJob(items);

        WorkerPool.Shared.Run(job, items);

        Assert.All(job.Runs, runs => Assert.Equal(1, runs));
        Assert.All(job.Workers, worker => Assert.InRange(worker, 0, WorkerPool.Shared.Parallelism - 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NothingRunsWithoutItems(int items)
    {
        var job = new CountingJob(4);

        WorkerPool.Shared.Run(job, items);

        Assert.All(job.Runs, runs => Assert.Equal(0, runs));
    }

    [Fact]
    public void SlowItemsAreSharedAmongTheWorkers()
    {
        if (WorkerPool.Shared.Parallelism == 1)
            return;
        var job = new CountingJob(48) { Delay = 4 };

        WorkerPool.Shared.Run(job, 48);

        Assert.True(job.Workers.Count > 1, $"{job.Workers.Count}");
    }

    [Fact]
    public void AnExceptionInAnItemIsRethrownToTheCallerAndThePoolStaysUsable()
    {
        var failing = new FailingJob(7);

        Assert.Throws<InvalidOperationException>(() => WorkerPool.Shared.Run(failing, 20));

        var job = new CountingJob(20);
        WorkerPool.Shared.Run(job, 20);
        Assert.All(job.Runs, runs => Assert.Equal(1, runs));
    }

    [Fact]
    public void CallersFromSeveralThreadsEachGetTheirItemsRun()
    {
        var jobs = Enumerable.Range(0, 4).Select(_ => new CountingJob(200)).ToArray();

        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = 4 }, job => WorkerPool.Shared.Run(job, 200));

        Assert.All(jobs, job => Assert.All(job.Runs, runs => Assert.Equal(1, runs)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AParallelismLimitKeepsTheWorkerNumbersBelowTheLimit(int limit)
    {
        using var pool = new WorkerPool(5);
        var job = new CountingJob(200) { Delay = 1 };

        pool.Run(job, 200, limit);

        Assert.All(job.Runs, runs => Assert.Equal(1, runs));
        Assert.All(job.Workers, worker => Assert.InRange(worker, 0, limit - 1));
        Assert.Equal(limit, job.Begun.Count);
        Assert.Equal(Enumerable.Range(0, limit), job.Begun.Order());
    }

    [Fact]
    public void EachWorkerBeginsOnceBeforeItTakesItems()
    {
        using var pool = new WorkerPool(3);
        var job = new CountingJob(64) { Delay = 1 };

        pool.Run(job, 64);

        Assert.Equal(pool.Parallelism, job.Begun.Count);
        Assert.Equal(Enumerable.Range(0, pool.Parallelism), job.Begun.Order());
    }

    [Fact]
    public void APoolWithoutHelpersRunsEveryItemOnTheCaller()
    {
        using var pool = new WorkerPool(0);
        var job = new CountingJob(10);

        pool.Run(job, 10);

        Assert.Equal(1, pool.Parallelism);
        Assert.All(job.Runs, runs => Assert.Equal(1, runs));
        Assert.Equal([0], job.Workers);
        Assert.Equal([0], job.Begun);
    }

    [Fact]
    public void ADisposedPoolRefusesToRun()
    {
        var pool = new WorkerPool(2);
        pool.Run(new CountingJob(8), 8);

        pool.Dispose();
        pool.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pool.Run(new CountingJob(8), 8));
    }

    [Fact]
    public void ARunNeedsAtLeastOneWorker()
    {
        using var pool = new WorkerPool(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => pool.Run(new CountingJob(4), 4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkerPool(-1));
    }

    [Fact]
    public void AWarmRunAllocatesNothingOnTheCallingThread()
    {
        var job = new CountingJob(32);
        WorkerPool.Shared.Run(job, 32);
        WorkerPool.Shared.Run(job, 32);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var round = 0; round < 5; round++)
            WorkerPool.Shared.Run(job, 32);

        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
