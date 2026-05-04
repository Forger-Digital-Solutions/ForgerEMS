using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public interface IAutoIntelligenceOrchestrator
{
    void ScheduleAppStartupWork(BackendContext backend);

    void ScheduleUsbSelectionRefresh(BackendContext backend, UsbTargetInfo? selectedTarget);

    void ScheduleManualIntelligenceRefresh(BackendContext backend);
}
