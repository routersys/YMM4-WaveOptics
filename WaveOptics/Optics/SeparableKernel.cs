using System.Numerics;

namespace WaveOptics.Optics;

internal sealed class SeparableKernel
{
    readonly Comparison<int> descending;
    double[] work = [];
    double[] basis = [];
    double[] singularValues = [];
    int[] order = [];
    float[] horizontal = [];
    float[] vertical = [];

    public SeparableKernel()
    {
        descending = (left, right) => singularValues[right].CompareTo(singularValues[left]);
    }

    public int Size { get; private set; }
    public int Rank { get; private set; }
    public double ResidualEnergyRatio { get; private set; }
    public double Sum { get; private set; }
    public ReadOnlySpan<float> Horizontal => horizontal.AsSpan(0, Rank * Size);
    public ReadOnlySpan<float> Vertical => vertical.AsSpan(0, Rank * Size);

    public static SeparableKernel Decompose(ReadOnlySpan<double> kernel, int size, double residualEnergyRatio, int maximumRank)
    {
        var separable = new SeparableKernel();
        separable.Update(kernel, size, residualEnergyRatio, maximumRank);
        return separable;
    }

    public void Update(ReadOnlySpan<double> kernel, int size, double residualEnergyRatio, int maximumRank)
    {
        var area = size * size;
        if (work.Length < area)
        {
            work = new double[area];
            basis = new double[area];
            horizontal = new float[area];
            vertical = new float[area];
        }
        if (order.Length != size)
        {
            singularValues = new double[size];
            order = new int[size];
        }

        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
                work[column * size + row] = kernel[row * size + column];
        }
        Array.Clear(basis, 0, area);
        for (var i = 0; i < size; i++)
            basis[i * size + i] = 1d;

        OrthogonalizeColumns(work, basis, size);

        var totalEnergy = 0d;
        for (var column = 0; column < size; column++)
        {
            var norm = 0d;
            for (var row = 0; row < size; row++)
            {
                var value = work[column * size + row];
                norm += value * value;
            }
            singularValues[column] = Math.Sqrt(norm);
            order[column] = column;
            totalEnergy += norm;
        }

        Array.Sort(order, descending);

        var largest = singularValues[order[0]];
        var significant = 0;
        var threshold = largest * 1e-12;
        for (var i = 0; i < size && singularValues[order[i]] > threshold; i++)
            significant++;
        significant = Math.Max(significant, 1);

        var rank = significant;
        if (residualEnergyRatio > 0 && totalEnergy > 0)
        {
            var retained = 0d;
            var target = (1d - residualEnergyRatio) * totalEnergy;
            rank = 0;
            for (var i = 0; i < significant; i++)
            {
                retained += singularValues[order[i]] * singularValues[order[i]];
                rank++;
                if (retained >= target)
                    break;
            }
        }
        rank = Math.Clamp(rank, 1, Math.Min(maximumRank, significant));

        var retainedEnergy = 0d;
        for (var i = 0; i < rank; i++)
            retainedEnergy += singularValues[order[i]] * singularValues[order[i]];
        var residual = totalEnergy > 0 ? Math.Max(0d, (totalEnergy - retainedEnergy) / totalEnergy) : 0d;

        var sum = 0d;
        for (var term = 0; term < rank; term++)
        {
            var column = order[term];
            var singularValue = singularValues[column];
            var inverse = singularValue > 0 ? 1d / singularValue : 0d;
            var horizontalBase = term * size;
            var horizontalSum = 0d;
            var verticalSum = 0d;
            for (var k = 0; k < size; k++)
            {
                horizontal[horizontalBase + k] = (float)(basis[column * size + k]);
                vertical[horizontalBase + k] = (float)(work[column * size + k] * inverse * singularValue);
                horizontalSum += horizontal[horizontalBase + k];
                verticalSum += vertical[horizontalBase + k];
            }
            sum += horizontalSum * verticalSum;
        }

        Size = size;
        Rank = rank;
        ResidualEnergyRatio = residual;
        Sum = sum;
    }

    static void OrthogonalizeColumns(double[] work, double[] basis, int size)
    {
        const int maximumSweeps = 60;
        const double tolerance = 1e-15;
        for (var sweep = 0; sweep < maximumSweeps; sweep++)
        {
            var converged = true;
            for (var p = 0; p < size - 1; p++)
            {
                var workP = work.AsSpan(p * size, size);
                var basisP = basis.AsSpan(p * size, size);
                for (var q = p + 1; q < size; q++)
                {
                    var workQ = work.AsSpan(q * size, size);
                    var alpha = 0d;
                    var beta = 0d;
                    var gamma = 0d;
                    for (var k = 0; k < size; k++)
                    {
                        var wp = workP[k];
                        var wq = workQ[k];
                        alpha += wp * wp;
                        beta += wq * wq;
                        gamma += wp * wq;
                    }

                    if (Math.Abs(gamma) <= tolerance * Math.Sqrt(alpha * beta))
                        continue;

                    converged = false;
                    var zeta = (beta - alpha) / (2d * gamma);
                    var t = Math.Sign(zeta) / (Math.Abs(zeta) + Math.Sqrt(1d + zeta * zeta));
                    if (zeta == 0d)
                        t = 1d;
                    var c = 1d / Math.Sqrt(1d + t * t);
                    var s = c * t;

                    var basisQ = basis.AsSpan(q * size, size);
                    Rotate(workP, workQ, c, s);
                    Rotate(basisP, basisQ, c, s);
                }
            }
            if (converged)
                break;
        }
    }

    static void Rotate(Span<double> first, Span<double> second, double c, double s)
    {
        var index = 0;
        var width = Vector<double>.Count;
        if (Vector.IsHardwareAccelerated && first.Length >= width)
        {
            var cosine = new Vector<double>(c);
            var sine = new Vector<double>(s);
            for (; index + width <= first.Length; index += width)
            {
                var a = new Vector<double>(first[index..]);
                var b = new Vector<double>(second[index..]);
                (cosine * a - sine * b).CopyTo(first[index..]);
                (sine * a + cosine * b).CopyTo(second[index..]);
            }
        }
        for (; index < first.Length; index++)
        {
            var a = first[index];
            var b = second[index];
            first[index] = c * a - s * b;
            second[index] = s * a + c * b;
        }
    }
}
