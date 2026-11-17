
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Threading;
using System.Threading.Tasks;
using Library.Models;

namespace CityFoundation.Rendering;

public class WorldMapRenderer
{
    private const double CellPixelSize = 0.5;

    private const double MinZoom = 0.3;
    private const double MaxZoom = 24;
    private const double InitialZoom = 1;

    private const double TileGridZoom = 0.7;
    private const double ConstructionGridZoom = 3;

    private const double MaxMoveOffset = 0.35;

    private readonly Canvas viewport;
    private readonly Canvas content;

    private readonly ScaleTransform scale = new();
    private readonly TranslateTransform translate = new();

    private Rectangle? constructionGrid;
    private Panel? tileGrid;

    private World? currentWorld;
    private CancellationTokenSource? renderCancellation;
    private int renderVersion;

    private bool isDragging;
    private Point lastMousePosition;

    private Rectangle? centerHighlight;
    // Режим рисования ПКМ
    private bool isPainting;
    // Запомненные координаты сессии рисования
    Point PaintStartCell;
    Point PaintEndCell;
    private bool isErasing;   // true = инструмент "Снести" false = рисование
    private int brushSize = 5;   // размер кисти в клетках мира (5, 10, 15)

    //коллекция чтобы не пытаться закрасить уже закрашенный пиксель повторно
    private readonly HashSet<(int X, int Y)> paintedCells = new();
    private readonly Dictionary<(int X, int Y), Rectangle> paintedVisuals = new();
    private Color paintColor = Colors.Gray;
    private double paintOpacity = 0.85;
    private bool isToolActive;

    public bool IsToolActive
    {
        get => isToolActive;
        set
        {
            isToolActive = value;
            if (!value) StopPainting();
        }
    }
    //режим стирания дороги
    public void SetEraseMode(int size)
    {
        brushSize = size;
        isToolActive = true;
        isErasing = true;
        StopPainting();
    }
    //выбирается размер кисти и режим рисования
    public void SetPaintBrush(Color color, int size, double opacity = 0.85)
    {
        paintColor = color;
        paintOpacity = opacity;
        brushSize = size;
        isToolActive = true;
        isErasing = false;
    }

    //отключает активные элементы
    public void ClearPaintBrush()
    {
        isToolActive = false;
        isErasing = false;
        StopPainting();
    }

   
    public void ClearPainted()
    {
        foreach (var visual in paintedVisuals.Values)
            content.Children.Remove(visual);

        paintedVisuals.Clear();
        paintedCells.Clear();
    }

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
        //для работы ПКМ
        viewport.MouseRightButtonDown += OnMouseDown;
        viewport.MouseRightButtonUp += OnMouseUp;

        viewport.MouseMove += OnMouseMove;
        viewport.MouseWheel += OnMouseWheel;

        viewport.SizeChanged += (_, _) =>
        {
            if (currentWorld != null)
                CenterMap();
        };
    }

    public async Task SetWorldAsync(World world)
    {
        currentWorld = world;

        renderCancellation?.Cancel();
        renderCancellation?.Dispose();
        renderCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = renderCancellation.Token;
        int version = Interlocked.Increment(ref renderVersion);

        scale.ScaleX = InitialZoom;
        scale.ScaleY = InitialZoom;

        await DrawAsync(world, version, cancellationToken);

        viewport.Dispatcher.BeginInvoke(
            new Action(CenterMap),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void SetWorld(World world) => _ = SetWorldAsync(world);

    private async Task DrawAsync(World world, int version, CancellationToken cancellationToken)
    {
        int tileCells = WorldTile.ChunkCount * WorldChunk.Size;
        double tileSize = tileCells * CellPixelSize;

        await viewport.Dispatcher.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested || version != renderVersion || !ReferenceEquals(currentWorld, world))
                return;

            content.Children.Clear();

            //отключение режима рисования
            paintedCells.Clear();
            paintedVisuals.Clear();
            isPainting = false;
            centerHighlight = null;

            constructionGrid = null;
            tileGrid = null;

            content.Width = world.TotalCellWidth * CellPixelSize;
            content.Height = world.TotalCellHeight * CellPixelSize;

            CenterMap();

            DrawTileGrid(tileSize);
            DrawConstructionGrid();

            UpdateLod();
        }, System.Windows.Threading.DispatcherPriority.Render, cancellationToken);

        var tileTasks = new List<Task>();
        for (int x = 0; x < world.Width; x++)
        {
            for (int y = 0; y < world.Height; y++)
            {
                int tileX = x;
                int tileY = y;
                tileTasks.Add(Task.Run(async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WriteableBitmap bitmap = CreateTileBitmap(world.Tiles[tileX, tileY]);

                    await viewport.Dispatcher.InvokeAsync(() =>
                    {
                        if (cancellationToken.IsCancellationRequested ||
                            version != renderVersion ||
                            !ReferenceEquals(currentWorld, world))
                            return;

                        Image image = new()
                        {
                            Source = bitmap,
                            Width = tileSize,
                            Height = tileSize,
                            Stretch = Stretch.Fill,
                            IsHitTestVisible = false,
                            SnapsToDevicePixels = true
                        };

                        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Fant);
                        Canvas.SetLeft(image, tileX * tileSize);
                        Canvas.SetTop(image, tileY * tileSize);
                        Panel.SetZIndex(image, 0);
                        content.Children.Add(image);
                    }, System.Windows.Threading.DispatcherPriority.Render, cancellationToken);
                }, cancellationToken));
            }
        }

        await Task.WhenAll(tileTasks);
    }

    // Создание текстуры тайла
    private WriteableBitmap CreateTileBitmap(WorldTile tile)
    {
        int size = WorldTile.ChunkCount * WorldChunk.Size;

        int stride = size * 3;
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

                int index = y * stride + x * 3;

                // BGR
                pixels[index] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
            }
        }

        WriteableBitmap bitmap = new(
            size,
            size,
            96,
            96,
            PixelFormats.Bgr24,
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
        if (height < 0 || slope < 16)
            return color;

        double amount = Math.Clamp(
            (slope - 16) / 20.0,
            0,
            1);

        amount *= 0.07;

        return Color.FromRgb(
            (byte)(color.R * (1 - amount)),
            (byte)(color.G * (1 - amount)),
            (byte)(color.B * (1 - amount)));
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

        if (isToolActive && e.ChangedButton == MouseButton.Right)
        {
            isPainting = true;

            PaintStartCell = e.GetPosition(viewport);

            viewport.CaptureMouse();
            //меняем курсор на плюсик
            Mouse.OverrideCursor = Cursors.Cross;

            //рисуем или стираем
            if (isErasing)
                EraseAt(e.GetPosition(viewport));
            else
                PaintAt(e.GetPosition(viewport));

            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        isDragging = true;
        lastMousePosition = e.GetPosition(viewport);
        viewport.CaptureMouse();
        Mouse.OverrideCursor = Cursors.Hand;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (isPainting && e.ChangedButton == MouseButton.Right)
        {
            PaintEndCell = e.GetPosition(viewport);
            //отмена режима рисования

            StopPainting();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        isDragging = false;
        viewport.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
    }

    private const double MinVisiblePart = 0.85;

    private void ClampTranslation()
    {
        double mapWidth = content.Width * scale.ScaleX;
        double mapHeight = content.Height * scale.ScaleY;

        double viewWidth = viewport.ActualWidth;
        double viewHeight = viewport.ActualHeight;

        double visibleX = Math.Min(viewWidth, mapWidth) * MinVisiblePart;
        double visibleY = Math.Min(viewHeight, mapHeight) * MinVisiblePart;

        double minX = visibleX - mapWidth;
        double maxX = viewWidth - visibleX;

        double minY = visibleY - mapHeight;
        double maxY = viewHeight - visibleY;

        translate.X = Math.Clamp(translate.X, minX, maxX);
        translate.Y = Math.Clamp(translate.Y, minY, maxY);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (isPainting)
        {
            Point pos = e.GetPosition(viewport);
            PaintEndCell = pos;

            if (isErasing)
                EraseAt(pos);//режим чтобы очистить дороги
            else
                PaintAt(pos);//режим чтобы рисовать дороги

            return;
        }

        if (!isDragging)
            return;

        Point position = e.GetPosition(viewport);
        Vector difference = position - lastMousePosition;
        translate.X += difference.X;
        translate.Y += difference.Y;
        ClampTranslation();
        lastMousePosition = position;
    }
    private void StopPainting()
    {
        if (!isPainting)
            return;

        isPainting = false;
        viewport.ReleaseMouseCapture();
        Mouse.OverrideCursor = isToolActive ? Cursors.Cross : null;
    }

    //переводит координаты пикселя на экране в индекс клетки мира
    private (int X, int Y)? ScreenToCell(Point screenPoint)
    {
        if (currentWorld == null) return null;

        double contentX = (screenPoint.X - translate.X) / scale.ScaleX;
        double contentY = (screenPoint.Y - translate.Y) / scale.ScaleY;

        int cellX = (int)(contentX / CellPixelSize);
        int cellY = (int)(contentY / CellPixelSize);

        if (cellX < 0 || cellY < 0 ||
            cellX >= currentWorld.TotalCellWidth ||
            cellY >= currentWorld.TotalCellHeight)
            return null;

        return (cellX, cellY);
    }
    //основная функция которая рисует
    private void PaintAt(Point screenPoint)
    {
        if (ScreenToCell(screenPoint) is not { } rawCell)
            return;

        var cell = SnapToGrid(rawCell, brushSize);
        
        if (!paintedCells.Add(cell))
            return;

        double sizePx = CellPixelSize * brushSize;

        Rectangle rect = new()
        {
            Width = sizePx,
            Height = sizePx,
            Fill = new SolidColorBrush(paintColor),
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };

        //подстановка под сетку
        Canvas.SetLeft(rect, (double)(cell.X / 2) + (double)(cell.X % 2) / 2);
        Canvas.SetTop(rect, (double)(cell.Y / 2) + (double)(cell.Y % 2) / 2);
        Panel.SetZIndex(rect, 40);

        content.Children.Add(rect);
        paintedVisuals[cell] = rect;

        //switch(brushSize)
        //{
        //    case 10:
        //        paintedVisuals[(cell.X + 1, cell.Y)] = rect;
        //        paintedVisuals[(cell.X + 1, cell.Y + 1)] = rect;
        //        paintedVisuals[(cell.X, cell.Y + 1)] = rect;
        //        break;
        //    case 15:
        //        paintedVisuals[(cell.X + 1, cell.Y)] = rect;
        //        paintedVisuals[(cell.X + 2, cell.Y)] = rect;
        //        paintedVisuals[(cell.X + 1, cell.Y + 1)] = rect;
        //        paintedVisuals[(cell.X + 2, cell.Y + 1)] = rect;
        //        paintedVisuals[(cell.X + 1, cell.Y + 2)] = rect;
        //        paintedVisuals[(cell.X + 2, cell.Y + 2)] = rect;
        //        paintedVisuals[(cell.X, cell.Y + 1)] = rect;
        //        paintedVisuals[(cell.X, cell.Y + 2)] = rect;
        //        break;
        //}
    }
    private static (int X, int Y) SnapToGrid((int X, int Y) cell, int size)
    {
        return (
            size * (cell.X / size),
            size * (cell.Y / size));
    }
    //стирание одной клетки
    private void EraseAt(Point screenPoint)
    {
        if (ScreenToCell(screenPoint) is not { } rawCell)
            return;

        var cell = SnapToGrid(rawCell, brushSize);

        if (!paintedVisuals.TryGetValue(cell, out var rect))
            return;

        content.Children.Remove(rect);
        paintedVisuals.Remove(cell);
        paintedCells.Remove(cell);
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

        ClampTranslation();
        UpdateLod();
    }
}
