using System.Diagnostics;
using Vortice.Direct3D11;

namespace WaveOptics.Harness;

internal sealed class GpuTimer : IDisposable
{
    static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(5);

    readonly ID3D11DeviceContext context;
    readonly ID3D11Query disjoint;
    readonly ID3D11Query start;
    readonly ID3D11Query stop;

    public GpuTimer(ID3D11Device device, ID3D11DeviceContext context)
    {
        this.context = context;
        disjoint = device.CreateQuery(new QueryDescription(QueryType.TimestampDisjoint, QueryFlags.None));
        start = device.CreateQuery(new QueryDescription(QueryType.Timestamp, QueryFlags.None));
        stop = device.CreateQuery(new QueryDescription(QueryType.Timestamp, QueryFlags.None));
    }

    public void Begin()
    {
        context.Begin(disjoint);
        context.End(start);
    }

    public void End()
    {
        context.End(stop);
        context.End(disjoint);
    }

    public TimeSpan? Resolve()
    {
        if (!Wait<QueryDataTimestampDisjoint>(disjoint, out var timing) || timing.Disjoint || timing.Frequency == 0UL)
            return null;
        if (!Wait<ulong>(start, out var began) || !Wait<ulong>(stop, out var ended) || ended < began)
            return null;
        return TimeSpan.FromSeconds((ended - began) / (double)timing.Frequency);
    }

    bool Wait<T>(ID3D11Query query, out T value) where T : unmanaged
    {
        var elapsed = Stopwatch.StartNew();
        while (!context.GetData(query, out value))
        {
            if (elapsed.Elapsed > ResolveTimeout)
                return false;
            Thread.Yield();
        }

        return true;
    }

    public void Dispose()
    {
        stop.Dispose();
        start.Dispose();
        disjoint.Dispose();
    }
}
