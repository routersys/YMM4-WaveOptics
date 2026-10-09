namespace WaveOptics.Tests;

internal static class AllocationProbe
{
    public static long MinimumAllocatedBytes(Action action, int rounds)
    {
        var minimum = long.MaxValue;
        for (var round = 0; round < rounds; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            action();
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return minimum;
    }

    public static void Settle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
