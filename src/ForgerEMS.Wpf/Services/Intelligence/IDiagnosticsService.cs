using System.Threading.Tasks;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public interface IDiagnosticsService
{
    UnifiedDiagnosticsReport BuildReport(
        string? systemIntelligenceJsonPath,
        string? usbIntelligenceJsonPath,
        string? toolkitHealthJsonPath,
        bool wslLikelyAvailable);

    Task WriteLatestReportAsync(string reportsDirectory, UnifiedDiagnosticsReport report);
}
