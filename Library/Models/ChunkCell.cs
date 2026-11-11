
using System;
using System.Runtime.InteropServices;

namespace Library.Models;

public struct ChunkCell
{
    private short height;
    private byte terrain;
    private byte slope;
    private byte flags;

    public float Height
    {
        get => height;
        set => height = (short)Math.Clamp(
            Math.Round(value),
            short.MinValue,
            short.MaxValue);
    }

    public TerrainType Terrain
    {
        get => (TerrainType)terrain;
        set => terrain = (byte)value;
    }

    public float Slope
    {
        get => slope;
        set => slope = (byte)Math.Clamp(
            Math.Round(value),
            0,
            90);
    }

    public bool IsBuildable
    {
        get => (flags & 1) != 0;
        set
        {
            if (value)
                flags |= 1;
            else
                flags &= 0xFE;
        }
    }

    public ChunkCell(
        float height,
        TerrainType terrain,
        float slope = 0,
        bool isBuildable = false)
    {
        this.height = 0;
        this.terrain = 0;
        this.slope = 0;
        flags = 0;

        Height = height;
        Terrain = terrain;
        Slope = slope;
        IsBuildable = isBuildable;
    }
}
