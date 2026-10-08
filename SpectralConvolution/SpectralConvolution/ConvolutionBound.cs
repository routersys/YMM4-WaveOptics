namespace SpectralConvolution;

internal static class ConvolutionBound
{
    public static readonly double GpuOperationError = Math.ScaleB(1d, -23);
    public static readonly double CpuOperationError = Math.ScaleB(1d, -24);
    public static readonly double SingleRounding = Math.ScaleB(1d, -24);
    public static readonly double DoubleRounding = Math.ScaleB(1d, -53);
    public static readonly double SmallestNormal = Math.ScaleB(1d, -126);
    public static readonly double TrigonometricError = Math.ScaleB(1d, -51);

    public static double TwiddleAngleError => Math.PI * TrigonometricError;

    public static double DoubleTwiddleError => TwiddleAngleError + Math.Sqrt(2d) * TrigonometricError;

    public static double SingleTwiddleError => SingleRounding * (1d + DoubleTwiddleError) + DoubleTwiddleError;

    public static double Gamma(int operations, double unit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(operations);
        if (!double.IsFinite(unit) || unit < 0d)
            throw new ArgumentOutOfRangeException(nameof(unit));
        var product = operations * unit;
        if (product >= 1d)
            throw new ArgumentOutOfRangeException(nameof(operations));
        return product / (1d - product);
    }

    public static double ComplexMultiplication(double unit) => Math.Sqrt(2d) * Gamma(2, unit);

    public static double Stage(double unit, double twiddleError)
        => (1d + unit) * (twiddleError + (1d + twiddleError) * ComplexMultiplication(unit)) + unit;

    public static double Transform(int stages, double unit, double twiddleError)
        => double.ExpM1(stages * double.LogP1(Stage(unit, twiddleError)));

    public static double SpectrumError(int size)
    {
        var transform = Transform(2 * TilePlan.Log2Of(size), DoubleRounding, DoubleTwiddleError) * size;
        return SingleRounding * (1d + transform) + transform + Math.Sqrt(2d) * SmallestNormal;
    }

    public static double Relative(int size, double operationError)
    {
        var multiplication = ComplexMultiplication(operationError);
        var transform = Transform(2 * TilePlan.Log2Of(size), operationError, SingleTwiddleError);
        var spectrum = SpectrumError(size);
        var product = transform * (1d + spectrum) * (1d + multiplication) + spectrum * (1d + multiplication) + multiplication;
        var convolution = product + transform * (1d + product);
        return convolution + SingleRounding * (1d + convolution);
    }

    public static double ChromaticRelative(int size, double operationError)
    {
        var relative = Relative(size, operationError);
        var splitAndMerge = 2d * Math.Sqrt(2d) * operationError;
        return relative + splitAndMerge * (1d + relative);
    }

    public static int ChromaticFlushedOperations(int size)
        => FlushedOperations(size) + (int)Math.Ceiling(16d * Math.Sqrt(2d) * size);

    public static double ChromaticAbsolute(int size, double operationError, double norm)
    {
        if (!double.IsFinite(norm) || norm < 0d)
            throw new ArgumentOutOfRangeException(nameof(norm));
        return ChromaticRelative(size, operationError) * norm + ChromaticFlushedOperations(size) * SmallestNormal;
    }

    public static int FlushedOperations(int size)
    {
        return (int)Math.Ceiling(2d * (4 * TilePlan.Log2Of(size) + 2) * 4d * Math.Sqrt(2d) * size);
    }

    public static double Absolute(int size, double operationError, double norm)
    {
        if (!double.IsFinite(norm) || norm < 0d)
            throw new ArgumentOutOfRangeException(nameof(norm));
        return Relative(size, operationError) * norm + FlushedOperations(size) * SmallestNormal;
    }
}
