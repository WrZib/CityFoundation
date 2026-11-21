namespace Library.Models;

public struct RoadCell
{
    //тип дороги
    public RoadType Type;
    //какие из 4 сторон соединены
    public RoadConnection Connections;

    /// <summary>
    /// Клетка является частью горизонтального сегмента.
    /// Влияет на то, считаются ли соседи слева/справа соединениями.
    /// </summary>
    public bool HasHorizontal;

    /// <summary>
    /// Клетка является частью вертикального сегмента.
    /// Влияет на то, что считаются ли соседи сверху/снизу соединениями.
    /// </summary>
    public bool HasVertical;
}