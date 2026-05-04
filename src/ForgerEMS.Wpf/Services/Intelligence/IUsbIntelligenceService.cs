using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public interface IUsbIntelligenceService
{
    UsbTopologySnapshot BuildTopologySnapshot(UsbTargetInfo? selectedTarget, UsbTopologyBuildOptions? options = null);

    Task WriteLatestReportAsync(string reportsDirectory, UsbTopologySnapshot snapshot);

    UsbBuilderPreflightResult GetVentoyPreflight(UsbTargetInfo? selectedTarget, UsbTopologySnapshot? snapshot);
}
