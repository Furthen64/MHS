using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Mhs.Editor.Editor;
using Mhs.Editor.Settings;
using Mhs.Editor.ViewModels;

namespace Mhs.Editor;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType SceneJsonFileType = new("Scene JSON")
    {
        Patterns = ["*.json"]
    };

    private readonly AppPreferencesStore _preferencesStore = new();
    private readonly IReadOnlyList<GpuOption> _availableGpuOptions;
    private AppPreferences _preferences;
    private IStorageFile? _currentSceneFile;

    public MainWindow()
    {
        _preferences = _preferencesStore.Load();
        _availableGpuOptions = GpuDiscoveryService.Discover();
        _preferences.PreferredOpenGlGpuName = ResolvePreferredGpu(_preferences.PreferredOpenGlGpuName);
        GpuDiscoveryService.ApplyProcessGpuPreference(_preferences.PreferredOpenGlGpuName);

        InitializeComponent();
        DataContext = new MainWindowViewModel();
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SetPreferredOpenGlGpu(_preferences.PreferredOpenGlGpuName);
        }

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Opened += OnOpened;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (vm.HandleKeyDown(e.Key))
        {
            e.Handled = true;
        }
    }

    private void OnNewSceneClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        _currentSceneFile = null;
        vm.NewScene();
    }

    private async void OnOpenSceneClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open scene",
                AllowMultiple = false,
                FileTypeFilter = [SceneJsonFileType]
            });

            if (files.Count == 0)
            {
                return;
            }

            var file = files[0];
            await using var stream = await file.OpenReadAsync();
            var scene = await SceneFileJsonSerializer.LoadAsync(stream);
            vm.LoadSceneFileData(scene);
            _currentSceneFile = file;
            vm.SetSceneStatus($"Opened {GetDisplayName(file)}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            vm.SetSceneStatus($"Open failed: {ex.Message}");
        }
    }

    private async void OnSaveSceneClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        try
        {
            if (_currentSceneFile is null)
            {
                await SaveSceneAsAsync(vm);
                return;
            }

            await SaveSceneToFileAsync(vm, _currentSceneFile);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            vm.SetSceneStatus($"Save failed: {ex.Message}");
        }
    }

    private async void OnSaveSceneAsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        await SaveSceneAsAsync(vm);
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnOpenGlGpuSetupClick(object? sender, RoutedEventArgs e)
    {
        await ShowOnboardingWizardAsync(markOnboardingComplete: true);
    }

    private async Task SaveSceneAsAsync(MainWindowViewModel vm)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save scene",
                SuggestedFileName = "scene.json",
                DefaultExtension = "json",
                FileTypeChoices = [SceneJsonFileType]
            });

            if (file is null)
            {
                return;
            }

            await SaveSceneToFileAsync(vm, file);
            _currentSceneFile = file;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            vm.SetSceneStatus($"Save failed: {ex.Message}");
        }
    }

    private static async Task SaveSceneToFileAsync(MainWindowViewModel vm, IStorageFile file)
    {
        var scene = vm.CreateSceneFileData();
        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek)
        {
            stream.SetLength(0);
            stream.Seek(0, SeekOrigin.Begin);
        }

        await SceneFileJsonSerializer.SaveAsync(stream, scene);
        await stream.FlushAsync();
        vm.SetSceneStatus($"Saved {GetDisplayName(file)}");
    }

    private static string GetDisplayName(IStorageItem item)
        => string.IsNullOrWhiteSpace(item.Name) ? "scene" : item.Name;

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        if (_preferences.OnboardingCompleted)
        {
            return;
        }

        await ShowOnboardingWizardAsync(markOnboardingComplete: true);
    }

    private async Task ShowOnboardingWizardAsync(bool markOnboardingComplete)
    {
        var onboardingVm = new OnboardingWizardViewModel(_availableGpuOptions, _preferences.PreferredOpenGlGpuName);
        var onboardingWindow = new OnboardingWizardWindow
        {
            DataContext = onboardingVm
        };

        var accepted = await onboardingWindow.ShowDialog<bool>(this);
        if (!accepted)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SetSceneStatus("OpenGL GPU setup skipped");
            }
            return;
        }

        if (onboardingVm.SelectedGpu is null)
        {
            return;
        }

        _preferences.PreferredOpenGlGpuName = ResolvePreferredGpu(onboardingVm.SelectedGpu.Name);
        if (markOnboardingComplete)
        {
            _preferences.OnboardingCompleted = true;
        }

        _preferencesStore.Save(_preferences);
        GpuDiscoveryService.ApplyProcessGpuPreference(_preferences.PreferredOpenGlGpuName);

        if (DataContext is MainWindowViewModel mainVm)
        {
            mainVm.SetPreferredOpenGlGpu(_preferences.PreferredOpenGlGpuName);
        }
    }

    private string ResolvePreferredGpu(string preferredGpuName)
    {
        if (_availableGpuOptions.Count == 0)
        {
            return "System default GPU";
        }

        var selected = _availableGpuOptions.FirstOrDefault(option =>
            option.Name.Equals(preferredGpuName, StringComparison.OrdinalIgnoreCase));

        return selected?.Name ?? _availableGpuOptions[0].Name;
    }
}
