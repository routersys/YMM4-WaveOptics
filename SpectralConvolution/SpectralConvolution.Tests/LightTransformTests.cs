namespace SpectralConvolution.Tests;

public sealed class LightTransformTests
{
    static readonly LightOptions Linear = new(true, false, 0f, 0f);

    static double ReferenceDecode(double encoded)
        => encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);

    [Fact]
    public void TheEndpointsAreFixed()
    {
        Assert.Equal(0f, LightTransform.Decode(0f));
        Assert.Equal(1f, LightTransform.Decode(1f));
        Assert.Equal(0f, LightTransform.Encode(0f));
        Assert.Equal(1f, LightTransform.Encode(1f));
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(0.04045f)]
    [InlineData(0.05f)]
    [InlineData(0.2f)]
    [InlineData(0.5f)]
    [InlineData(0.8f)]
    public void DecodeFollowsTheStandardCurve(float encoded)
    {
        var expected = ReferenceDecode(encoded);

        Assert.Equal(expected, LightTransform.Decode(encoded), expected * 1e-6);
    }

    [Fact]
    public void EncodeReturnsEveryByteThatDecodeStarted()
    {
        for (var value = 0; value < 256; value++)
        {
            var encoded = LightTransform.Encode(LightTransform.Decode(value / 255f));

            Assert.Equal(value, (byte)(encoded * 255f + 0.5f));
        }
    }

    [Fact]
    public void WithoutLinearLightTheBytesPassThrough()
    {
        var (red, green, blue, alpha) = LightTransform.FromBytes(10, 200, 33, 128, default);

        Assert.Equal(10 / 255f, red);
        Assert.Equal(200 / 255f, green);
        Assert.Equal(33 / 255f, blue);
        Assert.Equal(128 / 255f, alpha);
    }

    [Fact]
    public void AnOpaquePixelIsDecodedChannelByChannel()
    {
        var (red, green, blue, alpha) = LightTransform.FromBytes(10, 128, 255, 255, Linear);

        Assert.Equal(LightTransform.Decode(10 / 255f), red);
        Assert.Equal(LightTransform.Decode(128 / 255f), green);
        Assert.Equal(1f, blue);
        Assert.Equal(1f, alpha);
    }

    [Fact]
    public void ATranslucentPixelIsDecodedFromItsStraightColorAndPremultipliedAgain()
    {
        var (red, green, blue, alpha) = LightTransform.FromBytes(128, 64, 32, 128, Linear);

        var opacity = 128 / 255f;
        Assert.Equal(opacity, alpha);
        Assert.Equal(1.0, red / opacity, 1e-6);
        Assert.Equal(ReferenceDecode(64 / 128d), green / opacity, 1e-6);
        Assert.Equal(ReferenceDecode(32 / 128d), blue / opacity, 1e-6);
    }

    [Fact]
    public void AFullyTransparentPixelIsZeroWhateverItsColor()
    {
        Assert.Equal((0f, 0f, 0f, 0f), LightTransform.FromBytes(200, 100, 50, 0, Linear));
    }

    [Fact]
    public void HighlightsRaiseAWhitePixelByTheBoost()
    {
        var options = new LightOptions(true, false, 0.9f, 10f);

        var (red, green, blue, alpha) = LightTransform.FromBytes(255, 255, 255, 255, options);

        Assert.Equal(10f, red);
        Assert.Equal(10f, green);
        Assert.Equal(10f, blue);
        Assert.Equal(10f, alpha);
    }

    [Fact]
    public void HighlightsLeaveAPixelAtOrBelowTheThresholdAlone()
    {
        var options = new LightOptions(true, false, 230 / 255f, 10f);

        var raised = LightTransform.FromBytes(230, 230, 230, 255, options);
        var plain = LightTransform.FromBytes(230, 230, 230, 255, Linear);

        Assert.Equal(plain, raised);
    }

    [Fact]
    public void HighlightsRaiseByTheSquareOfTheExcess()
    {
        var options = new LightOptions(true, false, 0.9f, 10f);
        var peak = 242 / 255d;
        var expectedWeight = 1d + 9d * Math.Pow((peak - 0.9f) / (1d - 0.9f), 2d);

        var raised = LightTransform.FromBytes(242, 242, 242, 255, options);
        var plain = LightTransform.FromBytes(242, 242, 242, 255, Linear);

        Assert.Equal(expectedWeight, raised.Red / plain.Red, expectedWeight * 1e-3);
        Assert.Equal(raised.Red / plain.Red, raised.Alpha / plain.Alpha, 1e-3);
    }

    [Fact]
    public void HighlightsKeepTheRatioBetweenTheChannels()
    {
        var options = new LightOptions(true, false, 0.5f, 30f);

        var raised = LightTransform.FromBytes(255, 128, 64, 255, options);
        var plain = LightTransform.FromBytes(255, 128, 64, 255, Linear);

        Assert.Equal(raised.Red / plain.Red, raised.Green / plain.Green, 1e-4);
        Assert.Equal(raised.Red / plain.Red, raised.Blue / plain.Blue, 1e-4);
    }

    [Theory]
    [InlineData(1f, 0.5f)]
    [InlineData(0f, 0.5f)]
    [InlineData(10f, 1f)]
    public void HighlightsDoNothingWithoutABoostAboveOneOrAThresholdBelowOne(float boost, float threshold)
    {
        var options = new LightOptions(true, false, threshold, boost);

        Assert.False(options.Highlights);
        Assert.Equal(LightTransform.FromBytes(255, 200, 100, 255, Linear), LightTransform.FromBytes(255, 200, 100, 255, options));
    }

    [Fact]
    public void HighlightsNeedLinearLight()
    {
        var options = new LightOptions(false, false, 0.5f, 10f);

        Assert.False(options.Highlights);
        Assert.Equal(LightTransform.FromBytes(255, 200, 100, 255, default), LightTransform.FromBytes(255, 200, 100, 255, options));
    }

    [Fact]
    public void ForTheConvolutionOnlyTheInputAffectingFieldsRemain()
    {
        Assert.Equal(new LightOptions(true, false, 0.9f, 10f), new LightOptions(true, true, 0.9f, 10f).ForConvolution());
        Assert.Equal(new LightOptions(true, false, 0f, 0f), new LightOptions(true, true, 0.9f, 1f).ForConvolution());
        Assert.Equal(new LightOptions(false, false, 0f, 0f), new LightOptions(false, true, 0.9f, 10f).ForConvolution());
    }

    [Fact]
    public void EveryOpaqueByteSurvivesTheRoundTrip()
    {
        for (var value = 0; value < 256; value++)
        {
            var (red, green, blue, alpha) = LightTransform.FromBytes((byte)value, (byte)value, (byte)value, 255, Linear);
            var (outputBlue, outputGreen, outputRed, outputAlpha) = LightTransform.ToBytes(red, green, blue, alpha, 1f, Linear, 0, 0);

            Assert.Equal((byte)value, outputRed);
            Assert.Equal((byte)value, outputGreen);
            Assert.Equal((byte)value, outputBlue);
            Assert.Equal((byte)255, outputAlpha);
        }
    }

    [Fact]
    public void ATranslucentPixelSurvivesTheRoundTripWithinOneStep()
    {
        for (var alpha = 1; alpha < 256; alpha += 7)
        {
            for (var value = 0; value <= alpha; value += 5)
            {
                var (red, green, blue, opacity) = LightTransform.FromBytes((byte)value, (byte)(value / 2), (byte)(value / 3), (byte)alpha, Linear);
                var (outputBlue, outputGreen, outputRed, outputAlpha) = LightTransform.ToBytes(red, green, blue, opacity, 1f, Linear, 0, 0);

                Assert.InRange(outputRed, value - 1, value + 1);
                Assert.InRange(outputGreen, value / 2 - 1, value / 2 + 1);
                Assert.InRange(outputBlue, value / 3 - 1, value / 3 + 1);
                Assert.Equal((byte)alpha, outputAlpha);
            }
        }
    }

    [Fact]
    public void TheGainBrightensAndSaturates()
    {
        var half = LightTransform.Decode(0.5f);

        var doubled = LightTransform.ToBytes(half, half, half, 1f, 2f, Linear, 0, 0);
        var saturated = LightTransform.ToBytes(half, half, half, 1f, 100f, Linear, 0, 0);

        Assert.Equal((byte)(LightTransform.Encode(MathF.Min(half * 2f, 1f)) * 255f + 0.5f), doubled.Red);
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), saturated);
    }

    [Fact]
    public void AColorBeyondTheOpacityIsHeldAtTheOpacity()
    {
        var (blue, green, red, alpha) = LightTransform.ToBytes(2f, 0.1f, 0f, 0.5f, 1f, Linear, 0, 0);

        Assert.Equal((byte)128, alpha);
        Assert.Equal((byte)128, red);
        Assert.True(green < 128);
        Assert.Equal((byte)0, blue);
    }

    [Fact]
    public void WithoutLinearLightTheOutputIsTheClampedValue()
    {
        float[] values = [float.NaN, -1f, 0f, 0.2f, 0.5f, 0.999f, 1f, 2f, float.PositiveInfinity];
        foreach (var value in values)
        {
            var (blue, green, red, alpha) = LightTransform.ToBytes(value, value, value, value, 1.5f, default, 3, 4);

            var expected = CpuTileConvolver.ToUnorm(value * 1.5f);
            Assert.Equal(expected, red);
            Assert.Equal(expected, green);
            Assert.Equal(expected, blue);
            Assert.Equal(expected, alpha);
        }
    }

    [Fact]
    public void TheDitherThresholdStaysBelowOneAndAveragesOneHalf()
    {
        var sum = 0d;
        var count = 0;
        for (var y = 0; y < 300; y++)
        {
            for (var x = 0; x < 300; x++)
            {
                var threshold = LightTransform.DitherThreshold(x, y, 0);
                Assert.InRange(threshold, 0f, 0.99999994f);
                sum += threshold;
                count++;
            }
        }

        Assert.Equal(0.5, sum / count, 0.01);
    }

    [Fact]
    public void TheDitherThresholdIsDeterministicAndDiffersByChannel()
    {
        Assert.Equal(LightTransform.DitherThreshold(12, 34, 1), LightTransform.DitherThreshold(12, 34, 1));
        var differing = 0;
        for (var x = 0; x < 64; x++)
        {
            if (LightTransform.DitherThreshold(x, 7, 0) != LightTransform.DitherThreshold(x, 7, 1))
                differing++;
        }

        Assert.True(differing > 60);
    }

    [Fact]
    public void ADitheredValueKeepsItsFractionOnAverage()
    {
        var options = new LightOptions(false, true, 0f, 0f);
        var value = 100.3f / 255f;
        var sum = 0d;
        var count = 0;
        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 200; x++)
            {
                var (_, _, red, _) = LightTransform.ToBytes(value, 0f, 0f, 1f, 1f, options, x, y);
                Assert.InRange(red, 100, 101);
                sum += red;
                count++;
            }
        }

        Assert.Equal(100.3, sum / count, 0.02);
    }

    [Fact]
    public void ADitheredBlackAndWhiteAreUnchanged()
    {
        var options = new LightOptions(false, true, 0f, 0f);
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), LightTransform.ToBytes(0f, 0f, 0f, 1f, 1f, options, x, y));
                Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), LightTransform.ToBytes(1f, 1f, 1f, 1f, 1f, options, x, y));
            }
        }
    }

    [Fact]
    public void ALinearWhiteStaysWhiteWhenDithered()
    {
        var options = new LightOptions(true, true, 0f, 0f);
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 100; x++)
                Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), LightTransform.ToBytes(1f, 1f, 1f, 1f, 1f, options, x, y));
        }
    }

    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(-0.1f, 0f)]
    [InlineData(1.1f, 0f)]
    [InlineData(0.5f, float.NaN)]
    [InlineData(0.5f, -1f)]
    [InlineData(0.5f, 1001f)]
    public void InvalidOptionsAreRejected(float threshold, float boost)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightOptions(true, false, threshold, boost).Validate());
    }

    [Fact]
    public void TheDefaultOptionsAreValidAndDefault()
    {
        var options = default(LightOptions);

        options.Validate();
        Assert.True(options.IsDefault);
        Assert.False(options.Highlights);
        Assert.False(new LightOptions(true, false, 0f, 0f).IsDefault);
        Assert.False(new LightOptions(false, true, 0f, 0f).IsDefault);
    }

    [Fact]
    public void ADitheredFullValueNeverWrapsAroundWhateverTheThreshold()
    {
        var options = new LightOptions(false, true, 0f, 0f);
        var nearOne = 0;
        for (var y = 0; y < 2048; y++)
        {
            for (var x = 0; x < 2048; x++)
            {
                if (LightTransform.DitherThreshold(x, y, 0) < 0.99999f && LightTransform.DitherThreshold(x, y, 3) < 0.99999f)
                    continue;

                nearOne++;
                var (_, _, red, alpha) = LightTransform.ToBytes(1f, 1f, 1f, 1f, 1f, options, x, y);
                Assert.Equal(((byte)255, (byte)255), (red, alpha));
            }
        }

        Assert.True(nearOne > 10, $"{nearOne}");
    }
}
