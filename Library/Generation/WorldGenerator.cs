
using System;
using System.Threading.Tasks;
using Library.Models;

namespace Library.Generation;

public class WorldGenerator
{
    private TerrainGenerator terrainGenerator = null!;
    private BiomeGenerator biomeGenerator = null!;

    private int totalCellWidth;
    private int totalCellHeight;

    private double cellSizeMeters;

    public World Generate(
        int seed,
        int width = 3,
        int height = 3,
        double tileSizeKm = 2)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException(
                "Размер мира должен быть положительным.");

        terrainGenerator = new TerrainGenerator(seed);
        biomeGenerator = new BiomeGenerator();

        World world = new(
            seed,
            width,
            height);

        totalCellWidth = world.TotalCellWidth;
        totalCellHeight = world.TotalCellHeight;

        cellSizeMeters =
            tileSizeKm * 1000 /
            (WorldTile.ChunkCount * WorldChunk.Size);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                world.Tiles[x, y] = new WorldTile(x, y);
            }
        }

        int totalChunkColumns =
            width * WorldTile.ChunkCount;

        int totalChunkRows =
            height * WorldTile.ChunkCount;

        Parallel.For(
            0,
            totalChunkColumns,
            globalChunkX =>
            {
                int tileX =
                    globalChunkX / WorldTile.ChunkCount;

                int localChunkX =
                    globalChunkX % WorldTile.ChunkCount;

                for (int globalChunkY = 0;
                     globalChunkY < totalChunkRows;
                     globalChunkY++)
                {
                    int tileY =
                        globalChunkY / WorldTile.ChunkCount;

                    int localChunkY =
                        globalChunkY % WorldTile.ChunkCount;

                    WorldChunk chunk = GenerateChunk(
                        globalChunkX,
                        globalChunkY);

                    world.Tiles[tileX, tileY]
                        .Chunks[localChunkX, localChunkY] = chunk;
                }
            });

        return world;
    }

    private WorldChunk GenerateChunk(
        int globalChunkX,
        int globalChunkY)
    {
        WorldChunk chunk = new(
            globalChunkX,
            globalChunkY);

        int startX =
            globalChunkX * WorldChunk.Size;

        int startY =
            globalChunkY * WorldChunk.Size;

        ChunkCell[,] extendedCells = new ChunkCell[
            WorldChunk.Size + 2,
            WorldChunk.Size + 2];

        for (int x = -1; x <= WorldChunk.Size; x++)
        {
            for (int y = -1; y <= WorldChunk.Size; y++)
            {
                int worldX = Math.Clamp(
                    startX + x,
                    0,
                    totalCellWidth - 1);

                int worldY = Math.Clamp(
                    startY + y,
                    0,
                    totalCellHeight - 1);

                extendedCells[x + 1, y + 1] =
                    GenerateCell(worldX, worldY);
            }
        }

        for (int x = 0; x < WorldChunk.Size; x++)
        {
            for (int y = 0; y < WorldChunk.Size; y++)
            {
                ChunkCell cell =
                    extendedCells[x + 1, y + 1];

                float left =
                    extendedCells[x, y + 1].Height;

                float right =
                    extendedCells[x + 2, y + 1].Height;

                float top =
                    extendedCells[x + 1, y].Height;

                float bottom =
                    extendedCells[x + 1, y + 2].Height;

                double dx = (right - left) / 2.0;
                double dy = (bottom - top) / 2.0;

                double slopeRadians = Math.Atan(
                    Math.Sqrt(dx * dx + dy * dy) /
                    cellSizeMeters);

                cell.Slope = (float)(
                    slopeRadians * 180 / Math.PI);

                cell.IsBuildable =
                    cell.Terrain != TerrainType.Water &&
                    cell.Terrain != TerrainType.Mountains &&
                    cell.Slope <= 8;

                chunk.Cells[x, y] = cell;
            }
        }

        return chunk;
    }



    private ChunkCell GenerateCell(int worldX, int worldY)
    {
        double elevation =
            terrainGenerator.GetElevation(worldX, worldY);

        double moisture =
            terrainGenerator.GetMoisture(worldX, worldY);

        double lakeNoise =
            terrainGenerator.GetLakeNoise(worldX, worldY);

        // Генерация озёр.
        bool isLake =
            elevation < 0.62 &&
            lakeNoise > 0.58;

        if (isLake)
        {
            double depth =
                3 + (lakeNoise - 0.58) * 35;

            return new ChunkCell(
                (float)-depth,
                TerrainType.Water);
        }

        TerrainType terrain = biomeGenerator.Generate(
            elevation,
            moisture,
            false);

        float cellHeight =
            terrainGenerator.GetHeightMeters(elevation);

        return new ChunkCell(
            cellHeight,
            terrain);
    }


}
