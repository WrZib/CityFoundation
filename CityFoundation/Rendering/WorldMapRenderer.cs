
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Library.Models;

namespace CityFoundation.Rendering;

public class WorldMapRenderer
{
    private const double CellPixelSize = 0.5;

    private const double MinZoom = 0.5;
    private const double MaxZoom = 24;
    private const double InitialZoom = 3;

    private const double TileGridZoom = 0.7;
    private const double ConstructionGridZoom = 3;

    private readonly Canvas viewport;
    private readonly Canvas content;

    private readonly ScaleTransform scale = new();
    private readonly TranslateTransform translate = new();

    private Rectangle? constructionGrid;
    private Panel? tileGrid;

    private World? currentWorld;

    private bool isDragging;
    private Point lastMousePosition;

    public WorldMapRenderer(Canvas viewport, Canvas content)
    {
        this.viewport = viewport;
        this.content = content;

        TransformGroup transform = new();
        transform.Children.Add(scale);
        transform.Children.Add(translate);

        content.RenderTransform = transform;

        viewport.ClipToBounds = true;

        RenderOptions.SetBitmapScalingMode(
            content,
            BitmapScalingMode.Fant);

        viewport.MouseLeftButtonDown += OnMouseDown;
        viewport.MouseLeftButtonUp += OnMouseUp;
        viewport.MouseMove += OnMouseMove;
        viewport.MouseWheel += OnMouseWheel;

        viewport.SizeChanged += (_, _) =>
        {
            if (currentWorld != null)
                CenterMap();
        };
    }

    public void SetWorld(World world)
    {
        currentWorld = world;

        scale.ScaleX = InitialZoom;
        scale.ScaleY = InitialZoom;

        Draw();

        viewport.Dispatcher.BeginInvoke(
            new Action(CenterMap),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Draw()
    {
        if (currentWorld == null)
            return;

        content.Children.Clear();

        constructionGrid = null;
        tileGrid = null;

        int tileCells =
            WorldTile.ChunkCount * WorldChunk.Size;

        double tileSize = tileCells * CellPixelSize;

        content.Width = currentWorld.TotalCellWidth * CellPixelSize;
        content.Height = currentWorld.TotalCellHeight * CellPixelSize;

        // Отрисовка тайлов
        for (int x = 0; x < currentWorld.Width; x++)
        {
            for (int y = 0; y < currentWorld.Height; y++)
            {
                WriteableBitmap bitmap =
                    CreateTileBitmap(currentWorld.Tiles[x, y]);

                Image image = new()
                {
                    Source = bitmap,
                    Width = tileSize,
                    Height = tileSize,
                    Stretch = Stretch.Fill,
                    IsHitTestVisible = false,
                    SnapsToDevicePixels = true
                };

                RenderOptions.SetBitmapScalingMode(
                    image,
                    BitmapScalingMode.Fant);

                Canvas.SetLeft(image, x * tileSize);
                Canvas.SetTop(image, y * tileSize);

                content.Children.Add(image);
            }
        }

        DrawTileGrid(tileSize);
        DrawConstructionGrid();

        UpdateLod();
    }

    // Создание текстуры тайла
    private WriteableBitmap CreateTileBitmap(WorldTile tile)
    {
        int size = WorldTile.ChunkCount * WorldChunk.Size;

        int stride = size * 4;
        byte[] pixels = new byte[size * stride];

        for (int y = 0; y < size; y++)
        {
            int chunkY = y / WorldChunk.Size;
            int cellY = y % WorldChunk.Size;

            for (int x = 0; x < size; x++)
            {
                int chunkX = x / WorldChunk.Size;
                int cellX = x % WorldChunk.Size;

                ChunkCell cell =
                    tile.Chunks[chunkX, chunkY]
                        .Cells[cellX, cellY];

                Color color = GetTerrainColor(cell);

                color = ApplySlopeShading(
                    color,
                    cell.Slope,
                    cell.Height);

                int index = y * stride + x * 4;

                pixels[index] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = 255;
            }
        }

        WriteableBitmap bitmap = new(
            size,
            size,
            96,
            96,
            PixelFormats.Bgra32,
            null);

        bitmap.WritePixels(
            new Int32Rect(0, 0, size, size),
            pixels,
            stride,
            0);

        bitmap.Freeze();

        return bitmap;
    }

    // Цвет биома
    private Color GetTerrainColor(ChunkCell cell)
    {
        return cell.Terrain switch
        {
            TerrainType.Water => GetWaterColor(cell.Height),
            TerrainType.Plains => GetPlainsColor(cell.Height),
            TerrainType.Forest => GetForestColor(cell.Height),
            TerrainType.Hills => GetHillColor(cell.Height),
            TerrainType.Mountains => GetMountainColor(cell.Height),
            _ => Colors.Magenta
        };
    }

    private Color GetWaterColor(float height)
    {
        double depth = Math.Clamp(Math.Abs(height) / 45.0, 0, 1);

        return Blend(
            Color.FromRgb(125, 190, 195),
            Color.FromRgb(20, 55, 105),
            depth);
    }

    private Color GetPlainsColor(float height)
    {
        double amount = Math.Clamp(height / 3000.0, 0, 1);

        return Blend(
            Color.FromRgb(139, 174, 99),
            Color.FromRgb(157, 181, 112),
            amount);
    }

    private Color GetForestColor(float height)
    {
        double amount = Math.Clamp(height / 3000.0, 0, 1);

        return Blend(
            Color.FromRgb(39, 91, 58),
            Color.FromRgb(66, 119, 72),
            amount);
    }

    private Color GetHillColor(float height)
    {
        double amount = Math.Clamp(height / 3000.0, 0, 1);

        return Blend(
            Color.FromRgb(139, 119, 85),
            Color.FromRgb(176, 155, 119),
            amount);
    }

    private Color GetMountainColor(float height)
    {
        double elevation = Math.Clamp(height / 3000.0, 0, 1);

        if (elevation < 0.65)
        {
            return Blend(
                Color.FromRgb(75, 78, 80),
                Color.FromRgb(125, 128, 130),
                elevation / 0.65);
        }

        return Blend(
            Color.FromRgb(125, 128, 130),
            Color.FromRgb(205, 207, 208),
            (elevation - 0.65) / 0.35);
    }

    private Color ApplySlopeShading(
        Color color,
        float slope,
        float height)
    {
        if (height < 0)
            return color;

        double shade = Math.Clamp(slope / 60.0, 0, 0.22);

        return Blend(
            color,
            Color.FromRgb(45, 45, 45),
            shade);
    }

    private Color Blend(Color first, Color second, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);

        return Color.FromRgb(
            (byte)(first.R + (second.R - first.R) * amount),
            (byte)(first.G + (second.G - first.G) * amount),
            (byte)(first.B + (second.B - first.B) * amount));
    }

    // Сетка тайлов
    private void DrawTileGrid(double tileSize)
    {
        if (currentWorld == null)
            return;

        Canvas grid = new()
        {
            Width = content.Width,
            Height = content.Height,
            IsHitTestVisible = false
        };

        SolidColorBrush brush = new(
            Color.FromArgb(210, 225, 230, 215));

        for (int x = 0; x <= currentWorld.Width; x++)
        {
            Line line = new()
            {
                X1 = x * tileSize,
                X2 = x * tileSize,
                Y1 = 0,
                Y2 = content.Height,
                Stroke = brush,
                StrokeThickness = 0.22
            };

            grid.Children.Add(line);
        }

        for (int y = 0; y <= currentWorld.Height; y++)
        {
            Line line = new()
            {
                X1 = 0,
                X2 = content.Width,
                Y1 = y * tileSize,
                Y2 = y * tileSize,
                Stroke = brush,
                StrokeThickness = 0.22
            };

            grid.Children.Add(line);
        }

        Panel.SetZIndex(grid, 20);

        content.Children.Add(grid);
        tileGrid = grid;
    }

    // Строительная сетка
    private void DrawConstructionGrid()
    {
        double cellSize =
            ConstructionGrid.CellSize * CellPixelSize;

        Pen pen = new(
            new SolidColorBrush(
                Color.FromArgb(105, 20, 29, 34)),
            0.16);

        pen.Freeze();

        GeometryGroup geometry = new();

        geometry.Children.Add(
            new LineGeometry(
                new Point(cellSize, 0),
                new Point(cellSize, cellSize)));

        geometry.Children.Add(
            new LineGeometry(
                new Point(0, cellSize),
                new Point(cellSize, cellSize)));

        geometry.Freeze();

        GeometryDrawing drawing = new(null, pen, geometry);
        drawing.Freeze();

        DrawingBrush brush = new(drawing)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            Viewbox = new Rect(0, 0, cellSize, cellSize),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, cellSize, cellSize),
            ViewportUnits = BrushMappingMode.Absolute
        };

        brush.Freeze();

        constructionGrid = new Rectangle
        {
            Width = content.Width,
            Height = content.Height,
            Fill = brush,
            IsHitTestVisible = false
        };

        Panel.SetZIndex(constructionGrid, 10);

        content.Children.Add(constructionGrid);
    }

    // LOD сеток
    private void UpdateLod()
    {
        double zoom = scale.ScaleX;

        if (tileGrid != null)
        {
            tileGrid.Visibility =
                zoom >= TileGridZoom
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        if (constructionGrid != null)
        {
            constructionGrid.Visibility =
                zoom >= ConstructionGridZoom
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }

    private void CenterMap()
    {
        if (currentWorld == null)
            return;

        translate.X =
            (viewport.ActualWidth -
             content.Width * scale.ScaleX) / 2;

        translate.Y =
            (viewport.ActualHeight -
             content.Height * scale.ScaleY) / 2;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        isDragging = true;
        lastMousePosition = e.GetPosition(viewport);

        viewport.CaptureMouse();
        Mouse.OverrideCursor = Cursors.Hand;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        isDragging = false;
        viewport.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!isDragging)
            return;

        Point position = e.GetPosition(viewport);
        Vector difference = position - lastMousePosition;

        translate.X += difference.X;
        translate.Y += difference.Y;

        lastMousePosition = position;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.12 : 1.0 / 1.12;

        double newZoom = Math.Clamp(
            scale.ScaleX * factor,
            MinZoom,
            MaxZoom);

        double actualFactor = newZoom / scale.ScaleX;

        Point mouse = e.GetPosition(viewport);

        translate.X =
            mouse.X - (mouse.X - translate.X) * actualFactor;

        translate.Y =
            mouse.Y - (mouse.Y - translate.Y) * actualFactor;

        scale.ScaleX = newZoom;
        scale.ScaleY = newZoom;

        UpdateLod();
    }
}
