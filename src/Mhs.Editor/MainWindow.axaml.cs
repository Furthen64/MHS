using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Mhs.Editor.ViewModels;

namespace Mhs.Editor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
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
}
