using System.Runtime.ExceptionServices;

namespace SpectralConvolution;

internal interface IParallelJob
{
    void Execute(int index, int worker);
}

internal sealed class WorkerPool
{
    public const int MaximumHelpers = 5;

    readonly Thread[] threads;
    readonly SemaphoreSlim start = new(0);
    readonly SemaphoreSlim finished = new(0);
    readonly object gate = new();
    IParallelJob? job;
    int count;
    int next;
    Exception? failure;

    WorkerPool(int helpers)
    {
        threads = new Thread[helpers];
        for (var index = 0; index < helpers; index++)
        {
            var worker = index + 1;
            threads[index] = new Thread(() => Serve(worker)) { IsBackground = true, Name = nameof(WorkerPool) };
            threads[index].Start();
        }
    }

    public static WorkerPool Shared { get; } = new(Math.Clamp(Environment.ProcessorCount - 1, 0, MaximumHelpers));

    public int Parallelism => threads.Length + 1;

    public void Run(IParallelJob parallelJob, int itemCount)
    {
        ArgumentNullException.ThrowIfNull(parallelJob);
        if (itemCount <= 0)
            return;
        lock (gate)
        {
            if (itemCount == 1 || threads.Length == 0)
            {
                for (var index = 0; index < itemCount; index++)
                    parallelJob.Execute(index, 0);
                return;
            }

            job = parallelJob;
            count = itemCount;
            next = 0;
            failure = null;
            var helpers = Math.Min(threads.Length, itemCount - 1);
            start.Release(helpers);
            Drain(0);
            for (var index = 0; index < helpers; index++)
                finished.Wait();

            job = null;
            if (failure is { } exception)
                ExceptionDispatchInfo.Throw(exception);
        }
    }

    void Serve(int worker)
    {
        while (true)
        {
            start.Wait();
            Drain(worker);
            finished.Release();
        }
    }

    void Drain(int worker)
    {
        try
        {
            int index;
            while ((index = Interlocked.Increment(ref next) - 1) < count)
                job!.Execute(index, worker);
        }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(ref failure, exception, null);
        }
    }
}
