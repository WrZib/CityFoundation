using System;
using System.Threading.Tasks;
using Library.Models;

namespace Library.Generation;

public class WorldGenerator
{
    public World Generate(
        int seed,
        int width = 5,
        int height = 5)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException(
                "Размер мира должен быть положительным.");

        World world = new(seed, width, height);

        TerrainGenerator terrainGenerator = new(seed);
        BiomeGenerator biomeGenerator = new();

        int worldWidth = world.TotalCellWidth;
        int worldHeight = world.TotalCellHeight;

        double cellSizeMeters = world.TileSizeKm * 1000.0 /
            (WorldTile.ChunkCount * WorldChunk.Size);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                world.Tiles[x, y] = new WorldTile(x, y);
            }
        }

        ChunkCell[,] cells = new ChunkCell[worldWidth, worldHeight];

        Parallel.For(0, worldWidth, x =>
        {
            for (int y = 0; y < worldHeight; y++)
            {
                cells[x, y] = GenerateCell(
                    terrainGenerator,
                    biomeGenerator,
                    x,
                    y);
            }
        });

        Parallel.For(0, worldWidth, x =>
        {
            int leftX = Math.Max(0, x - 1);
            int rightX = Math.Min(worldWidth - 1, x + 1);

            for (int y = 0; y < worldHeight; y++)
            {
                int topY = Math.Max(0, y - 1);
                int bottomY = Math.Min(worldHeight - 1, y + 1);

                ChunkCell cell = cells[x, y];

                double dx =
                    (cells[rightX, y].Height - cells[leftX, y].Height) /
                    Math.Max(1, rightX - leftX);

                double dy =
                    (cells[x, bottomY].Height - cells[x, topY].Height) /
                    Math.Max(1, bottomY - topY);

                double slopeRadians = Math.Atan(
                    Math.Sqrt(dx * dx + dy * dy) / cellSizeMeters);

                cell.Slope = (float)(
                    slopeRadians * 180.0 / Math.PI);

                cell.IsBuildable =
                    cell.Terrain != TerrainType.Water &&
                    cell.Terrain != TerrainType.Mountains &&
                    cell.Slope <= 8;

                cells[x, y] = cell;
            }
        });

        int chunksWide = width * WorldTile.ChunkCount;
        int chunksHigh = height * WorldTile.ChunkCount;
        int totalChunks = chunksWide * chunksHigh;

        Parallel.For(0, totalChunks, index =>
        {
            int globalChunkX = index / chunksHigh;
            int globalChunkY = index % chunksHigh;

            int tileX = globalChunkX / WorldTile.ChunkCount;
            int tileY = globalChunkY / WorldTile.ChunkCount;

            int localChunkX = globalChunkX % WorldTile.ChunkCount;
            int localChunkY = globalChunkY % WorldTile.ChunkCount;

            WorldChunk chunk = new(globalChunkX, globalChunkY);

            int startX = globalChunkX * WorldChunk.Size;
            int startY = globalChunkY * WorldChunk.Size;

            for (int x = 0; x < WorldChunk.Size; x++)
            {
                for (int y = 0; y < WorldChunk.Size; y++)
                {
                    chunk.Cells[x, y] = cells[startX + x, startY + y];
                }
            }

            world.Tiles[tileX, tileY]
                .Chunks[localChunkX, localChunkY] = chunk;
        });

        new ResourceGenerator(seed).Generate(world);

        GenerateHighways(world, seed);

        return world;
    }

    private static void GenerateHighways(World world, int seed)
    {
        var random = new Random(seed ^ 0x4D4150);
        int columns = world.TotalCellWidth / ConstructionGrid.CellSize;
        int rows = world.TotalCellHeight / ConstructionGrid.CellSize;
        int horizontalRow = random.Next(rows / 4, rows * 3 / 4);
        int verticalColumn = random.Next(columns / 4, columns * 3 / 4);

        for (int x = 0; x < columns; x++)
        {
            var roadCell = new RoadCell(x, horizontalRow);
            if (world.GetCell(x * ConstructionGrid.CellSize, horizontalRow * ConstructionGrid.CellSize)?.IsBuildable == true)
                world.Roads.Add(new Road(roadCell, RoadType.Highway, RoadDirection.TwoWay, RoadAxis.Horizontal));
        }
        for (int y = 0; y < rows; y++)
        {
            var roadCell = new RoadCell(verticalColumn, y);
            if (world.GetCell(verticalColumn * ConstructionGrid.CellSize, y * ConstructionGrid.CellSize)?.IsBuildable == true &&
                !world.Roads.Any(r => r.Cell == roadCell))
                world.Roads.Add(new Road(roadCell, RoadType.Highway, RoadDirection.TwoWay, RoadAxis.Vertical));
        }
        world.RebuildRoadNetwork();
    }

    private static ChunkCell GenerateCell(
        TerrainGenerator generator,
        BiomeGenerator biomeGenerator,
        int x,
        int y)
    {
        TerrainNoiseSample sample = generator.Sample(x, y);

        bool isLake =
            sample.Elevation < 0.62 &&
            sample.Lake > 0.58;
        if (isLake)
        {
            float depth = (float)(
                3 + (sample.Lake - 0.58) * 35);

            return new ChunkCell(
                -depth,
                TerrainType.Water);
        }

        TerrainType terrain = biomeGenerator.Generate(
            sample.Elevation,
            sample.Moisture,
            false);

        float height = generator.GetHeightMeters(
            sample.Elevation);

        return new ChunkCell(height, terrain);
    }
}
