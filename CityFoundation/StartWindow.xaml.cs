using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Library.Models;

namespace CityFoundation;

public partial class StartWindow : Window
{
    private const string SavesDirectory = "Saves";

    public StartWindow() => InitializeComponent();

    // ================= ГЛАВНОЕ МЕНЮ =================

    // Создать мир
    private void CreateWorldButton_Click(object sender, RoutedEventArgs e)
        => ShowMainAndCloseStart(new MainWindow());

    // Выбрать из существующих
    private void LoadWorldButton_Click(object sender, RoutedEventArgs e)
    {
        LoadExistingWorlds();
        MainMenuPanel.Visibility = Visibility.Collapsed;
        WorldSelectionPanel.Visibility = Visibility.Visible;
    }
    
    // Выход
    private void ExitButton_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    // ================= ПАНЕЛЬ ВЫБОРА =================

    // Назад
    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        WorldSelectionPanel.Visibility = Visibility.Collapsed;
        MainMenuPanel.Visibility = Visibility.Visible;
    }

    // Запустить выбранный мир
    private void LoadSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (WorldsList.SelectedItem is not SavedWorldInfo info)
        {
            MessageBox.Show("Выберите мир из списка.", "Внимание",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // загрузить из файла
            // открыть MainWindow
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось загрузить мир:\n{ex.Message}",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ================= УТИЛИТЫ =================

    // Переключение окон
    private void ShowMainAndCloseStart(MainWindow main)
    {
        Application.Current.MainWindow = main;
        main.Show();
        Close();
    }

    // Чтение списка сохранений
    private void LoadExistingWorlds()
    {
        var worlds = new List<SavedWorldInfo>();

        if (Directory.Exists(SavesDirectory))
        {
            foreach (var file in Directory.GetFiles(SavesDirectory, "*.sav"))
            {
                worlds.Add(new SavedWorldInfo
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    LastModified = File.GetLastWriteTime(file)
                        .ToString("dd.MM.yyyy HH:mm")
                });
            }
        }

        WorldsList.ItemsSource = worlds;
        LoadSelectedButton.IsEnabled = worlds.Count > 0;

        bool empty = worlds.Count == 0;
        WorldsList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        NoWorldsMessage.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }
}

// Класс данных
public class SavedWorldInfo
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string LastModified { get; set; } = "";
}