using VentoyToolkitSetup.Wpf.Infrastructure;

namespace VentoyToolkitSetup.Wpf.Models;

public sealed class UsbBuilderProfileOption : ObservableObject
{
    private bool _isIncluded;

    public string CategoryId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Platform { get; init; } = string.Empty;

    public bool IsRequired { get; init; }

    public bool DefaultIncluded { get; init; }

    public bool RequiresManualMedia { get; init; }

    public bool CanToggle => !IsRequired;

    public bool IsIncluded
    {
        get => IsRequired || _isIncluded;
        set
        {
            var normalized = IsRequired || value;
            if (SetProperty(ref _isIncluded, normalized))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText =>
        IsRequired
            ? "Required"
            : RequiresManualMedia
                ? "Manual media required"
                : IsIncluded
                    ? "Included"
                    : "Off";
}
