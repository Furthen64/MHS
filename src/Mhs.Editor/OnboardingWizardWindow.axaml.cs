using Avalonia.Controls;
using Mhs.Editor.ViewModels;

namespace Mhs.Editor;

public partial class OnboardingWizardWindow : Window
{
    public OnboardingWizardWindow()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OnboardingWizardViewModel { SelectedGpu: null })
        {
            return;
        }

        Close(true);
    }
}
