
using System.Collections.Generic;

namespace Library.Models;

public class WorldChunk
{
    public const int Size = 64;

    public int X { get; set; }
    public int Y { get; set; }

    public ChunkCell[,] Cells { get; set; }

    public WorldChunk()
    {
        Cells = new ChunkCell[Size, Size];
    }

    public WorldChunk(int x, int y) : this()
    {
        X = x;
        Y = y;
    }
}
