
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
    private const int CellPixelSize = 1;

    private readonly Canvas viewport;
    private readonly Canvas content;

    private readonly ScaleTransform scale = new();
    private readonly TranslateTransform translate = new();

    private World? currentWorld;
    private ConstructionGrid? constructionGrid;

    private bool isDragging;
    private Point lastMousePosition;

    public WorldMapRenderer(Canvas viewport, Canvas content)
    {
        this.viewport = viewport;
        this.content = content;

        TransformGroup transforms = new();
        transforms.Children.Add(scale);
        transforms.Children.Add(translate);

        content.RenderTransform = transforms;

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
        constructionGrid = new ConstructionGrid(world);

        scale.ScaleX = 3;
        scale.ScaleY = 3;

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

        int mapWidth =
            currentWorld.TotalCellWidth * CellPixelSize;

        int mapHeight =
            currentWorld.TotalCellHeight * CellPixelSize;

        content.Width = mapWidth;
        content.Height = mapHeight;

        // Отрисовка тайлов
        for (int tileX = 0; tileX < currentWorld.Width; tileX++)
        {
            for (int tileY = 0; tileY < currentWorld.Height; tileY++)
            {
                WorldTile tile = currentWorld.Tiles[tileX, tileY];

                WriteableBitmap bitmap = CreateTileBitmap(tile);

                Image image = new()
                {
                    Source = bitmap,
                    Width = WorldTile.ChunkCount *
                            WorldChunk.Size * CellPixelSize,
                    Height = WorldTile.ChunkCount *
                             WorldChunk.Size * CellPixelSize,
                    Stretch = Stretch.Fill,
                    IsHitTestVisible = false
                };

                double tileLeft =
                    tileX * WorldTile.ChunkCount *
                    WorldChunk.Size * CellPixelSize;

                double tileTop =
                    tileY * WorldTile.ChunkCount *
                    WorldChunk.Size * CellPixelSize;

                Canvas.SetLeft(image, tileLeft);
                Canvas.SetTop(image, tileTop);

                content.Children.Add(image);
            }
        }

        // Строительная сетка
        DrawConstructionGrid();

        // Границы тайлов
        DrawTileGrid(mapWidth, mapHeight);
    }

    private WriteableBitmap CreateTileBitmap(WorldTile tile)
    {
        int size =
            WorldTile.ChunkCount * WorldChunk.Size;

        WriteableBitmap bitmap = new(
            size,
            size,
            96,
            96,
            PixelFormats.Bgra32,
            null);

        int stride = size * 4;
        byte[] pixels = new byte[size * size * 4];

        for (int chunkX = 0;
             chunkX < WorldTile.ChunkCount;
             chunkX++)
        {
            for (int chunkY = 0;
                 chunkY < WorldTile.ChunkCount;
                 chunkY++)
            {
                WorldChunk chunk =
                    tile.Chunks[chunkX, chunkY];

                for (int cellX = 0;
                     cellX < WorldChunk.Size;
                     cellX++)
                {
                    for (int cellY = 0;
                         cellY < WorldChunk.Size;
                         cellY++)
                    {
                        ChunkCell cell =
                            chunk.Cells[cellX, cellY];

                        int x =
                            chunkX * WorldChunk.Size + cellX;

                        int y =
                            chunkY * WorldChunk.Size + cellY;

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
            }
        }

        bitmap.WritePixels(
            new Int32Rect(0, 0, size, size),
            pixels,
            stride,
            0);

        return bitmap;
    }

    private Color GetTerrainColor(ChunkCell cell)
    {
        return cell.Terrain switch
        {
            TerrainType.Water =>
                GetWaterColor(cell.Height),

            TerrainType.Plains =>
                GetPlainsColor(cell.Height),

            TerrainType.Forest =>
                GetForestColor(cell.Height),

            TerrainType.Hills =>
                GetHillColor(cell.Height),

            TerrainType.Mountains =>
                GetMountainColor(cell.Height),

            _ => Colors.Magenta
        };
    }

    private Color GetWaterColor(float height)
    {
        double depth = Math.Clamp(
            Math.Abs(height) / 45.0,
            0,
            1);

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
        double elevation = Math.Clamp(
            height / 3000.0,
            0,
            1);

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

        double shade = Math.Clamp(
            slope / 60.0,
            0,
            0.22);

        return Blend(
            color,
            Color.FromRgb(45, 45, 45),
            shade);
    }

    private Color Blend(
        Color first,
        Color second,
        double amount)
    {
        amount = Math.Clamp(amount, 0, 1);

        return Color.FromRgb(
            (byte)(first.R + (second.R - first.R) * amount),
            (byte)(first.G + (second.G - first.G) * amount),
            (byte)(first.B + (second.B - first.B) * amount));
    }

    private void DrawConstructionGrid()
    {
        if (constructionGrid == null || currentWorld == null)
            return;

        int cellSize = ConstructionGrid.CellSize;

        int mapWidth = currentWorld.TotalCellWidth;
        int mapHeight = currentWorld.TotalCellHeight;

        GeometryGroup geometry = new();

        for (int x = 0; x <= mapWidth; x += cellSize)
        {
            geometry.Children.Add(
                new LineGeometry(
                    new Point(x, 0),
                    new Point(x, mapHeight)));
        }

        for (int y = 0; y <= mapHeight; y += cellSize)
        {
            geometry.Children.Add(
                new LineGeometry(
                    new Point(0, y),
                    new Point(mapWidth, y)));
        }

        Path grid = new()
        {
            Data = geometry,
            Stroke = new SolidColorBrush(
                Color.FromArgb(90, 15, 22, 30)),
            StrokeThickness = 0.6,
            IsHitTestVisible = false
        };

        content.Children.Add(grid);
    }

    // Границы крупных тайлов.
    private void DrawTileGrid(int mapWidth, int mapHeight)
    {
        int tilePixelSize =
            WorldChunk.Size *
            WorldTile.ChunkCount *
            CellPixelSize;

        for (int x = 0; x <= currentWorld!.Width; x++)
        {
            Line line = new()
            {
                X1 = x * tilePixelSize,
                X2 = x * tilePixelSize,
                Y1 = 0,
                Y2 = mapHeight,
                Stroke = new SolidColorBrush(
                    Color.FromArgb(100, 25, 28, 32)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };

            content.Children.Add(line);
        }

        for (int y = 0; y <= currentWorld.Height; y++)
        {
            Line line = new()
            {
                X1 = 0,
                X2 = mapWidth,
                Y1 = y * tilePixelSize,
                Y2 = y * tilePixelSize,
                Stroke = new SolidColorBrush(
                    Color.FromArgb(100, 25, 28, 32)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };

            content.Children.Add(line);
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
        isDragging = true;
        lastMousePosition = e.GetPosition(viewport);

        viewport.CaptureMouse();
        Mouse.OverrideCursor = Cursors.Hand;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
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
        double zoom = e.Delta > 0 ? 1.1 : 0.9;
        double newScale = scale.ScaleX * zoom;

        if (newScale < 0.5 || newScale > 24)
            return;

        Point mouse = e.GetPosition(viewport);

        translate.X =
            mouse.X - (mouse.X - translate.X) * zoom;

        translate.Y =
            mouse.Y - (mouse.Y - translate.Y) * zoom;

        scale.ScaleX = newScale;
        scale.ScaleY = newScale;
    }
}
