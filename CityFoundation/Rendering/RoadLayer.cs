using System;
using System.Windows;
using System.Windows.Media;
using Library.Models;

namespace CityFoundation.Rendering;

public class RoadLayer : FrameworkElement
{
    //ссылка на сеть дорог
    private RoadNetwork? network;
    //сколько пикселей занимает одна строительная клетка
    private double cellPxSize;

    public RoadLayer()
    {
        IsHitTestVisible = false;
    }

    public void Configure(RoadNetwork network, double cellPxSize, double mapWidth, double mapHeight)
    {
        this.network = network;
        this.cellPxSize = cellPxSize;
        Width = mapWidth;
        Height = mapHeight;
        //поместить для перерисовки
        InvalidateVisual();
    }

    public void Refresh() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        if (network == null || network.Count == 0) return;

        foreach (var ((x, y), cell) in network.GetCells())
            DrawRoad(dc, x, y, cell);
    }

    private void DrawRoad(DrawingContext dc, int x, int y, RoadCell cell)
    {
        double px = x * cellPxSize;
        double py = y * cellPxSize;
        double size = cellPxSize;

        // База  цветное покрытие на всю клетку (без швов между соседями)
        dc.DrawRectangle(GetRoadBrush(cell.Type), null, new Rect(px, py, size, size));

        // Разметка
        DrawMarkings(dc, px, py, size, cell);
    }

    //кисть
    private static Brush GetRoadBrush(RoadType type) => type switch
    {
        //грунтовая
        RoadType.Dirt => new SolidColorBrush(Color.FromRgb(139, 105, 70)),
        //асфальт
        RoadType.Asphalt => new SolidColorBrush(Color.FromRgb(60, 60, 65)),
        //магистраль
        RoadType.Highway => new SolidColorBrush(Color.FromRgb(40, 40, 45)),
        _ => Brushes.Transparent
    };

    private void DrawMarkings(DrawingContext dc, double px, double py, double size, RoadCell cell)
    {
        var c = cell.Connections;
        bool hasN = (c & RoadConnection.North) != 0;
        bool hasE = (c & RoadConnection.East) != 0;
        bool hasS = (c & RoadConnection.South) != 0;
        bool hasW = (c & RoadConnection.West) != 0;

        if (!hasN && !hasE && !hasS && !hasW) return;

        // Цвета разметки
        Brush centerBrush = cell.Type == RoadType.Dirt ? Brushes.SaddleBrown : Brushes.Gold;
        Brush dashBrush = cell.Type == RoadType.Dirt
            ? new SolidColorBrush(Color.FromRgb(210, 180, 140))
            : Brushes.White;

        double yellowThickness = Math.Max(0.8, size * 0.10);
        double dashThickness = Math.Max(0.5, size * 0.06);

        var yellowPen = new Pen(centerBrush, yellowThickness)
        {
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat
        };
        var dashPen = new Pen(dashBrush, dashThickness)
        {
            DashStyle = new DashStyle(new[] { 2.5, 1.5 }, 0),
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat
        };
        yellowPen.Freeze();
        dashPen.Freeze();

        bool nsThrough = hasN && hasS;
        bool ewThrough = hasE && hasW;

        // Полные прямые сегменты
        if (nsThrough) DrawVerticalLanes(dc, px, py, size, yellowPen, dashPen);
        if (ewThrough) DrawHorizontalLanes(dc, px, py, size, yellowPen, dashPen);

        // Поворот две смежные стороны, без прямой
        if (!nsThrough && !ewThrough)
        {
            if (hasN && hasE)
            {
                DrawCornerArcs(dc, new Point(px + size, py),
                    Math.PI, Math.PI / 2, size, yellowPen, dashPen);
                return;
            }
            if (hasE && hasS)
            {
                DrawCornerArcs(dc, new Point(px + size, py + size),
                    3 * Math.PI / 2, Math.PI, size, yellowPen, dashPen);
                return;
            }
            if (hasS && hasW)
            {
                DrawCornerArcs(dc, new Point(px, py + size),
                    0, -Math.PI / 2, size, yellowPen, dashPen);
                return;
            }
            if (hasW && hasN)
            {
                DrawCornerArcs(dc, new Point(px, py),
                    Math.PI / 2, 0, size, yellowPen, dashPen);
                return;
            }
        }

        // 3. Тупики и ветви T-перекрёстков
        if (hasN && !hasS) DrawHalfVerticalLanes(dc, px, py, size, true, yellowPen, dashPen);
        if (hasS && !hasN) DrawHalfVerticalLanes(dc, px, py, size, false, yellowPen, dashPen);
        if (hasE && !hasW) DrawHalfHorizontalLanes(dc, px, py, size, true, yellowPen, dashPen);
        if (hasW && !hasE) DrawHalfHorizontalLanes(dc, px, py, size, false, yellowPen, dashPen);
    }

    //Прямые
    private void DrawVerticalLanes(DrawingContext dc, double px, double py, double size,
        Pen yellowPen, Pen dashPen)
    {
        double x1 = px + size * 0.25;
        double x2 = px + size * 0.50;
        double x3 = px + size * 0.75;
        dc.DrawLine(dashPen, new Point(x1, py), new Point(x1, py + size));
        dc.DrawLine(yellowPen, new Point(x2, py), new Point(x2, py + size));
        dc.DrawLine(dashPen, new Point(x3, py), new Point(x3, py + size));
    }

    private void DrawHorizontalLanes(DrawingContext dc, double px, double py, double size,
        Pen yellowPen, Pen dashPen)
    {
        double y1 = py + size * 0.25;
        double y2 = py + size * 0.50;
        double y3 = py + size * 0.75;
        dc.DrawLine(dashPen, new Point(px, y1), new Point(px + size, y1));
        dc.DrawLine(yellowPen, new Point(px, y2), new Point(px + size, y2));
        dc.DrawLine(dashPen, new Point(px, y3), new Point(px + size, y3));
    }

    //Половинки (тупик / ветвь T)
    private void DrawHalfVerticalLanes(DrawingContext dc, double px, double py, double size,
        bool fromTop, Pen yellowPen, Pen dashPen)
    {
        double yStart = fromTop ? py : py + size;
        double yEnd = py + size / 2;
        double x1 = px + size * 0.25;
        double x2 = px + size * 0.50;
        double x3 = px + size * 0.75;
        dc.DrawLine(dashPen, new Point(x1, yStart), new Point(x1, yEnd));
        dc.DrawLine(yellowPen, new Point(x2, yStart), new Point(x2, yEnd));
        dc.DrawLine(dashPen, new Point(x3, yStart), new Point(x3, yEnd));
    }

    private void DrawHalfHorizontalLanes(DrawingContext dc, double px, double py, double size,
        bool fromRight, Pen yellowPen, Pen dashPen)
    {
        double xStart = fromRight ? px + size : px;
        double xEnd = px + size / 2;
        double y1 = py + size * 0.25;
        double y2 = py + size * 0.50;
        double y3 = py + size * 0.75;
        dc.DrawLine(dashPen, new Point(xStart, y1), new Point(xEnd, y1));
        dc.DrawLine(yellowPen, new Point(xStart, y2), new Point(xEnd, y2));
        dc.DrawLine(dashPen, new Point(xStart, y3), new Point(xEnd, y3));
    }

    // Повороты (дуги)
    private void DrawCornerArcs(DrawingContext dc, Point center,
        double angleStart, double angleEnd, double size, Pen yellowPen, Pen dashPen)
    {
        DrawArcPolyline(dc, dashPen, center, size * 0.25, angleStart, angleEnd);  // внутренняя белая
        DrawArcPolyline(dc, yellowPen, center, size * 0.50, angleStart, angleEnd);  // жёлтая по центру
        DrawArcPolyline(dc, dashPen, center, size * 0.75, angleStart, angleEnd);  // внешняя белая
    }

    private void DrawArcPolyline(DrawingContext dc, Pen pen, Point center, double radius,
        double angleStart, double angleEnd)
    {
        const int segments = 8;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            double x = center.X + radius * Math.Cos(angleStart);
            double y = center.Y + radius * Math.Sin(angleStart);
            ctx.BeginFigure(new Point(x, y), false, false);

            for (int i = 1; i <= segments; i++)
            {
                double t = i / (double)segments;
                double angle = angleStart + (angleEnd - angleStart) * t;
                double px = center.X + radius * Math.Cos(angle);
                double py = center.Y + radius * Math.Sin(angle);
                ctx.LineTo(new Point(px, py), true, true);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}