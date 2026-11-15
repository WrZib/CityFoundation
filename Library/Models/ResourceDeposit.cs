namespace Library.Models;

public sealed class ResourceDeposit
{
    public int Id { get; init; }
    public ResourceType Type { get; init; }
    public int CenterX { get; init; }
    public int CenterY { get; init; }
    public int RadiusCells { get; init; }
    public int AreaCells { get; init; }
    public double TotalAmount { get; init; }
    public double RemainingAmount { get; set; }
    public double Richness { get; init; }
    public double DepthMeters { get; init; }
    public int ShapeSeed { get; init; }
    public double SizeMeters { get; init; }
}
