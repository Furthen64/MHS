using Avalonia.Controls;
using Avalonia.Input;
using Mhs.Editor.ViewModels;

namespace Mhs.Editor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        vm.HandleKeyDown(e.Key);
        if (e.Key is Key.R or Key.Delete or Key.Back or Key.M or Key.Escape)
        {
            e.Handled = true;
        }
    }
}
