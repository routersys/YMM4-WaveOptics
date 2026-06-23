namespace WaveOptics.Optics;

internal readonly struct SeparableKernel
{
    public int Size { get; }
    public int Rank { get; }
    public double ResidualEnergyRatio { get; }
    public float[] Horizontal { get; }
    public float[] Vertical { get; }

    SeparableKernel(int size, int rank, double residualEnergyRatio, float[] horizontal, float[] vertical)
    {
        Size = size;
        Rank = rank;
        ResidualEnergyRatio = residualEnergyRatio;
        Horizontal = horizontal;
        Vertical = vertical;
    }

    public static SeparableKernel Decompose(ReadOnlySpan<double> kernel, int size, double residualEnergyRatio, int maximumRank)
    {
        var work = new double[size * size];
        for (var index = 0; index < work.Length; index++)
            work[index] = kernel[index];
        var basis = new double[size * size];
        for (var i = 0; i < size; i++)
            basis[i * size + i] = 1d;

        OrthogonalizeColumns(work, basis, size);

        var singularValues = new double[size];
        var order = new int[size];
        var totalEnergy = 0d;
        for (var column = 0; column < size; column++)
        {
            var norm = 0d;
            for (var row = 0; row < size; row++)
            {
                var value = work[row * size + column];
                norm += value * value;
            }
            singularValues[column] = Math.Sqrt(norm);
            order[column] = column;
            totalEnergy += norm;
        }

        Array.Sort(order, (left, right) => singularValues[right].CompareTo(singularValues[left]));

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

        var horizontal = new float[rank * size];
        var vertical = new float[rank * size];
        for (var term = 0; term < rank; term++)
        {
            var column = order[term];
            var singularValue = singularValues[column];
            var inverse = singularValue > 0 ? 1d / singularValue : 0d;
            var horizontalBase = term * size;
            for (var k = 0; k < size; k++)
            {
                horizontal[horizontalBase + k] = (float)(basis[k * size + column]);
                vertical[horizontalBase + k] = (float)(work[k * size + column] * inverse * singularValue);
            }
        }

        return new SeparableKernel(size, rank, residual, horizontal, vertical);
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
                for (var q = p + 1; q < size; q++)
                {
                    var alpha = 0d;
                    var beta = 0d;
                    var gamma = 0d;
                    for (var k = 0; k < size; k++)
                    {
                        var wp = work[k * size + p];
                        var wq = work[k * size + q];
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

                    for (var k = 0; k < size; k++)
                    {
                        var index = k * size;
                        var wp = work[index + p];
                        var wq = work[index + q];
                        work[index + p] = c * wp - s * wq;
                        work[index + q] = s * wp + c * wq;
                        var vp = basis[index + p];
                        var vq = basis[index + q];
                        basis[index + p] = c * vp - s * vq;
                        basis[index + q] = s * vp + c * vq;
                    }
                }
            }
            if (converged)
                break;
        }
    }
}
