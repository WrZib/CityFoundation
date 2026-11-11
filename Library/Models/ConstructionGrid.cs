using System;

namespace Library.Models;

public class ConstructionGrid
{
    private readonly World world;

    public const int CellSize = 10;

    public ConstructionGrid(World world)
    {
        this.world = world;
    }

    public int Columns => (int)Math.Ceiling(
        world.TotalCellWidth / (double)CellSize);

    public int Rows => (int)Math.Ceiling(
        world.TotalCellHeight / (double)CellSize);

    public bool IsInsideWorld(int x, int y)
    {
        return x >= 0 &&
               y >= 0 &&
               x < world.TotalCellWidth &&
               y < world.TotalCellHeight;
    }

    public ChunkCell GetCell(int x, int y)
    {
        if (!IsInsideWorld(x, y))
            throw new ArgumentOutOfRangeException();

        int cellsPerTile =
            WorldTile.ChunkCount * WorldChunk.Size;

        int tileX = x / cellsPerTile;
        int tileY = y / cellsPerTile;

        int localX = x % cellsPerTile;
        int localY = y % cellsPerTile;

        int chunkX = localX / WorldChunk.Size;
        int chunkY = localY / WorldChunk.Size;

        int cellX = localX % WorldChunk.Size;
        int cellY = localY % WorldChunk.Size;

        return world.Tiles[tileX, tileY]
            .Chunks[chunkX, chunkY]
            .Cells[cellX, cellY];
    }

    public bool CanBuild(int x, int y)
    {
        return IsInsideWorld(x, y) &&
               GetCell(x, y).IsBuildable;
    }

    public (int X, int Y) GetConstructionCell(
        int worldX,
        int worldY)
    {
        return (
            worldX / CellSize,
            worldY / CellSize);
    }

    public (int X, int Y) GetWorldPosition(
        int gridX,
        int gridY)
    {
        return (
            gridX * CellSize,
            gridY * CellSize);
    }

    public double CellSizeMeters =>
        world.TileSizeKm * 1000 /
        (WorldTile.ChunkCount * WorldChunk.Size);

    public double ConstructionCellSizeMeters =>
        CellSize * CellSizeMeters;
}