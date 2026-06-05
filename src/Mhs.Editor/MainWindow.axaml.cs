using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Visuals;
using Avalonia.VisualTree;
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
    private readonly DispatcherTimer _materialFlowTimer;
    private IReadOnlyList<GpuOption>? _availableGpuOptions;
    private AppPreferences _preferences;
    private IStorageFile? _currentSceneFile;
    private string _inspectorNameEditStartText = string.Empty;

    public MainWindow()
    {
        _preferences = _preferencesStore.Load();
        if (string.IsNullOrWhiteSpace(_preferences.PreferredOpenGlGpuName))
        {
            _preferences.PreferredOpenGlGpuName = "System default GPU";
        }

        GpuDiscoveryService.ApplyProcessGpuPreference(_preferences.PreferredOpenGlGpuName);
        StartupDiagnostics.Log($"Main window constructing. Preferred GPU: {_preferences.PreferredOpenGlGpuName}");

        InitializeComponent();
        DataContext = new MainWindowViewModel();
        ApplyWindowPreferences();
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ApplyAppPreferences(_preferences);
        }

        _materialFlowTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _materialFlowTimer.Tick += OnMaterialFlowTick;
        _materialFlowTimer.Start();

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Opened += OnOpened;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (IsTextInputFocused(e.Source))
        {
            return;
        }

        if (e.Key == Key.F && vm.FocusSelectedObject(GetActiveViewportBounds()))
        {
            e.Handled = true;
            return;
        }

        if (vm.HandleKeyDown(e.Key))
        {
            e.Handled = true;
        }
    }

    private void OnSceneTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        if (listBox.SelectedItem is SceneTreeNodeViewModel { IsGroupHeader: true })
        {
            listBox.UnselectAll();
        }
    }

    private void OnSceneTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: SceneTreeNodeViewModel { IsGroupHeader: false } node }
            || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (vm.FocusSceneTreeNode(node, GetActiveViewportBounds()))
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

    private void OnMaterialFlowTick(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.TickMaterialFlow();
        }
    }

    private async void OnOpenGlGpuSetupClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await ShowOnboardingWizardAsync(markOnboardingComplete: true);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"OpenGL GPU setup failed: {ex}");
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SetSceneStatus($"OpenGL GPU setup failed: {ex.Message}");
            }
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var settingsVm = new SettingsWindowViewModel(_preferences, EnsureGpuOptionsDiscovered());
            var settingsWindow = new SettingsWindow
            {
                DataContext = settingsVm
            };

            var accepted = await settingsWindow.ShowDialog<bool>(this);
            if (!accepted)
            {
                return;
            }

            _preferences = settingsVm.ToPreferences(_preferences);
            if (string.IsNullOrWhiteSpace(_preferences.PreferredOpenGlGpuName))
            {
                _preferences.PreferredOpenGlGpuName = "System default GPU";
            }

            _preferencesStore.Save(_preferences);
            GpuDiscoveryService.ApplyProcessGpuPreference(_preferences.PreferredOpenGlGpuName);
            ApplyWindowPreferences();
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ApplyAppPreferences(_preferences, updateStatus: true);
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Settings dialog failed: {ex}");
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SetSceneStatus($"Settings failed: {ex.Message}");
            }
        }
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

    private Avalonia.Rect GetActiveViewportBounds()
    {
        if (OpenGlViewport.IsVisible && OpenGlViewport.Bounds.Width > 0 && OpenGlViewport.Bounds.Height > 0)
        {
            return OpenGlViewport.Bounds;
        }

        if (SoftwareViewport.Bounds.Width > 0 && SoftwareViewport.Bounds.Height > 0)
        {
            return SoftwareViewport.Bounds;
        }

        return Bounds;
    }

    private static bool IsEditableInputVisual(Visual? visual)
    {
        if (visual is null)
        {
            return false;
        }

        return visual.GetSelfAndVisualAncestors().Any(item => item is TextBox or ComboBox);
    }

    private bool IsTextInputFocused(object? source)
    {
        if (source is Visual sourceVisual && IsEditableInputVisual(sourceVisual))
        {
            return true;
        }

        var focusedVisual = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        return IsEditableInputVisual(focusedVisual);
    }

    private void OnInspectorNameGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            _inspectorNameEditStartText = textBox.Text ?? string.Empty;
        }
    }

    private void OnInspectorNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                e.Handled = true;
                break;
            case Key.Escape:
                textBox.Text = _inspectorNameEditStartText;
                TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                e.Handled = true;
                break;
        }
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        if (_preferences.OnboardingCompleted)
        {
            return;
        }

        try
        {
            await ShowOnboardingWizardAsync(markOnboardingComplete: true);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Onboarding failed: {ex}");
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SetSceneStatus($"Onboarding failed: {ex.Message}");
            }
        }
    }

    private async Task ShowOnboardingWizardAsync(bool markOnboardingComplete)
    {
        var availableGpuOptions = EnsureGpuOptionsDiscovered();
        var onboardingVm = new OnboardingWizardViewModel(availableGpuOptions, _preferences.PreferredOpenGlGpuName);
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
        StartupDiagnostics.Log($"Onboarding selected GPU: {_preferences.PreferredOpenGlGpuName}");
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
        var availableGpuOptions = EnsureGpuOptionsDiscovered();
        if (availableGpuOptions.Count == 0)
        {
            return "System default GPU";
        }

        var selected = availableGpuOptions.FirstOrDefault(option =>
            option.Name.Equals(preferredGpuName, StringComparison.OrdinalIgnoreCase));

        return selected?.Name ?? availableGpuOptions[0].Name;
    }

    private IReadOnlyList<GpuOption> EnsureGpuOptionsDiscovered()
    {
        if (_availableGpuOptions is not null)
        {
            return _availableGpuOptions;
        }

        StartupDiagnostics.Log("Discovering GPUs via dxdiag.");
        _availableGpuOptions = GpuDiscoveryService.Discover();
        StartupDiagnostics.Log($"GPU discovery complete. Found {_availableGpuOptions.Count} option(s).");
        return _availableGpuOptions;
    }

    private void ApplyWindowPreferences()
    {
        WindowState = _preferences.OpenMaximized
            ? Avalonia.Controls.WindowState.Maximized
            : Avalonia.Controls.WindowState.Normal;
    }
}
