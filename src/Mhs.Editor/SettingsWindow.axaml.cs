using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Mhs.Editor;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
        => Close(false);

    private void OnSaveClick(object? sender, RoutedEventArgs e)
        => Close(true);
}
