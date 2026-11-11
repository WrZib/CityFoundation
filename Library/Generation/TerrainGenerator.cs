using System;

namespace Library.Generation;

public class TerrainGenerator
{
    private readonly PerlinNoise continentalNoise;
    private readonly PerlinNoise detailNoise;
    private readonly PerlinNoise mountainNoise;
    private readonly PerlinNoise ridgeNoise;
    private readonly PerlinNoise lakeNoise;
    private readonly PerlinNoise lakeDetailNoise;

    public TerrainGenerator(int seed)
    {
        continentalNoise = new PerlinNoise(seed);
        detailNoise = new PerlinNoise(seed + 1);
        mountainNoise = new PerlinNoise(seed + 2);
        ridgeNoise = new PerlinNoise(seed + 3);
        lakeNoise = new PerlinNoise(seed + 4);
        lakeDetailNoise = new PerlinNoise(seed + 5);
    }

    public TerrainNoiseSample Sample(int x, int y)
    {
        x /= 2;
        y /= 2;

        double continental = continentalNoise.FractalNoise(
            x * 0.0018, y * 0.0018, 4, 0.5, 2.0);

        double detail = detailNoise.FractalNoise(
            x * 0.006, y * 0.006, 3, 0.5, 2.0);

        double elevation = continental * 0.82 + detail * 0.08 + 0.10;

        double mountain = mountainNoise.FractalNoise(
            x * 0.0025, y * 0.0025, 4, 0.55, 2.0);

        double ridgeValue = ridgeNoise.FractalNoise(
            x * 0.004, y * 0.004, 4, 0.5, 2.0);

        double ridge = 1.0 - Math.Abs(ridgeValue);
        ridge *= ridge;

        double mountainMask = SmoothStep(0.55, 0.86, mountain);
        double hillMask = SmoothStep(0.41, 0.58, elevation);

        elevation += mountainMask * hillMask * (0.25 + ridge * 0.35);
        elevation = Math.Clamp(elevation, 0, 1);

        double moisture = detailNoise.FractalNoise(
            x * 0.004 + 100, y * 0.004 + 100, 3);

        double lakeLarge = lakeNoise.FractalNoise(
            x * 0.0018, y * 0.0018, 4, 0.5, 2.0);

        double lakeMedium = lakeDetailNoise.FractalNoise(
            x * 0.006, y * 0.006, 3, 0.5, 2.0);

        double lakeFine = lakeNoise.FractalNoise(
            x * 0.018 + 500, y * 0.018 + 500, 2, 0.5, 2.0);

        double lake = lakeLarge * 0.65 +
                      lakeMedium * 0.25 +
                      lakeFine * 0.10;

        return new TerrainNoiseSample(elevation, moisture, lake);
    }

    public float GetHeightMeters(double elevation)
    {
        if (elevation < 0.25)
            return (float)(-5 - (0.25 - elevation) * 150);

        double normalized = Math.Clamp(
            (elevation - 0.25) / 0.75, 0, 1);

        return (float)(normalized * 3000);
    }

    public static double SmoothStep(double min, double max, double value)
    {
        double t = Math.Clamp(
            (value - min) / (max - min), 0, 1);

        return t * t * (3 - 2 * t);
    }
}

public readonly record struct TerrainNoiseSample(
    double Elevation,
    double Moisture,
    double Lake);