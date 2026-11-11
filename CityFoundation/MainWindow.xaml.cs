using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Library.Generation;
using Library.Models;
using CityFoundation.Rendering;

namespace CityFoundation;

public partial class MainWindow : Window
{
    private readonly WorldGenerator generator = new();

    private WorldMapRenderer? renderer;
    private World? currentWorld;

    private bool isInitializing;
    private readonly string worldDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Saves");
    private string automaticSavePath => Path.Combine(worldDirectory, "automatic.sav");
    private readonly SaveService saveService = new SaveService();

    private string? _activeCategory;
    private string? _activeTool;

    private bool _isPaused;
    private int _speed = 1;
    private bool _exitConfirmed;

    private static readonly Color ColorAccent = (Color)ColorConverter.ConvertFromString("#3B82F6");
    private static readonly Color ColorElevated = (Color)ColorConverter.ConvertFromString("#1A1F26");
    private static readonly Color ColorWarning = (Color)ColorConverter.ConvertFromString("#F59E0B");
    private static readonly Color ColorText = (Color)ColorConverter.ConvertFromString("#E6EAF0");
    private static readonly Color ColorDark = (Color)ColorConverter.ConvertFromString("#0A0E14");

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(World world)
    {
        InitializeComponent();
        currentWorld = world;
    }

    // Загрузка и генерация
    private void OnWindowLoaded(object sender, RoutedEventArgs e) => LoadOrCreateWorldAsync();

    private async void LoadOrCreateWorldAsync()
    {
        if (isInitializing) return;
        isInitializing = true;

        ShowLoading(true, "Инициализация...");

        // Даем интерфейсу отрисовать оверлей и запустить анимацию
        await Task.Delay(100);

        try
        {
            // ГЕНЕРАЦИЯ (в фоновом потоке) - интерфейс плавно анимируется
            if (currentWorld == null)
            {
                if (File.Exists(automaticSavePath))
                {
                    SetStatus("Загрузка сохранённого мира...");
                    currentWorld = await Task.Run(() => saveService.Load(automaticSavePath));
                }
                else
                {
                    SetStatus("Генерация рельефа...");
                    int seed = Random.Shared.Next(1, int.MaxValue);

                    // Вызов генератора в Task.Run
                    currentWorld = await Task.Run(() => generator.Generate(seed, 3, 3));

                    SetStatus("Сохранение мира...");
                    Directory.CreateDirectory(worldDirectory);
                    await Task.Run(() => saveService.Save(currentWorld!, automaticSavePath));
                }
            }

            // ОТРИСОВКА КАРТЫ (в главном потоке)
            SetStatus("Отрисовка карты...");

            // Даем анимации сделать несколько кадров перед тяжелой отрисовкой
            await Task.Delay(150);

            renderer = new WorldMapRenderer(MapViewport, MapContent);

            // Этот метод блокирует UI-поток (так устроен WorldMapRenderer),
            // но анимация уже запущена и будет отрисована.
            renderer.SetWorld(currentWorld!);

            SetStatus("Мир готов");
            await Task.Delay(300); // Даем пользователю увидеть надпись
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось загрузить мир:\n{ex.Message}",
                "CitySim", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ShowLoading(false);
            isInitializing = false;
        }
    }

    // Потокобезопасно обновляет текст статуса (перенаправляет вызов в главный UI-поток)
    private void SetStatus(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(message));
            return;
        }
        LoadingText.Text = message;
    }

    // Включает/выключает оверлей загрузки
    private void ShowLoading(bool show, string initialText = "")
    {
        if (show)
        {
            if (!string.IsNullOrEmpty(initialText))
                LoadingText.Text = initialText;
            LoadingOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    // ================= ОСТАЛЬНОЙ ИНТЕРФЕЙС =================

    // Создание нового мира
    private async void NewWorldMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Текущий мир будет потерян. Продолжить?", "Новый мир", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        ShowLoading(true, "Генерация нового мира...");
        await Task.Delay(100);

        try
        {
            SetStatus("Генерация рельефа...");
            int seed = Random.Shared.Next(1, int.MaxValue);
            currentWorld = await Task.Run(() => generator.Generate(seed, 3, 3));

            SetStatus("Сохранение мира...");
            await Task.Run(() => saveService.Save(currentWorld!, automaticSavePath));

            SetStatus("Отрисовка карты...");
            await Task.Delay(150);

            renderer?.SetWorld(currentWorld!);

            SetStatus("Мир готов");
            await Task.Delay(300);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ShowLoading(false);
        }
    }

    private void SaveMenuItem_Click(object sender, RoutedEventArgs e) => SaveWorldWithDialog();
    private void LoadMenuItem_Click(object sender, RoutedEventArgs e) { }
    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("CitySim — учебный проект.\nМоделирование городской инфраструктуры.\n\n© 2025", "О программе", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // Закрытие окна
    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_exitConfirmed) return;
        var result = MessageBox.Show("Сохранить мир перед выходом?", "Выход", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (result == MessageBoxResult.Yes && !SaveWorldWithDialog()) { e.Cancel = true; return; }
        _exitConfirmed = true;
    }

    // Сохранение мира
    private bool SaveWorldWithDialog() => true;

    // Управление временем 
    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        _isPaused = !_isPaused;
        PauseButton.Content = _isPaused ? "Пуск" : "Пауза";
        PauseButton.Background = new SolidColorBrush(_isPaused ? ColorWarning : ColorElevated);
        PauseButton.Foreground = new SolidColorBrush(_isPaused ? ColorDark : ColorText);
    }


    // Скорость
    private void Speed1Button_Click(object sender, RoutedEventArgs e) => SetSpeed(1);
    private void Speed2Button_Click(object sender, RoutedEventArgs e) => SetSpeed(2);
    private void Speed3Button_Click(object sender, RoutedEventArgs e) => SetSpeed(4);

    private void SetSpeed(int speed)
    {
        _speed = speed;
        _isPaused = false;
        PauseButton.Content = "Пауза";
        PauseButton.Background = new SolidColorBrush(ColorElevated);
        PauseButton.Foreground = new SolidColorBrush(ColorText);
        ApplySpeedStyle(Speed1Button, speed == 1);
        ApplySpeedStyle(Speed2Button, speed == 2);
        ApplySpeedStyle(Speed3Button, speed == 4);
    }

    // Изменение кнопки скорости
    private static void ApplySpeedStyle(Button btn, bool active)
    {
        btn.Background = new SolidColorBrush(active ? ColorAccent : ColorElevated);
        btn.Foreground = new SolidColorBrush(active ? ColorDark : ColorText);
        btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    // Кнопки инструментов в категориях
    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        if (_activeCategory == tag)
        {
            SubMenuBorder.Visibility = Visibility.Collapsed;
            _activeCategory = null; _activeTool = null; return;
        }
        _activeCategory = tag; _activeTool = null;
        SubMenuPanel.Children.Clear();
        SubMenuTitle.Text = GetCategoryTitle(tag);
        foreach (var tool in GetToolsForCategory(tag))
        {
            var b = new Button { Content = tool, Tag = tool };
            b.SetResourceReference(StyleProperty, "SubmenuButtonStyle");
            b.Click += ToolButton_Click;
            SubMenuPanel.Children.Add(b);
        }
        SubMenuBorder.Visibility = Visibility.Visible;
    }

    // Сохраняет выбранный инструмент (из подменю) в переменную _activeTool
    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _activeTool = btn.Tag?.ToString();
    }

    private static string GetCategoryTitle(string tag) => tag switch
    {
        "Roads" => "Дороги",
        "Housing" => "Жильё",
        "Industry" => "Промышленность",
        "Utilities" => "ЖКХ",
        "Services" => "Службы",
        "Nature" => "Природа",
        "Population" => "Население",
        "Economy" => "Экономика",
        _ => tag
    };

    private static IEnumerable<string> GetToolsForCategory(string tag) => tag switch
    {
        "Roads" => new[] { "Грунтовая", "Асфальт", "Магистраль", "Мост", "Перекрёсток", "Снести" },
        "Housing" => new[] { "Частный дом", "Малоэтажный", "Среднеэтажный", "Многоэтажный", "Общежитие", "Снести" },
        "Industry" => new[] { "Шахта Fe", "Шахта Cu", "Нефтевышка", "Газ", "Завод", "Фабрика", "Электростанция", "Склад" },
        "Utilities" => new[] { "Подстанция", "Водобашня", "Котельная", "Канализация", "Газопровод" },
        "Services" => new[] { "Пожарная", "Полиция", "Больница", "Поликлиника", "Школа", "Колледж", "Вуз" },
        "Nature" => new[] { "Парк", "Велодорожка", "Прогулка", "Озеленение", "Пруд" },
        "Population" => new[] { "Демография", "Занятость", "Миграция" },
        "Economy" => new[] { "Бюджет", "Налоги", "Торговля", "Логистика" },
        _ => Array.Empty<string>()
    };

    // обновление ресурсов
    private void UpdateResourcesPanel()
    {
        if (currentWorld == null) return;
    }
}

// Сервис сохранений
public class SaveService
{
    public void Save(World world, string path) { }
    public World Load(string path) => new World();
}