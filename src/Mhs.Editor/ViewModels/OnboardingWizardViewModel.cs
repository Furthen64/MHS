using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Mhs.Editor.Settings;

namespace Mhs.Editor.ViewModels;

public sealed class OnboardingWizardViewModel
{
    public ObservableCollection<GpuOption> AvailableGpus { get; }
    public GpuOption? SelectedGpu { get; set; }

    public OnboardingWizardViewModel(IReadOnlyList<GpuOption> gpus, string preferredGpuName)
    {
        AvailableGpus = new ObservableCollection<GpuOption>(gpus);

        SelectedGpu =
            AvailableGpus.FirstOrDefault(g => g.Name.Equals(preferredGpuName, System.StringComparison.OrdinalIgnoreCase))
            ?? AvailableGpus.FirstOrDefault();
    }
}
