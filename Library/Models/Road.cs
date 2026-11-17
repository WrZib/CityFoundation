namespace Library.Models;

public enum RoadType
{
    Dirt,
    Ordinary,
    Highway
}

public enum RoadDirection
{
    TwoWay,
    OneWay
}

public enum RoadAxis
{
    Horizontal,
    Vertical,
    Cross
}

public readonly record struct RoadCell(int X, int Y);

public sealed class Road
{
    public RoadCell Cell { get; set; }
    public RoadType Type { get; set; }
    public RoadDirection Direction { get; set; }
    public RoadAxis Axis { get; set; }
    public RoadCell? OneWayFrom { get; set; }

    public Road() { }

    public Road(RoadCell cell, RoadType type, RoadDirection direction, RoadAxis axis = RoadAxis.Horizontal)
    {
        Cell = cell;
        Type = type;
        Direction = direction;
        Axis = axis;
    }
}
