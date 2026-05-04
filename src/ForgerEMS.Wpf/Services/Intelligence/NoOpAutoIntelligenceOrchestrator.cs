using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public sealed class NoOpAutoIntelligenceOrchestrator : IAutoIntelligenceOrchestrator
{
    public void ScheduleAppStartupWork(BackendContext backend)
    {
    }

    public void ScheduleUsbSelectionRefresh(BackendContext backend, UsbTargetInfo? selectedTarget)
    {
    }

    public void ScheduleManualIntelligenceRefresh(BackendContext backend)
    {
    }
}
