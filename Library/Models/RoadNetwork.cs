namespace Library.Models;

/// <summary>Локальная дорожная сеть. Готовит данные для поиска маршрутов людей.</summary>
public sealed class RoadNetwork
{
    private readonly Dictionary<RoadCell, Road> roads = new();

    public IReadOnlyDictionary<RoadCell, Road> Roads => roads;

    public void Rebuild(IEnumerable<Road> source)
    {
        roads.Clear();
        foreach (var road in source)
            roads[road.Cell] = road;
    }

    public IEnumerable<RoadCell> GetNeighbours(RoadCell cell)
    {
        foreach (var next in new[]
        {
            new RoadCell(cell.X - 1, cell.Y), new RoadCell(cell.X + 1, cell.Y),
            new RoadCell(cell.X, cell.Y - 1), new RoadCell(cell.X, cell.Y + 1)
        })
        {
            if (!roads.TryGetValue(next, out var road)) continue;
            if (road.Direction == RoadDirection.TwoWay || road.OneWayFrom is null || road.OneWayFrom.Value == cell)
                yield return next;
        }
    }

    public IReadOnlyList<RoadCell> FindShortestRoute(RoadCell start, RoadCell end)
    {
        var queue = new Queue<RoadCell>();
        var previous = new Dictionary<RoadCell, RoadCell?> { [start] = null };
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == end) break;
            foreach (var next in GetNeighbours(current))
                if (!previous.ContainsKey(next)) { previous[next] = current; queue.Enqueue(next); }
        }
        if (!previous.ContainsKey(end)) return Array.Empty<RoadCell>();
        var result = new List<RoadCell>();
        for (RoadCell? at = end; at is not null; at = previous[at.Value]) result.Add(at.Value);
        result.Reverse();
        return result;
    }
}
