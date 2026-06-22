namespace WaveOptics.Abstractions;

public sealed class PsfKernel
{
    readonly double[] values;

    public int Size { get; }
    public ReadOnlyMemory<double> Values => values;

    public double this[int x, int y]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(x);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Size);
            ArgumentOutOfRangeException.ThrowIfNegative(y);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Size);
            return values[y * Size + x];
        }
    }

    public PsfKernel(int size, ReadOnlySpan<double> values)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        if ((size & 1) == 0)
            throw new ArgumentException(nameof(size));
        if (values.Length != size * size)
            throw new ArgumentException(nameof(values));

        this.values = values.ToArray();
        for (var index = 0; index < this.values.Length; index++)
        {
            var value = this.values[index];
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(values));
        }

        var sum = this.values.Sum();
        if (!double.IsFinite(sum) || sum <= 0)
            throw new ArgumentOutOfRangeException(nameof(values));

        for (var index = 0; index < this.values.Length; index++)
            this.values[index] /= sum;

        Size = size;
    }

    public double Sum()
    {
        var sum = 0d;
        var compensation = 0d;
        foreach (var value in values)
        {
            var corrected = value - compensation;
            var next = sum + corrected;
            compensation = next - sum - corrected;
            sum = next;
        }
        return sum;
    }

    public double[] ToArray() => (double[])values.Clone();
}
