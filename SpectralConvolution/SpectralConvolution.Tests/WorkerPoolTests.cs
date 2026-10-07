namespace SpectralConvolution.Tests;

public sealed class WorkerPoolTests
{
    sealed class CountingJob(int items) : IParallelJob
    {
        public readonly int[] Runs = new int[items];
        public readonly HashSet<int> Workers = [];
        public int Delay;

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
