using System;
using System.Collections.Generic;

namespace Library.Models;

public class RoadNetwork
{
    private readonly Dictionary<(int X, int Y), RoadCell> cells = new();

    //возвращает число дорог
    public int Count => cells.Count;

    public bool HasRoad(int x, int y) => cells.ContainsKey((x, y));

    public RoadCell? GetRoad(int x, int y) =>
        cells.TryGetValue((x, y), out var cell) ? cell : null;

    //перечисление всех дорог
    public IEnumerable<((int X, int Y) Position, RoadCell Cell)> GetCells()
    {
        foreach (var kvp in cells)
            yield return (kvp.Key, kvp.Value);
    }

    public void DrawSegment(int x1, int y1, int x2, int y2, RoadType type)
    {
        //если инстурмент не выбран то не рисуем
        if (type == RoadType.None) return;

        int dx = x2 - x1;
        int dy = y2 - y1;
        //рисование горизонтальной или вертикальной линии
        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            int minX = Math.Min(x1, x2);
            int maxX = Math.Max(x1, x2);
            for (int x = minX; x <= maxX; x++)
                AddHorizontal(x, y1, type);
        }
        else
        {
            int minY = Math.Min(y1, y2);
            int maxY = Math.Max(y1, y2);
            for (int y = minY; y <= maxY; y++)
                AddVertical(x1, y, type);
        }
    }

    // Добавляет/обновляет клетку как часть ГОРИЗОНТАЛЬНОГО сегмента.

    public void AddHorizontal(int x, int y, RoadType type)
    {
        if (type == RoadType.None) { RemoveRoad(x, y); return; }

        RoadCell cell;
        if (cells.TryGetValue((x, y), out var existing))
        {
            cell = existing;
            cell.Type = type;
            cell.HasHorizontal = true;
        }
        else
        {
            cell = new RoadCell { Type = type, HasHorizontal = true };
        }

        cells[(x, y)] = cell;
        UpdateConnectionsAround(x, y);
    }

    // Добавляет/обновляет клетку как часть ВЕРТИКАЛЬНОГО сегмента.

    public void AddVertical(int x, int y, RoadType type)
    {
        if (type == RoadType.None) { RemoveRoad(x, y); return; }

        RoadCell cell;
        if (cells.TryGetValue((x, y), out var existing))
        {
            cell = existing;
            cell.Type = type;
            cell.HasVertical = true;
        }
        else
        {
            cell = new RoadCell { Type = type, HasVertical = true };
        }

        cells[(x, y)] = cell;
        UpdateConnectionsAround(x, y);
    }

    /// <summary>
    /// Оставляет клетку с обоими флагами (крест). Для совместимости.
    /// </summary>
    public void SetRoad(int x, int y, RoadType type)
    {
        //если None то команда стереть дорогу
        if (type == RoadType.None) { RemoveRoad(x, y); return; }

        RoadCell cell;
        if (cells.TryGetValue((x, y), out var existing))
            cell = existing;
        else
            cell = new RoadCell();

        cell.Type = type;
        cell.HasHorizontal = true;
        cell.HasVertical = true;
        cells[(x, y)] = cell;
        //обновляем соседей
        UpdateConnectionsAround(x, y);
    }

    public void RemoveRoad(int x, int y)
    {
        if (cells.Remove((x, y)))
            UpdateConnectionsAround(x, y);
    }

    public void Clear() => cells.Clear();

    //пересчет всех связей
    private void UpdateConnectionsAround(int x, int y)
    {
        UpdateConnection(x, y);
        UpdateConnection(x - 1, y);
        UpdateConnection(x + 1, y);
        UpdateConnection(x, y - 1);
        UpdateConnection(x, y + 1);
    }

    private void UpdateConnection(int x, int y)
    {
        if (!cells.TryGetValue((x, y), out var cell)) return;

        var conn = RoadConnection.None;

        // Вертикальные связи - только если ОБЕ клетки часть вертикальных сегментов
        if (cell.HasVertical)
        {
            if (cells.TryGetValue((x, y - 1), out var n) && n.HasVertical)
                conn |= RoadConnection.North;
            if (cells.TryGetValue((x, y + 1), out var s) && s.HasVertical)
                conn |= RoadConnection.South;
        }

        // Горизонтальные связи - только если ОБЕ клетки часть горизонтальных сегментов
        if (cell.HasHorizontal)
        {
            if (cells.TryGetValue((x + 1, y), out var e) && e.HasHorizontal)
                conn |= RoadConnection.East;
            if (cells.TryGetValue((x - 1, y), out var w) && w.HasHorizontal)
                conn |= RoadConnection.West;
        }

        cell.Connections = conn;
        cells[(x, y)] = cell;
    }
    public void RemoveHorizontal(int x, int y)
    {
        if (!cells.TryGetValue((x, y), out var cell)) return;

        cell.HasHorizontal = false;

        if (!cell.HasVertical)
            cells.Remove((x, y));
        else
            cells[(x, y)] = cell;

        UpdateConnectionsAround(x, y);
    }

    public void RemoveVertical(int x, int y)
    {
        if (!cells.TryGetValue((x, y), out var cell)) return;

        cell.HasVertical = false;

        if (!cell.HasHorizontal)
            cells.Remove((x, y));
        else
            cells[(x, y)] = cell;

        UpdateConnectionsAround(x, y);
    }

    public void EraseSegment(int x1, int y1, int x2, int y2)
    {
        int dx = x2 - x1;
        int dy = y2 - y1;

        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            int minX = Math.Min(x1, x2);
            int maxX = Math.Max(x1, x2);
            for (int x = minX; x <= maxX; x++)
                RemoveHorizontal(x, y1);
        }
        else
        {
            int minY = Math.Min(y1, y2);
            int maxY = Math.Max(y1, y2);
            for (int y = minY; y <= maxY; y++)
                RemoveVertical(x1, y);
        }
    }
}