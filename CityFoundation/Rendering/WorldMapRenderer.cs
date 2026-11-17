
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
    private Canvas? buildingGrid;
    private Panel? tileGrid;

    private World? currentWorld;
    private CancellationTokenSource? renderCancellation;
    private int renderVersion;
    private ResourceLayer visibleResourceLayers = ResourceLayer.All;

    private bool isDragging;
    private Point lastMousePosition;

    private Rectangle? centerHighlight;
    // Режим строительства дорог ЛКМ
    private bool isPainting;
    // Запомненные координаты сессии рисования
    private Point PaintStartCell;
    private Point PaintEndCell;
    private bool isErasing;
    private RoadType selectedRoadType = RoadType.Dirt;
    private RoadDirection selectedRoadDirection = RoadDirection.TwoWay;

    //коллекция чтобы не пытаться закрасить уже закрашенный пиксель повторно
    private readonly Dictionary<RoadCell, Image> roadVisuals = new();
    private readonly HashSet<RoadCell> strokeCreatedCells = new();
    private readonly HashSet<RoadCell> strokeExistingCells = new();
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
    public void SetEraseMode(int size = 1)
    {
        isToolActive = true;
        isErasing = true;
    }
    //выбирается размер кисти и режим рисования
    public void SetPaintBrush(Color color, int size, double opacity = 0.85)
    {
        selectedRoadType = size >= 15 ? RoadType.Highway : size >= 10 ? RoadType.Ordinary : RoadType.Dirt;
        selectedRoadDirection = RoadDirection.TwoWay;
        isToolActive = true;
        isErasing = false;
    }

    public void SetRoadTool(RoadType type, RoadDirection direction)
    {
        selectedRoadType = type;
        selectedRoadDirection = direction;
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
        foreach (var visual in roadVisuals.Values)
            content.Children.Remove(visual);
        roadVisuals.Clear();
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
        world.RebuildRoadNetwork();

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

            ClearPainted();
            isPainting = false;
            centerHighlight = null;

            constructionGrid = null;
            tileGrid = null;

            content.Width = world.TotalCellWidth * CellPixelSize;
            content.Height = world.TotalCellHeight * CellPixelSize;

            CenterMap();

            DrawTileGrid(tileSize);
            // Строительная сетка мира скрыта. Отдельная сетка появится только рядом с дорогами.
            constructionGrid = null;
            buildingGrid = null;

            UpdateLod();
            RedrawRoads();
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
                color = ApplyResourceDots(color, cell, x + tile.X * size, y + tile.Y * size);

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

    public void SetResourceLayerVisibility(ResourceLayer layer, bool visible)
    {
        visibleResourceLayers = visible ? visibleResourceLayers | layer : visibleResourceLayers & ~layer;
        if (currentWorld != null) SetWorld(currentWorld);
    }

    private Color ApplyResourceDots(Color baseColor, ChunkCell cell, int x, int y)
    {
        if (cell.ResourceDepositId == 0 || currentWorld == null)
            return baseColor;
        ResourceDeposit deposit = currentWorld.ResourceDeposits.FirstOrDefault(d => d.Id == cell.ResourceDepositId);
        if (deposit.Type == ResourceType.Water)
            return baseColor;
        ResourceLayer layer = deposit.Type switch
        {
            ResourceType.Water => ResourceLayer.Water, ResourceType.Stone => ResourceLayer.Stone,
            ResourceType.Iron => ResourceLayer.Iron, ResourceType.Copper => ResourceLayer.Copper,
            ResourceType.Oil => ResourceLayer.Oil, ResourceType.Gas => ResourceLayer.Gas, _ => ResourceLayer.None
        };
        if ((visibleResourceLayers & layer) == 0)
            return baseColor;
        double dx = x - deposit.CenterX;
        double dy = y - deposit.CenterY;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double radius = Math.Max(1, deposit.RadiusCells);
        double centerWeight = Math.Clamp(1 - distance / radius, 0, 1);
        int hash = Math.Abs((x * 73856093) ^ (y * 19349663) ^ deposit.ShapeSeed);
        double markerChance = 0.003 + centerWeight * 0.06;
        double randomValue = Math.Abs(hash % 1000) / 1000.0;
        if (randomValue > markerChance)
            return baseColor;
        Color dot = deposit.Type switch
        {
            ResourceType.Stone => Color.FromRgb(115, 115, 115), ResourceType.Iron => Color.FromRgb(188, 65, 45),
            ResourceType.Copper => Color.FromRgb(199, 117, 53), ResourceType.Oil => Color.FromRgb(30, 30, 30),
            ResourceType.Gas => Color.FromRgb(150, 100, 220), ResourceType.Water => Color.FromRgb(30, 130, 220), _ => baseColor
        };
        return Blend(baseColor, dot, 0.72);
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
            constructionGrid.Visibility = Visibility.Collapsed;
        }
        if (buildingGrid != null) buildingGrid.Visibility = zoom >= 1 ? Visibility.Visible : Visibility.Collapsed;
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
        // Ctrl временно превращает ЛКМ в панорамирование карты даже в режиме строительства.
        if (e.ChangedButton == MouseButton.Left && IsCtrlPressed())
        {
            isDragging = true;
            isPainting = false;
            lastMousePosition = e.GetPosition(viewport);
            viewport.CaptureMouse();
            Mouse.OverrideCursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }

        if (isToolActive && e.ChangedButton == MouseButton.Left)
        {
            isPainting = true;

            strokeCreatedCells.Clear();
            strokeExistingCells.Clear();
            if (currentWorld != null)
                foreach (var road in currentWorld.Roads) strokeExistingCells.Add(road.Cell);

            PaintStartCell = e.GetPosition(viewport);

            viewport.CaptureMouse();
            //меняем курсор на плюсик
            Mouse.OverrideCursor = Cursors.Cross;

            if (isErasing)
                EraseAt(e.GetPosition(viewport));
            else
                PaintAlongSelection(e.GetPosition(viewport));

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
        if (isPainting && e.ChangedButton == MouseButton.Left)
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
        if (isDragging && IsCtrlPressed())
        {
            Point ctrlPosition = e.GetPosition(viewport);
            Vector ctrlDifference = ctrlPosition - lastMousePosition;
            translate.X += ctrlDifference.X;
            translate.Y += ctrlDifference.Y;
            ClampTranslation();
            lastMousePosition = ctrlPosition;
            return;
        }

        if (isPainting)
        {
            Point pos = e.GetPosition(viewport);
            PaintEndCell = pos;

            if (isErasing)
                EraseAlongSelection(pos);
            else
                PaintAlongSelection(pos);

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

    private static bool IsCtrlPressed() =>
        Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
    private void StopPainting()
    {
        if (!isPainting)
            return;

        isPainting = false;
        viewport.ReleaseMouseCapture();
        Mouse.OverrideCursor = isToolActive ? Cursors.Cross : null;
        strokeCreatedCells.Clear();
        strokeExistingCells.Clear();
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
    private void PaintAlongSelection(Point screenPoint)
    {
        if (ScreenToCell(screenPoint) is not { } rawCell)
            return;
        var start = ScreenToCell(PaintStartCell) is { } s ? s : rawCell;
        var a = ToRoadCell(start); var b = ToRoadCell(rawCell);
        bool horizontal = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y);
        int from = horizontal ? Math.Min(a.X, b.X) : Math.Min(a.Y, b.Y);
        int to = horizontal ? Math.Max(a.X, b.X) : Math.Max(a.Y, b.Y);
        var targetCells = new HashSet<RoadCell>();
        for (int i = from; i <= to; i++)
        {
            var cell = horizontal ? new RoadCell(i, a.Y) : new RoadCell(a.X, i);
            if (selectedRoadType == RoadType.Highway) continue;
            if (!IsRoadBuildable(cell)) continue;
            if (!CanPlaceRoadCell(cell, horizontal, targetCells)) continue;
            targetCells.Add(cell);
        }

        if (currentWorld == null) return;
        foreach (var oldCell in strokeCreatedCells.Except(targetCells).ToList())
            currentWorld.Roads.RemoveAll(r => r.Cell == oldCell);
        strokeCreatedCells.RemoveWhere(oldCell => !targetCells.Contains(oldCell));

        foreach (var cell in targetCells)
        {
            RoadCell? sourceCell = null;
            if (selectedRoadDirection == RoadDirection.OneWay)
            {
                int step = horizontal ? Math.Sign(b.X - a.X) : Math.Sign(b.Y - a.Y);
                if (step == 0) step = 1;
                sourceCell = horizontal ? new RoadCell(cell.X - step, cell.Y) : new RoadCell(cell.X, cell.Y - step);
            }
            UpsertRoad(cell, sourceCell, horizontal ? RoadAxis.Horizontal : RoadAxis.Vertical);
            if (!strokeExistingCells.Contains(cell)) strokeCreatedCells.Add(cell);
        }
        RedrawRoads();
    }

    private bool IsRoadBuildable(RoadCell roadCell)
    {
        if (currentWorld == null) return false;
        int startX = roadCell.X * ConstructionGrid.CellSize;
        int startY = roadCell.Y * ConstructionGrid.CellSize;
        for (int x = 0; x < ConstructionGrid.CellSize; x++)
            for (int y = 0; y < ConstructionGrid.CellSize; y++)
                if (currentWorld.GetCell(startX + x, startY + y)?.IsBuildable == true)
                    return true;
        return false;
    }

    private RoadCell ToRoadCell((int X, int Y) cell) => new(cell.X / ConstructionGrid.CellSize, cell.Y / ConstructionGrid.CellSize);

    private bool CanPlaceRoadCell(RoadCell cell, bool horizontal, HashSet<RoadCell> strokeCells)
    {
        if (currentWorld == null) return false;
        var existing = currentWorld.Roads.FirstOrDefault(r => r.Cell == cell);
        if (existing != null) return true; // Совпадение двух направлений становится перекрестком.

        // Между параллельными улицами оставляем минимум две строительные ячейки.
        foreach (var road in currentWorld.Roads)
        {
            if (strokeCells.Contains(road.Cell)) continue;
            if (horizontal && road.Axis is RoadAxis.Horizontal or RoadAxis.Cross &&
                road.Cell.Y != cell.Y && Math.Abs(road.Cell.Y - cell.Y) <= 2 &&
                Math.Abs(road.Cell.X - cell.X) <= 1) return false;
            if (!horizontal && road.Axis is RoadAxis.Vertical or RoadAxis.Cross &&
                road.Cell.X != cell.X && Math.Abs(road.Cell.X - cell.X) <= 2 &&
                Math.Abs(road.Cell.Y - cell.Y) <= 1) return false;
        }
        return true;
    }

    private void UpsertRoad(RoadCell cell, RoadCell? from, RoadAxis axis)
    {
        var road = currentWorld!.Roads.FirstOrDefault(r => r.Cell == cell);
        if (road == null)
        {
            road = new Road(cell, selectedRoadType, selectedRoadDirection, axis) { OneWayFrom = from };
            currentWorld.Roads.Add(road);
        }
        else
        {
            road.Type = selectedRoadType;
            road.Direction = selectedRoadDirection;
            road.OneWayFrom = from;
            road.Axis = road.Axis == axis ? axis : RoadAxis.Cross;
        }
        currentWorld.RebuildRoadNetwork();
    }

    private void EraseAlongSelection(Point screenPoint)
    {
        if (ScreenToCell(screenPoint) is not { } rawCell || currentWorld == null) return;
        var cell = ToRoadCell(rawCell);
        var start = ScreenToCell(PaintStartCell) is { } startRaw ? ToRoadCell(startRaw) : cell;
        var road = currentWorld.Roads.FirstOrDefault(r => r.Cell == cell);
        if (road == null) return;

        var cells = new HashSet<RoadCell>();
        bool horizontal = Math.Abs(cell.X - start.X) >= Math.Abs(cell.Y - start.Y);
        if (cell != start)
        {
            int from = horizontal ? Math.Min(start.X, cell.X) : Math.Min(start.Y, cell.Y);
            int to = horizontal ? Math.Max(start.X, cell.X) : Math.Max(start.Y, cell.Y);
            for (int i = from; i <= to; i++)
                cells.Add(horizontal ? new RoadCell(i, start.Y) : new RoadCell(start.X, i));
        }
        else
        {
            // Одиночный клик удаляет сегмент до перекрестка или конца улицы.
            cells.Add(cell);
            var axis = road.Axis == RoadAxis.Cross ? RoadAxis.Horizontal : road.Axis;
            foreach (int sign in new[] { -1, 1 })
            {
                for (int distance = 1; distance < 10000; distance++)
                {
                    var next = axis == RoadAxis.Horizontal
                        ? new RoadCell(cell.X + distance * sign, cell.Y)
                        : new RoadCell(cell.X, cell.Y + distance * sign);
                    var nextRoad = currentWorld.Roads.FirstOrDefault(r => r.Cell == next);
                    if (nextRoad == null || nextRoad.Axis == RoadAxis.Cross) break;
                    cells.Add(next);
                }
            }
        }
        currentWorld.Roads.RemoveAll(r => cells.Contains(r.Cell));
        currentWorld.RebuildRoadNetwork();
        RedrawRoads();
    }

    private void EraseAt(Point screenPoint)
    {
        EraseAlongSelection(screenPoint);
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

    private void RedrawRoads()
    {
        if (currentWorld == null) return;
        foreach (var visual in roadVisuals.Values) content.Children.Remove(visual);
        roadVisuals.Clear();
        if (buildingGrid != null) content.Children.Remove(buildingGrid);

        buildingGrid = new Canvas { Width = content.Width, Height = content.Height, IsHitTestVisible = false };
        var roads = currentWorld.Roads.ToDictionary(r => r.Cell);
        var cells = roads.Keys.ToHashSet();
        var buildingCells = new HashSet<RoadCell>();
        foreach (var road in currentWorld.Roads)
        {
            var image = new Image
            {
                Width = ConstructionGrid.CellSize * CellPixelSize,
                Height = ConstructionGrid.CellSize * CellPixelSize,
                Source = CreateRoadBitmap(road, roads),
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(image, road.Cell.X * ConstructionGrid.CellSize * CellPixelSize);
            Canvas.SetTop(image, road.Cell.Y * ConstructionGrid.CellSize * CellPixelSize);
            Panel.SetZIndex(image, 35);
            content.Children.Add(image);
            roadVisuals[road.Cell] = image;
            DrawBuildingCells(road.Cell, cells, buildingCells);
        }
        Panel.SetZIndex(buildingGrid, 30);
        content.Children.Add(buildingGrid);
        UpdateLod();
    }

    private void DrawBuildingCells(RoadCell road, HashSet<RoadCell> roads, HashSet<RoadCell> buildingCells)
    {
        foreach (var cell in new[]
        {
            new RoadCell(road.X - 1, road.Y), new RoadCell(road.X + 1, road.Y),
            new RoadCell(road.X, road.Y - 1), new RoadCell(road.X, road.Y + 1)
        })
        {
            if (roads.Contains(cell) || !buildingCells.Add(cell)) continue;
            var rect = new Rectangle
            {
                Width = ConstructionGrid.CellSize * CellPixelSize,
                Height = ConstructionGrid.CellSize * CellPixelSize,
                Stroke = new SolidColorBrush(Color.FromArgb(130, 235, 213, 120)),
                StrokeThickness = 0.18,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(rect, cell.X * ConstructionGrid.CellSize * CellPixelSize);
            Canvas.SetTop(rect, cell.Y * ConstructionGrid.CellSize * CellPixelSize);
            buildingGrid.Children.Add(rect);
        }
    }

    private BitmapSource CreateRoadBitmap(Road road, IReadOnlyDictionary<RoadCell, Road> roads)
    {
        const int size = 40;
        var pixels = new byte[size * size * 4];
        Color baseColor = road.Type switch
        {
            RoadType.Dirt => Color.FromRgb(142, 103, 65),
            RoadType.Highway => Color.FromRgb(38, 43, 50),
            _ => Color.FromRgb(65, 68, 73)
        };
        Color edgeColor = road.Type switch
        {
            RoadType.Dirt => Color.FromRgb(184, 144, 91),
            RoadType.Highway => Color.FromRgb(232, 190, 70),
            _ => Color.FromRgb(178, 182, 185)
        };
        var left = roads.ContainsKey(new RoadCell(road.Cell.X - 1, road.Cell.Y));
        var right = roads.ContainsKey(new RoadCell(road.Cell.X + 1, road.Cell.Y));
        var up = roads.ContainsKey(new RoadCell(road.Cell.X, road.Cell.Y - 1));
        var down = roads.ContainsKey(new RoadCell(road.Cell.X, road.Cell.Y + 1));
        bool hasHorizontal = road.Axis is RoadAxis.Horizontal or RoadAxis.Cross || left || right;
        bool hasVertical = road.Axis is RoadAxis.Vertical or RoadAxis.Cross || up || down;
        bool mixedNeighbour = roads.Values.Any(other =>
            Math.Abs(other.Cell.X - road.Cell.X) + Math.Abs(other.Cell.Y - road.Cell.Y) == 1 &&
            other.Type != road.Type);
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            bool center = x is >= 13 and <= 26 && y is >= 13 and <= 26;
            bool horizontal = center || (y is >= 13 and <= 26 && ((left && x < 20) || (right && x >= 20)));
            bool vertical = center || (x is >= 13 and <= 26 && ((up && y < 20) || (down && y >= 20)));
            bool sidewalk = (hasHorizontal && y is 10 or 11 or 28 or 29) &&
                            ((left && x < 20) || (right && x >= 20) || road.Axis == RoadAxis.Horizontal) ||
                            (hasVertical && x is 10 or 11 or 28 or 29) &&
                            ((up && y < 20) || (down && y >= 20) || road.Axis == RoadAxis.Vertical);
            Color c = horizontal || vertical ? baseColor : sidewalk ? Color.FromRgb(205, 178, 118) : Colors.Transparent;
            if ((x + y) % 11 == 0 && c.A != 0) c = edgeColor;
            if (mixedNeighbour && c.A != 0 && (x == 19 || y == 19)) c = Color.FromRgb(216, 146, 53);
            if (c.A != 0 && road.Direction == RoadDirection.OneWay &&
                ((hasHorizontal && y == 19) || (hasVertical && x == 19)))
                c = Color.FromRgb(235, 205, 84); // маркер одностороннего движения
            if (c.A != 0 && road.Direction == RoadDirection.TwoWay &&
                ((hasHorizontal && y == 19 && x % 9 < 4) || (hasVertical && x == 19 && y % 9 < 4)))
                c = Color.FromRgb(220, 220, 200); // разделительная разметка двухсторонней дороги
            int i = (y * size + x) * 4; pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R; pixels[i + 3] = c.A;
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze(); return bitmap;
    }
}
