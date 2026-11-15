using Library.Models;

namespace Library.Generation;

public sealed class ResourceGenerator
{
    private readonly PerlinNoise distribution;
    private readonly PerlinNoise richness;
    private readonly PerlinNoise shape;

    public ResourceGenerator(int seed)
    {
        distribution = new PerlinNoise(seed + 100);
        richness = new PerlinNoise(seed + 200);
        shape = new PerlinNoise(seed + 300);
    }

    public void Generate(World world)
    {
        int width = world.TotalCellWidth;
        int height = world.TotalCellHeight;
        int id = 1;
        int waterArea = 0;

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            ChunkCell? cell = world.GetCell(x, y);
            if (cell?.Terrain != TerrainType.Water)
                continue;
            ChunkCell waterCell = cell.Value;
            waterCell.ResourceDepositId = (ushort)id;
            SetCell(world, x, y, waterCell);
            waterArea++;
        }

        if (waterArea > 0)
        {
            double waterAmount = waterArea * 5000;
            world.ResourceDeposits.Add(new ResourceDeposit
            {
                Id = id++, Type = ResourceType.Water, CenterX = width / 2,
                CenterY = height / 2, RadiusCells = 0, AreaCells = waterArea,
                TotalAmount = waterAmount, RemainingAmount = waterAmount,
                Richness = 1, DepthMeters = 1
            });
        }

        for (int y = 128; y < height - 128; y += 256)
        for (int x = 128; x < width - 128; x += 256)
        {
            ChunkCell? source = world.GetCell(x, y);
            if (source is null || source.Value.Terrain == TerrainType.Water)
                continue;

            double field = distribution.FractalNoise(x * 0.006, y * 0.006, 3);
            if (field < 0.66)
                continue;

            ResourceType type = ChooseType(source.Value.Terrain, x, y);
            if (type == ResourceType.None)
                continue;

            double sizeMeters = type is ResourceType.Oil or ResourceType.Gas
                ? (650 + field * 650) * 0.6
                : (300 + field * 500) * 0.6;
            int radius = Math.Max(40, (int)(sizeMeters / world.CellSizeMeters / 2));
            if (HasNearbyDeposit(world, type, x, y, radius))
                continue;
            double rich = 0.35 + richness.FractalNoise(x * 0.01, y * 0.01) * 0.65;
            double total = radius * radius * 1800 * rich;
            ResourceDeposit deposit = new()
            {
                Id = id++, Type = type, CenterX = x, CenterY = y,
                RadiusCells = radius, Richness = rich,
                AreaCells = 0, TotalAmount = total,
                RemainingAmount = total,
                SizeMeters = sizeMeters,
                DepthMeters = type is ResourceType.Oil or ResourceType.Gas
                    ? 400 + rich * 1600 : 2 + rich * 40,
                ShapeSeed = depositSeed(x, y)
            };

            int area = AssignDeposit(world, deposit);
            world.ResourceDeposits.Add(new ResourceDeposit
            {
                Id = deposit.Id, Type = deposit.Type, CenterX = deposit.CenterX,
                CenterY = deposit.CenterY, RadiusCells = deposit.RadiusCells,
                Richness = deposit.Richness, AreaCells = area,
                TotalAmount = deposit.TotalAmount, RemainingAmount = deposit.RemainingAmount,
                DepthMeters = deposit.DepthMeters,
                SizeMeters = deposit.SizeMeters,
                ShapeSeed = deposit.ShapeSeed
            });
        }

        EnsureResourceTypes(world, ref id);
    }

    private void EnsureResourceTypes(World world, ref int nextId)
    {
        ResourceType[] required =
        {
            ResourceType.Stone, ResourceType.Iron, ResourceType.Copper,
            ResourceType.Oil, ResourceType.Gas
        };

        foreach (ResourceType type in required)
        {
            if (world.ResourceDeposits.Any(d => d.Type == type))
                continue;

            for (int y = 128; y < world.TotalCellHeight - 128; y += 64)
            for (int x = 128; x < world.TotalCellWidth - 128; x += 64)
            {
                ChunkCell? cell = world.GetCell(x, y);
                if (cell is null || !CanHost(type, cell.Value.Terrain))
                    continue;

                double sizeMeters = type is ResourceType.Oil or ResourceType.Gas ? 400 : 220;
                ResourceDeposit deposit = new()
                {
                    Id = nextId++, Type = type, CenterX = x, CenterY = y,
                    RadiusCells = Math.Max(24, (int)(sizeMeters / world.CellSizeMeters / 2)),
                    Richness = 0.55, TotalAmount = 250000, RemainingAmount = 250000,
                    SizeMeters = sizeMeters, DepthMeters = type is ResourceType.Oil or ResourceType.Gas ? 700 : 15,
                    ShapeSeed = depositSeed(x, y)
                };
                if (HasNearbyDeposit(world, type, x, y, deposit.RadiusCells))
                    continue;
                int area = AssignDeposit(world, deposit);
                world.ResourceDeposits.Add(new ResourceDeposit
                {
                    Id = deposit.Id, Type = deposit.Type, CenterX = deposit.CenterX,
                    CenterY = deposit.CenterY, RadiusCells = deposit.RadiusCells,
                    AreaCells = area, Richness = deposit.Richness,
                    TotalAmount = deposit.TotalAmount, RemainingAmount = deposit.RemainingAmount,
                    SizeMeters = deposit.SizeMeters, DepthMeters = deposit.DepthMeters,
                    ShapeSeed = deposit.ShapeSeed
                });
                break;
            }
        }
    }

    private static bool CanHost(ResourceType type, TerrainType terrain) => type switch
    {
        ResourceType.Stone or ResourceType.Iron or ResourceType.Copper => terrain is TerrainType.Hills or TerrainType.Mountains,
        ResourceType.Oil or ResourceType.Gas => terrain is TerrainType.Plains or TerrainType.Forest,
        _ => false
    };

    private static bool HasNearbyDeposit(World world, ResourceType type, int x, int y, int radius)
    {
        return world.ResourceDeposits.Any(deposit =>
            deposit.Type == type &&
            Math.Sqrt(Math.Pow(deposit.CenterX - x, 2) + Math.Pow(deposit.CenterY - y, 2)) <
            Math.Max(500, radius + deposit.RadiusCells + 80));
    }

    private int AssignDeposit(World world, ResourceDeposit deposit)
    {
        int area = 0;
        int r = deposit.RadiusCells;
        for (int y = deposit.CenterY - r; y <= deposit.CenterY + r; y++)
        for (int x = deposit.CenterX - r; x <= deposit.CenterX + r; x++)
        {
            if (x < 0 || y < 0 || x >= world.TotalCellWidth || y >= world.TotalCellHeight)
                continue;
            double dx = x - deposit.CenterX;
            double dy = y - deposit.CenterY;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            double angle = Math.Atan2(dy, dx);
            double edgeNoise = shape.FractalNoise(
                Math.Cos(angle) * 0.9 + deposit.ShapeSeed * 0.01,
                Math.Sin(angle) * 0.9 + deposit.ShapeSeed * 0.02, 3);
            double unevenRadius = r * (0.78 + edgeNoise * 0.42);
            if (distance > unevenRadius || world.GetCell(x, y) is not ChunkCell cell ||
                cell.Terrain == TerrainType.Water)
                continue;

            cell.ResourceDepositId = (ushort)deposit.Id;
            SetCell(world, x, y, cell);
            area++;
        }
        return area;
    }

    private ResourceType ChooseType(TerrainType terrain, int x, int y)
    {
        double selector = distribution.FractalNoise(x * 0.012 + 50, y * 0.012 + 50, 2);
        if (terrain is TerrainType.Mountains or TerrainType.Hills)
            return selector < 0.5 ? ResourceType.Stone : selector < 0.75 ? ResourceType.Iron : ResourceType.Copper;
        return selector < 0.48 ? ResourceType.Oil : ResourceType.Gas;
    }

    private static int depositSeed(int x, int y) => Math.Abs(x * 73856093 ^ y * 19349663);

    private static void SetCell(World world, int x, int y, ChunkCell value)
    {
        int tileCells = WorldTile.ChunkCount * WorldChunk.Size;
        int tileX = x / tileCells, tileY = y / tileCells;
        int localX = x % tileCells, localY = y % tileCells;
        world.Tiles[tileX, tileY].Chunks[localX / WorldChunk.Size, localY / WorldChunk.Size]
            .Cells[localX % WorldChunk.Size, localY % WorldChunk.Size] = value;
    }
}
