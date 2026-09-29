namespace WaveOptics.Harness;

internal static class SyntheticImage
{
    const int GridPitch = 32;

    public static byte[] Create(int width, int height)
    {
        var pixels = new byte[width * height * HarnessImage.BytesPerPixel];
        var plateLeft = width * 0.10f;
        var plateRight = width * 0.56f;
        var plateTop = height * 0.14f;
        var plateBottom = height * 0.86f;
        var discCenterX = width * 0.70f;
        var discCenterY = height * 0.50f;
        var discRadius = MathF.Min(width, height) * 0.28f;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var positionX = x + 0.5f;
                var positionY = y + 0.5f;
                var plateInset = MathF.Min(MathF.Min(positionX - plateLeft, plateRight - positionX), MathF.Min(positionY - plateTop, plateBottom - positionY));
                var plateCoverage = Math.Clamp(plateInset + 0.5f, 0f, 1f);
                var distance = MathF.Sqrt((positionX - discCenterX) * (positionX - discCenterX) + (positionY - discCenterY) * (positionY - discCenterY));
                var discCoverage = Math.Clamp(discRadius - distance + 0.5f, 0f, 1f);
                if (plateCoverage == 0f && discCoverage == 0f)
                    continue;

                var onGrid = x % GridPitch == 0 || y % GridPitch == 0;
                var plateWeight = plateCoverage * (1f - discCoverage);
                var shade = 1f - distance / discRadius * 0.5f;
                var red = (onGrid ? 0.25f : 0.82f) * plateWeight + 0.90f * shade * discCoverage;
                var green = (onGrid ? 0.30f : 0.86f) * plateWeight + 0.45f * shade * discCoverage;
                var blue = (onGrid ? 0.40f : 0.90f) * plateWeight + 0.30f * shade * discCoverage;
                var alpha = plateWeight + discCoverage;

                var offset = (y * width + x) * HarnessImage.BytesPerPixel;
                pixels[offset] = ToByte(blue);
                pixels[offset + 1] = ToByte(green);
                pixels[offset + 2] = ToByte(red);
                pixels[offset + 3] = ToByte(alpha);
            }
        }

        return pixels;
    }

    static byte ToByte(float premultiplied) => (byte)MathF.Round(Math.Clamp(premultiplied, 0f, 1f) * byte.MaxValue);
}
