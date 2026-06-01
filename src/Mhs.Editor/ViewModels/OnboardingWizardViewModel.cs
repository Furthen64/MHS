using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Mhs.Editor.Settings;

namespace Mhs.Editor.ViewModels;

public partial class OnboardingWizardViewModel : ViewModelBase
{
    public ObservableCollection<GpuOption> AvailableGpus { get; }

    [ObservableProperty]
    private GpuOption? _selectedGpu;

    public OnboardingWizardViewModel(IReadOnlyList<GpuOption> gpus, string preferredGpuName)
    {
        AvailableGpus = new ObservableCollection<GpuOption>(gpus);

        SelectedGpu =
            AvailableGpus.FirstOrDefault(g => g.Name.Equals(preferredGpuName, System.StringComparison.OrdinalIgnoreCase))
            ?? AvailableGpus.FirstOrDefault();
    }
}
