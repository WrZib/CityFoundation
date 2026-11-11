
using Library.Models;

namespace Library.Generation;

public class BiomeGenerator
{
    public TerrainType Generate(
        double elevation,
        double moisture,
        bool isLake)
    {
        if (isLake || elevation < 0.25)
            return TerrainType.Water;

        if (elevation >= 0.72)
            return TerrainType.Mountains;

        if (elevation >= 0.63)
            return TerrainType.Hills;

        if (moisture >= 0.56)
            return TerrainType.Forest;

        return TerrainType.Plains;
    }
}
