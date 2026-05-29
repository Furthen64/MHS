using Avalonia.Controls;
using Mhs.Editor.ViewModels;

namespace Mhs.Editor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}
