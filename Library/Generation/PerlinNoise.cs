
using System;

namespace Library.Generation;

public class PerlinNoise
{
    private readonly int[] permutation = new int[512];

    public PerlinNoise(int seed)
    {
        int[] values = new int[256];

        for (int i = 0; i < values.Length; i++)
            values[i] = i;

        Random random = new Random(seed);

        for (int i = values.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);

            (values[i], values[j]) = (values[j], values[i]);
        }

        for (int i = 0; i < permutation.Length; i++)
            permutation[i] = values[i % 256];
    }

    public double Noise(double x, double y)
    {
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);

        int xi = x0 & 255;
        int yi = y0 & 255;

        double xf = x - x0;
        double yf = y - y0;

        double u = Fade(xf);
        double v = Fade(yf);

        int aa = permutation[permutation[xi] + yi];
        int ab = permutation[permutation[xi] + yi + 1];
        int ba = permutation[permutation[xi + 1] + yi];
        int bb = permutation[permutation[xi + 1] + yi + 1];

        double top = Lerp(
            Gradient(aa, xf, yf),
            Gradient(ba, xf - 1, yf),
            u);

        double bottom = Lerp(
            Gradient(ab, xf, yf - 1),
            Gradient(bb, xf - 1, yf - 1),
            u);

        return Lerp(top, bottom, v) * 0.70710678;
    }

    public double FractalNoise(
        double x,
        double y,
        int octaves = 5,
        double persistence = 0.5,
        double lacunarity = 2.0)
    {
        double total = 0;
        double amplitude = 1;
        double frequency = 1;
        double maxAmplitude = 0;

        for (int i = 0; i < octaves; i++)
        {
            total += Noise(
                x * frequency,
                y * frequency) * amplitude;

            maxAmplitude += amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return (total / maxAmplitude + 1) / 2;
    }

    private double Fade(double t)
    {
        return t * t * t * (t * (t * 6 - 15) + 10);
    }

    private double Lerp(double a, double b, double t)
    {
        return a + (b - a) * t;
    }

    private double Gradient(int hash, double x, double y)
    {
        return (hash & 3) switch
        {
            0 => x + y,
            1 => -x + y,
            2 => x - y,
            _ => -x - y
        };
    }
}
