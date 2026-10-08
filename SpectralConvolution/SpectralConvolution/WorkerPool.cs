using System.Runtime.ExceptionServices;

namespace SpectralConvolution;

internal interface IParallelJob
{
    void Begin(int worker)
    {
    }

    void Execute(int index, int worker);
}

internal sealed class WorkerPool : IDisposable
{
    public const int MaximumHelpers = 5;

    readonly Thread[] threads;
    readonly SemaphoreSlim start = new(0);
    readonly SemaphoreSlim finished = new(0);
    readonly object gate = new();
    IParallelJob? job;
    bool disposed;
    int count;
    int next;
    int claimed;
    Exception? failure;

    public WorkerPool(int helpers)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(helpers);
        threads = new Thread[helpers];
        for (var index = 0; index < helpers; index++)
        {
            threads[index] = new Thread(Serve) { IsBackground = true, Name = nameof(WorkerPool) };
            threads[index].Start();
        }
    }

    public static WorkerPool Shared { get; } = new(Math.Clamp(Environment.ProcessorCount - 1, 0, MaximumHelpers));

    public int Parallelism => threads.Length + 1;

    public void Run(IParallelJob parallelJob, int itemCount, int maximumParallelism = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(parallelJob);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumParallelism);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (itemCount <= 0)
            return;
        lock (gate)
        {
            var helpers = Math.Min(Math.Min(threads.Length, itemCount - 1), maximumParallelism - 1);
            if (helpers == 0)
            {
                parallelJob.Begin(0);
                for (var index = 0; index < itemCount; index++)
                    parallelJob.Execute(index, 0);
                return;
            }

            job = parallelJob;
            count = itemCount;
            next = 0;
            claimed = 0;
            failure = null;
            start.Release(helpers);
            Drain();
            for (var index = 0; index < helpers; index++)
                finished.Wait();

            job = null;
            if (failure is { } exception)
                ExceptionDispatchInfo.Throw(exception);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (threads.Length > 0)
            start.Release(threads.Length);
        foreach (var thread in threads)
            thread.Join();
        start.Dispose();
        finished.Dispose();
    }

    void Serve()
    {
        while (true)
        {
            start.Wait();
            if (disposed)
                return;
            Drain();
            finished.Release();
        }
    }

    void Drain()
    {
        try
        {
            var worker = Interlocked.Increment(ref claimed) - 1;
            job!.Begin(worker);
            int index;
            while ((index = Interlocked.Increment(ref next) - 1) < count)
                job.Execute(index, worker);
        }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(ref failure, exception, null);
        }
    }
}
