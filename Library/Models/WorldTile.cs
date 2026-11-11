
namespace Library.Models;

public class WorldTile
{
    public const int ChunkCount = 16;

    public const double SizeKm = 2;

    public int X { get; set; }

    public int Y { get; set; }

    public WorldChunk[,] Chunks { get; set; }

    public WorldTile()
    {
        Chunks = new WorldChunk[ChunkCount, ChunkCount];
    }

    public WorldTile(int x, int y) : this()
    {
        X = x;
        Y = y;
    }

    public WorldChunk GetChunk(int x, int y)
    {
        return Chunks[x, y];
    }

    public void SetChunk(int x, int y, WorldChunk chunk)
    {
        Chunks[x, y] = chunk;
    }
}
