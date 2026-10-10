namespace SpectralConvolution;

internal sealed class ScratchPool<T>(int capacity) where T : class, new()
{
    readonly T?[] slots = new T?[capacity];
    readonly object gate = new();

    public static ScratchPool<T> Shared { get; } = new(Environment.ProcessorCount);

    public Lease Rent()
    {
        T? item = null;
        lock (gate)
        {
            for (var index = 0; index < slots.Length; index++)
            {
                if (slots[index] is not { } found)
                    continue;

                item = found;
                slots[index] = null;
                break;
            }
        }

        return new Lease(this, item ?? new T());
    }

    void Return(T item)
    {
        lock (gate)
        {
            for (var index = 0; index < slots.Length; index++)
            {
                if (slots[index] is not null)
                    continue;

                slots[index] = item;
                return;
            }
        }
    }

    public readonly struct Lease(ScratchPool<T> pool, T value) : IDisposable
    {
        public T Value { get; } = value;

        public void Dispose() => pool.Return(Value);
    }
}
