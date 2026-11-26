// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class NoiseTests
{
    /// <summary>Sampling never leaves the documented 0..1 range, for every generator and both overloads.</summary>
    [Fact]
    public void EverySampleStaysWithinZeroToOne()
    {
        for (int i = -50; i <= 50; i++)
        {
            for (int j = -50; j <= 50; j++)
            {
                float x = i * 0.37f, y = j * 0.53f, z = (i + j) * 0.29f;

                AssertInRange(Noise.Perlin2D(x, y, 7));
                AssertInRange(Noise.Perlin3D(x, y, z, 7));
                AssertInRange(Noise.Value2D(x, y, 7));
                AssertInRange(Noise.Value3D(x, y, z, 7));
                AssertInRange(Noise.Fractal2D(x, y, 5, 2f, 0.5f, 7));
                AssertInRange(Noise.Fractal3D(x, y, z, 5, 2f, 0.5f, 7));
            }
        }
    }

    /// <summary>Noise is pure, so the same sample gives the same value every time and on both overloads.</summary>
    [Fact]
    public void SamplesAreDeterministic()
    {
        Assert.Equal(Noise.Perlin2D(12.34f, -5.67f, 3), Noise.Perlin2D(12.34f, -5.67f, 3));
        Assert.Equal(Noise.Fractal3D(1.5f, 2.5f, 3.5f, 4, 2f, 0.5f, 9), Noise.Fractal3D(1.5f, 2.5f, 3.5f, 4, 2f, 0.5f, 9));

        Assert.Equal(Noise.Perlin2D(4.2f, 1.1f, 5), Noise.Perlin2D(new Float2(4.2f, 1.1f), 5));
        Assert.Equal(Noise.Value3D(1.2f, 3.4f, 5.6f, 5), Noise.Value3D(new Float3(1.2f, 3.4f, 5.6f), 5));
        Assert.Equal(Noise.Fractal2D(0.7f, 9.3f, 3, 2f, 0.5f, 5), Noise.Fractal2D(new Float2(0.7f, 9.3f), 3, 2f, 0.5f, 5));
    }

    /// <summary>Gradient noise is zero at whole-number coordinates, which is the neutral 0.5 after normalization.</summary>
    [Fact]
    public void PerlinIsNeutralAtLatticePoints()
    {
        for (int x = -3; x <= 3; x++)
            for (int y = -3; y <= 3; y++)
            {
                Assert.Equal(0.5f, Noise.Perlin2D(x, y, 11));
                Assert.Equal(0.5f, Noise.Perlin3D(x, y, x - y, 11));
            }
    }

    /// <summary>A tiny step in the input moves the output only a little, so noise never teleports.</summary>
    [Fact]
    public void SamplesAreContinuous()
    {
        for (int i = 0; i < 200; i++)
        {
            float x = i * 0.61f, y = i * 0.37f;
            float step = 0.001f;

            Assert.True(MathF.Abs(Noise.Perlin2D(x + step, y, 4) - Noise.Perlin2D(x, y, 4)) < 0.01f);
            Assert.True(MathF.Abs(Noise.Value2D(x, y + step, 4) - Noise.Value2D(x, y, 4)) < 0.01f);
            Assert.True(MathF.Abs(Noise.Fractal2D(x + step, y, 4, 2f, 0.5f, 4) - Noise.Fractal2D(x, y, 4, 2f, 0.5f, 4)) < 0.05f);
        }
    }

    /// <summary>Different seeds give different patterns, so a seed is a usable variation knob.</summary>
    [Fact]
    public void SeedsChangeThePattern()
    {
        int differences = 0;
        for (int i = 0; i < 64; i++)
        {
            float x = i * 0.43f + 0.17f, y = i * 0.77f + 0.31f;
            if (MathF.Abs(Noise.Perlin2D(x, y, 1) - Noise.Perlin2D(x, y, 2)) > 0.001f)
                differences++;
        }

        Assert.True(differences > 40, $"only {differences} of 64 samples changed with the seed");
    }

    /// <summary>One octave of fractal noise is exactly the single octave it stacks.</summary>
    [Fact]
    public void OneOctaveFractalIsPlainPerlin()
    {
        for (int i = 0; i < 32; i++)
        {
            float x = i * 0.29f + 0.13f, y = i * 0.71f + 0.05f;
            Assert.Equal(Noise.Perlin2D(x, y, 6), Noise.Fractal2D(x, y, 1, 2f, 0.5f, 6));
            Assert.Equal(Noise.Perlin3D(x, y, x + y, 6), Noise.Fractal3D(x, y, x + y, 1, 2f, 0.5f, 6));
        }
    }

    /// <summary>Every octave actually shapes the result, so the octave count is a usable detail knob.</summary>
    [Fact]
    public void ExtraOctavesChangeTheSamples()
    {
        int changed = 0;
        for (int i = 0; i < 64; i++)
        {
            float x = i * 0.31f + 0.07f, y = i * 0.19f + 0.41f;
            if (MathF.Abs(Noise.Fractal2D(x, y, 1, 2f, 0.5f, 3) - Noise.Fractal2D(x, y, 5, 2f, 0.5f, 3)) > 0.0001f)
                changed++;
        }

        Assert.True(changed > 50, $"only {changed} of 64 samples responded to the octave count");
    }

    /// <summary>Finer octaves add the small, high frequency wobble that makes fractal noise look natural.</summary>
    [Fact]
    public void ExtraOctavesAddFineDetail()
    {
        // Second differences ignore the broad shape and measure only the fine wobble, which rises with every octave.
        float single = FineDetail(1), fractal = FineDetail(5);
        Assert.True(fractal > single * 2f, $"fractal detail {fractal} was not clearly above single octave {single}");
    }

    /// <summary>Bad fractal arguments are clamped instead of looping forever or dividing by zero.</summary>
    [Fact]
    public void FractalArgumentsAreGuarded()
    {
        Assert.Equal(Noise.Perlin2D(1.7f, 2.3f, 0), Noise.Fractal2D(1.7f, 2.3f, 0, 2f, 0.5f, 0));
        Assert.Equal(Noise.Perlin2D(1.7f, 2.3f, 0), Noise.Fractal2D(1.7f, 2.3f, 4, 2f, 0f, 0));
        AssertInRange(Noise.Fractal2D(1.7f, 2.3f, 1000, 2f, 0.5f, 0));
        AssertInRange(Noise.Fractal3D(1.7f, 2.3f, 3.1f, 1000, 2f, 0.5f, 0));
        AssertInRange(Noise.Fractal2D(1.7f, 2.3f, 4, 2f, -1f, 0));
    }

    private static float FineDetail(int octaves)
    {
        const float step = 0.01f;
        float total = 0f;
        for (int i = 0; i < 512; i++)
        {
            float x = i * 0.05f, y = i * 0.03f;
            float before = Noise.Fractal2D(x - step, y, octaves, 2f, 0.5f, 2);
            float middle = Noise.Fractal2D(x, y, octaves, 2f, 0.5f, 2);
            float after = Noise.Fractal2D(x + step, y, octaves, 2f, 0.5f, 2);
            total += MathF.Abs(after - 2f * middle + before);
        }
        return total;
    }

    private static void AssertInRange(float value)
    {
        Assert.True(value >= 0f && value <= 1f, $"noise sample {value} left the 0..1 range");
    }
}
