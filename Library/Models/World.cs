
namespace Library.Models;

public class World
{
    public int Seed { get; set; }

    public int Width { get; set; } = 3;

    public int Height { get; set; } = 3;

    public double TileSizeKm => WorldTile.SizeKm;

    public WorldTile[,] Tiles { get; set; }

    public int ChunksPerTile => WorldTile.ChunkCount;

    public int CellsPerChunk => WorldChunk.Size;

    public int TotalCellWidth => Width * ChunksPerTile * CellsPerChunk;

    public int TotalCellHeight => Height * ChunksPerTile * CellsPerChunk;

    public World()
    {
        Tiles = new WorldTile[Width, Height];
    }

    public World(int seed, int width = 3, int height = 3)
    {
        Seed = seed;
        Width = width;
        Height = height;

        Tiles = new WorldTile[width, height];
    }

    public ChunkCell? GetCell(int x, int y)
    {
        if (x < 0 || y < 0 ||
            x >= TotalCellWidth || y >= TotalCellHeight)
            return null;

        int tileX = x / (ChunksPerTile * CellsPerChunk);
        int tileY = y / (ChunksPerTile * CellsPerChunk);

        int localTileX = x % (ChunksPerTile * CellsPerChunk);
        int localTileY = y % (ChunksPerTile * CellsPerChunk);

        int chunkX = localTileX / CellsPerChunk;
        int chunkY = localTileY / CellsPerChunk;

        int cellX = localTileX % CellsPerChunk;
        int cellY = localTileY % CellsPerChunk;

        WorldChunk chunk = Tiles[tileX, tileY]
            .Chunks[chunkX, chunkY];

        return chunk.Cells[cellX, cellY];
    }
}
