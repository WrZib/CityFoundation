using CityFoundation.Rendering;
using Library.Generation;
using Library.Models;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

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
            5,
            5);

        renderer?.SetWorld(currentWorld);
    }
}
