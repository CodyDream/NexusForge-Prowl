// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Seeded procedural noise, for terrain, textures, particle jitter and idle animation.
/// <para>
/// Every method is pure: the same arguments and seed always give the same value, on any machine and any
/// thread, and nothing is cached between calls. Values are normalized to 0..1 the way Unity's
/// <c>Mathf.PerlinNoise</c> is, so 0.5 is the neutral value, and a lattice point of <see cref="Perlin2D"/>
/// or <see cref="Perlin3D"/> is exactly 0.5.
/// </para>
/// <para>
/// <see cref="Perlin2D"/>, <see cref="Perlin3D"/>, <see cref="Value2D"/> and <see cref="Value3D"/> sample one
/// octave; <see cref="Fractal2D"/> and <see cref="Fractal3D"/> stack octaves of Perlin noise into natural
/// looking detail. <see cref="Fractal2D"/> with one octave is <see cref="Perlin2D"/>.
/// </para>
/// </summary>
public static class Noise
{
    /// <summary>The largest magnitude one octave of 2D gradient noise can reach.</summary>
    private const float Perlin2DMax = 0.70710678f;

    /// <summary>The largest magnitude one octave of 3D gradient noise can reach.</summary>
    private const float Perlin3DMax = 0.86602540f;

    private const float InvSqrt2 = 0.70710678f;

    /// <summary>The most octaves a fractal sample runs, so a bad argument cannot stall a frame.</summary>
    private const int MaxOctaves = 12;

    // 8 unit gradients in 45 degree steps.
    private static readonly float[] s_grad2X = [1f, InvSqrt2, 0f, -InvSqrt2, -1f, -InvSqrt2, 0f, InvSqrt2];
    private static readonly float[] s_grad2Y = [0f, InvSqrt2, 1f, InvSqrt2, 0f, -InvSqrt2, -1f, -InvSqrt2];

    // The 12 edge midpoints of a cube, normalized to unit length.
    private static readonly float[] s_grad3X = [InvSqrt2, -InvSqrt2, InvSqrt2, -InvSqrt2, InvSqrt2, -InvSqrt2, InvSqrt2, -InvSqrt2, 0f, 0f, 0f, 0f];
    private static readonly float[] s_grad3Y = [InvSqrt2, InvSqrt2, -InvSqrt2, -InvSqrt2, 0f, 0f, 0f, 0f, InvSqrt2, -InvSqrt2, InvSqrt2, -InvSqrt2];
    private static readonly float[] s_grad3Z = [0f, 0f, 0f, 0f, InvSqrt2, InvSqrt2, -InvSqrt2, -InvSqrt2, InvSqrt2, InvSqrt2, -InvSqrt2, -InvSqrt2];

    /// <summary>
    /// One octave of 2D Perlin noise at <paramref name="x"/>, <paramref name="y"/>, normalized to 0..1.
    /// Whole-number coordinates are exactly 0.5, so <c>Perlin2D(x + 0.5f, y + 0.5f)</c> centers the features
    /// on the integers.
    /// </summary>
    public static float Perlin2D(float x, float y, int seed = 0)
    {
        int xi = FastFloor(x), yi = FastFloor(y);
        float xf = x - xi, yf = y - yi;

        float u = Fade(xf), v = Fade(yf);

        float n00 = Dot2(Hash(xi, yi, seed), xf, yf);
        float n10 = Dot2(Hash(xi + 1, yi, seed), xf - 1f, yf);
        float n01 = Dot2(Hash(xi, yi + 1, seed), xf, yf - 1f);
        float n11 = Dot2(Hash(xi + 1, yi + 1, seed), xf - 1f, yf - 1f);

        float bottom = Lerp(n00, n10, u);
        float top = Lerp(n01, n11, u);
        float n = Lerp(bottom, top, v);

        return Normalize(n, Perlin2DMax);
    }

    /// <inheritdoc cref="Perlin2D(float, float, int)"/>
    public static float Perlin2D(Float2 point, int seed = 0) => Perlin2D(point.X, point.Y, seed);

    /// <summary>
    /// One octave of 3D Perlin noise at <paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>,
    /// normalized to 0..1. Whole-number coordinates are exactly 0.5.
    /// </summary>
    public static float Perlin3D(float x, float y, float z, int seed = 0)
    {
        int xi = FastFloor(x), yi = FastFloor(y), zi = FastFloor(z);
        float xf = x - xi, yf = y - yi, zf = z - zi;

        float u = Fade(xf), v = Fade(yf), w = Fade(zf);

        float n000 = Dot3(Hash(xi, yi, zi, seed), xf, yf, zf);
        float n100 = Dot3(Hash(xi + 1, yi, zi, seed), xf - 1f, yf, zf);
        float n010 = Dot3(Hash(xi, yi + 1, zi, seed), xf, yf - 1f, zf);
        float n110 = Dot3(Hash(xi + 1, yi + 1, zi, seed), xf - 1f, yf - 1f, zf);
        float n001 = Dot3(Hash(xi, yi, zi + 1, seed), xf, yf, zf - 1f);
        float n101 = Dot3(Hash(xi + 1, yi, zi + 1, seed), xf - 1f, yf, zf - 1f);
        float n011 = Dot3(Hash(xi, yi + 1, zi + 1, seed), xf, yf - 1f, zf - 1f);
        float n111 = Dot3(Hash(xi + 1, yi + 1, zi + 1, seed), xf - 1f, yf - 1f, zf - 1f);

        float x00 = Lerp(n000, n100, u);
        float x10 = Lerp(n010, n110, u);
        float x01 = Lerp(n001, n101, u);
        float x11 = Lerp(n011, n111, u);

        float y0 = Lerp(x00, x10, v);
        float y1 = Lerp(x01, x11, v);
        float n = Lerp(y0, y1, w);

        return Normalize(n, Perlin3DMax);
    }

    /// <inheritdoc cref="Perlin3D(float, float, float, int)"/>
    public static float Perlin3D(Float3 point, int seed = 0) => Perlin3D(point.X, point.Y, point.Z, seed);

    /// <summary>One octave of 2D value noise at <paramref name="x"/>, <paramref name="y"/>, in 0..1.</summary>
    public static float Value2D(float x, float y, int seed = 0)
    {
        int xi = FastFloor(x), yi = FastFloor(y);
        float u = Fade(x - xi), v = Fade(y - yi);

        float v00 = Unit(Hash(xi, yi, seed));
        float v10 = Unit(Hash(xi + 1, yi, seed));
        float v01 = Unit(Hash(xi, yi + 1, seed));
        float v11 = Unit(Hash(xi + 1, yi + 1, seed));

        return Lerp(Lerp(v00, v10, u), Lerp(v01, v11, u), v);
    }

    /// <inheritdoc cref="Value2D(float, float, int)"/>
    public static float Value2D(Float2 point, int seed = 0) => Value2D(point.X, point.Y, seed);

    /// <summary>One octave of 3D value noise at <paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>, in 0..1.</summary>
    public static float Value3D(float x, float y, float z, int seed = 0)
    {
        int xi = FastFloor(x), yi = FastFloor(y), zi = FastFloor(z);
        float u = Fade(x - xi), v = Fade(y - yi), w = Fade(z - zi);

        float v000 = Unit(Hash(xi, yi, zi, seed));
        float v100 = Unit(Hash(xi + 1, yi, zi, seed));
        float v010 = Unit(Hash(xi, yi + 1, zi, seed));
        float v110 = Unit(Hash(xi + 1, yi + 1, zi, seed));
        float v001 = Unit(Hash(xi, yi, zi + 1, seed));
        float v101 = Unit(Hash(xi + 1, yi, zi + 1, seed));
        float v011 = Unit(Hash(xi, yi + 1, zi + 1, seed));
        float v111 = Unit(Hash(xi + 1, yi + 1, zi + 1, seed));

        float x00 = Lerp(v000, v100, u);
        float x10 = Lerp(v010, v110, u);
        float x01 = Lerp(v001, v101, u);
        float x11 = Lerp(v011, v111, u);

        return Lerp(Lerp(x00, x10, v), Lerp(x01, x11, v), w);
    }

    /// <inheritdoc cref="Value3D(float, float, float, int)"/>
    public static float Value3D(Float3 point, int seed = 0) => Value3D(point.X, point.Y, point.Z, seed);

    /// <summary>
    /// Fractal (fBm) 2D noise: <paramref name="octaves"/> layers of <see cref="Perlin2D"/>, each
    /// <paramref name="lacunarity"/> times finer and <paramref name="gain"/> times fainter than the last,
    /// averaged back into 0..1. One octave is exactly <see cref="Perlin2D"/>.
    /// </summary>
    /// <param name="octaves">Layer count, clamped to 1..12 so a bad argument cannot stall a frame.</param>
    /// <param name="lacunarity">Frequency multiplier between layers. 2 doubles the detail each layer.</param>
    /// <param name="gain">Amplitude multiplier between layers. 0.5 is the usual natural look.</param>
    public static float Fractal2D(float x, float y, int octaves = 4, float lacunarity = 2f, float gain = 0.5f, int seed = 0)
    {
        octaves = Math.Clamp(octaves, 1, MaxOctaves);

        float sum = 0f, amplitude = 1f, total = 0f, frequency = 1f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Perlin2D(x * frequency, y * frequency, seed + i * 1013) * amplitude;
            total += amplitude;

            amplitude *= gain;
            frequency *= lacunarity;
        }

        return total > 0f ? Math.Clamp(sum / total, 0f, 1f) : 0.5f;
    }

    /// <inheritdoc cref="Fractal2D(float, float, int, float, float, int)"/>
    public static float Fractal2D(Float2 point, int octaves = 4, float lacunarity = 2f, float gain = 0.5f, int seed = 0)
        => Fractal2D(point.X, point.Y, octaves, lacunarity, gain, seed);

    /// <inheritdoc cref="Fractal2D(float, float, int, float, float, int)"/>
    public static float Fractal3D(float x, float y, float z, int octaves = 4, float lacunarity = 2f, float gain = 0.5f, int seed = 0)
    {
        octaves = Math.Clamp(octaves, 1, MaxOctaves);

        float sum = 0f, amplitude = 1f, total = 0f, frequency = 1f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Perlin3D(x * frequency, y * frequency, z * frequency, seed + i * 1013) * amplitude;
            total += amplitude;

            amplitude *= gain;
            frequency *= lacunarity;
        }

        return total > 0f ? Math.Clamp(sum / total, 0f, 1f) : 0.5f;
    }

    /// <inheritdoc cref="Fractal3D(float, float, float, int, float, float, int)"/>
    public static float Fractal3D(Float3 point, int octaves = 4, float lacunarity = 2f, float gain = 0.5f, int seed = 0)
        => Fractal3D(point.X, point.Y, point.Z, octaves, lacunarity, gain, seed);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Dot2(uint hash, float x, float y)
    {
        int i = (int)(hash & 7);
        return s_grad2X[i] * x + s_grad2Y[i] * y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Dot3(uint hash, float x, float y, float z)
    {
        int i = (int)(hash % 12);
        return s_grad3X[i] * x + s_grad3Y[i] * y + s_grad3Z[i] * z;
    }

    /// <summary>Maps a gradient noise result of [-max, max] onto 0..1, clamped for the rare overshoot.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Normalize(float noise, float max) => Math.Clamp(0.5f + 0.5f * (noise / max), 0f, 1f);

    /// <summary>A hash as a value in 0..1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Unit(uint hash) => (hash >> 8) * (1f / 16777216f);

    /// <summary>Perlin's quintic fade curve: 0 at 0, 1 at 1, with zero first and second derivatives at both ends.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FastFloor(float value)
    {
        int i = (int)value;
        return value < i ? i - 1 : i;
    }

    /// <summary>A 2D lattice hash. Mixed so neighbouring cells and seeds are unrelated.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)seed * 0xCB1AB31Fu;
            h ^= h >> 13;
            h *= 0x85EBCA6Bu;
            h ^= h >> 16;
            return h;
        }
    }

    /// <summary>A 3D lattice hash. Mixed so neighbouring cells and seeds are unrelated.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)z * 0xC5A9B7D1u ^ (uint)seed * 0xCB1AB31Fu;
            h ^= h >> 13;
            h *= 0x85EBCA6Bu;
            h ^= h >> 16;
            return h;
        }
    }
}
