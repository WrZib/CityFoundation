using System;
using System.Windows;
using Library.Generation;
using Library.Models;
using CityFoundation.Rendering;

namespace CityFoundation;

public partial class MainWindow : Window
{
    private readonly WorldGenerator generator = new();

    private WorldMapRenderer? renderer;
    private World? currentWorld;

    public MainWindow()
    {
        InitializeComponent();

        renderer = new WorldMapRenderer(
            MapViewport,
            MapContent);

        GenerateNewWorld();
    }

    private void GenerateNewWorld()
    {
        int seed = Random.Shared.Next(
            1,
            int.MaxValue);

        currentWorld = generator.Generate(
            seed,
            3,
            3,
            2);

        renderer?.SetWorld(currentWorld);
    }
}
