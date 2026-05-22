using System;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.DriveValidation;

public interface IDriveValidationService
{
    Task<DriveValidationResult> RunAsync(
        UsbTargetInfo target,
        DriveValidationOptions options,
        string? portPathHint = null,
        Action<DriveValidationProgress>? onProgress = null,
        CancellationToken cancellationToken = default);
}
