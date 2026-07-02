using VentoyToolkitSetup.Wpf.Infrastructure;

namespace VentoyToolkitSetup.Wpf.Models;

// Item-level descriptor for the USB Builder Profile drill-down.
// Stable Id is required: persisted profile selections refer to items by Id, so
// renames must keep the Id stable. ManifestEntryName is an optional join key
// against manifests/ForgerEMS.updates.json for items whose payload is a managed
// download or shortcut; UsbRelativePath can be used as an exact destination
// selector for manifest pages, seed folders, and guidance files.
public sealed class UsbBuilderProfileItem : ObservableObject
{
    private bool _isSelected;
    private long _detectedBytes;
    private bool _existsOnUsb;

    public string Id { get; init; } = string.Empty;

    public string CategoryId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Subcategory { get; init; } = string.Empty;

    public string ShortDescription { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public UsbBuilderProfileItemKind Kind { get; init; }

    public UsbBuilderProfileItemTier Tier { get; init; } = UsbBuilderProfileItemTier.Optional;

    public UsbBuilderProfileSpaceEstimate SpaceEstimate { get; init; } =
        UsbBuilderProfileSpaceEstimate.UserSupplied("varies");

    public string? ManifestEntryName { get; init; }

    public string? UsbRelativePath { get; init; }

    public string Notes { get; init; } = string.Empty;

    public bool RequiresUserSuppliedMedia { get; init; }

    public bool VendorPortalOnly { get; init; }

    public bool LargePayload { get; init; }

    public bool IsSelected
    {
        get => Tier == UsbBuilderProfileItemTier.Required || _isSelected;
        set
        {
            var normalized = Tier == UsbBuilderProfileItemTier.Required || value;
            if (SetProperty(ref _isSelected, normalized))
            {
                OnPropertyChanged(nameof(SpaceChipText));
            }
        }
    }

    public bool CanToggle => Tier != UsbBuilderProfileItemTier.Required;

    public bool IsRecommendedBaseline =>
        Tier == UsbBuilderProfileItemTier.Required || Tier == UsbBuilderProfileItemTier.Recommended;

    public long DetectedBytes
    {
        get => _detectedBytes;
        set
        {
            if (SetProperty(ref _detectedBytes, value))
            {
                OnPropertyChanged(nameof(SpaceChipText));
                OnPropertyChanged(nameof(StatusBadge));
            }
        }
    }

    public bool ExistsOnUsb
    {
        get => _existsOnUsb;
        set
        {
            if (SetProperty(ref _existsOnUsb, value))
            {
                OnPropertyChanged(nameof(StatusBadge));
            }
        }
    }

    public string TierLabel => Tier switch
    {
        UsbBuilderProfileItemTier.Required => "Required",
        UsbBuilderProfileItemTier.Recommended => "Recommended",
        _ => "Optional"
    };

    public string KindLabel => Kind switch
    {
        UsbBuilderProfileItemKind.ManagedDownload => "Managed Download",
        UsbBuilderProfileItemKind.OfficialPage => "Official Link",
        UsbBuilderProfileItemKind.ManualMediaFolder => "Manual Folder",
        UsbBuilderProfileItemKind.Shortcut => "Shortcut",
        UsbBuilderProfileItemKind.HtmlGuide => "Guidance",
        UsbBuilderProfileItemKind.DropFolder => "Manual Folder",
        UsbBuilderProfileItemKind.Iso => "Managed Download",
        UsbBuilderProfileItemKind.Tool => "Managed Download",
        UsbBuilderProfileItemKind.VendorLink => "Shortcut",
        UsbBuilderProfileItemKind.RecoveryMedia => "Recovery Media",
        UsbBuilderProfileItemKind.Driver => "Driver",
        _ => "Item"
    };

    public string TypeBadgeLabel =>
        RequiresUserSuppliedMedia
            ? "User Supplied"
            : KindLabel;

    public string SourceDisplay =>
        string.IsNullOrWhiteSpace(Source)
            ? (string.IsNullOrWhiteSpace(Subcategory) ? "ForgerEMS catalog" : Subcategory)
            : Source;

    public string DescriptionText =>
        string.IsNullOrWhiteSpace(ShortDescription)
            ? Notes
            : ShortDescription;

    public string WarningText
    {
        get
        {
            if (Tier == UsbBuilderProfileItemTier.Required)
            {
                return "Required core item";
            }

            if (RequiresUserSuppliedMedia)
            {
                return "Manual media required";
            }

            if (VendorPortalOnly)
            {
                return "Vendor portal only";
            }

            if (LargePayload)
            {
                return "Large download";
            }

            return string.Empty;
        }
    }

    public bool HasWarningText => !string.IsNullOrWhiteSpace(WarningText);

    public bool CountsAsManagedDownload =>
        !RequiresUserSuppliedMedia &&
        (Kind == UsbBuilderProfileItemKind.ManagedDownload ||
         Kind == UsbBuilderProfileItemKind.Iso ||
         Kind == UsbBuilderProfileItemKind.Tool ||
         Kind == UsbBuilderProfileItemKind.RecoveryMedia ||
         Kind == UsbBuilderProfileItemKind.Driver) &&
        !string.IsNullOrWhiteSpace(ManifestEntryName);

    public bool CountsAsManualOrUserSupplied =>
        RequiresUserSuppliedMedia ||
        Kind == UsbBuilderProfileItemKind.DropFolder ||
        Kind == UsbBuilderProfileItemKind.ManualMediaFolder;

    public long ManagedDownloadEstimateBytes =>
        IsSelected && CountsAsManagedDownload
            ? SpaceEstimate.TypicalBytes ?? 0
            : 0;

    public long KnownUsbFootprintEstimateBytes =>
        IsSelected && !CountsAsManualOrUserSupplied
            ? SpaceEstimate.TypicalBytes ?? 0
            : 0;

    public string SpaceChipText =>
        DetectedBytes > 0
            ? $"On USB: {UsbTargetInfo.FormatBytes(DetectedBytes)}"
            : SpaceEstimate.TypicalBytes is > 0
                ? $"Est. {UsbTargetInfo.FormatBytes(SpaceEstimate.TypicalBytes.Value)}"
                : (string.IsNullOrWhiteSpace(SpaceEstimate.DisplayHint) ? "Varies" : SpaceEstimate.DisplayHint);

    public string StatusBadge
    {
        get
        {
            if (ExistsOnUsb)
            {
                return "Already on USB";
            }

            if (RequiresUserSuppliedMedia)
            {
                return "Manual media required";
            }

            if (VendorPortalOnly)
            {
                return "Vendor portal only";
            }

            if (LargePayload)
            {
                return "Large download";
            }

            return string.Empty;
        }
    }

    public bool HasStatusBadge => !string.IsNullOrEmpty(StatusBadge);

    // Estimated bytes this item would add as new download if selected and not
    // already on the USB. Returns 0 when the item is user-supplied or already
    // present; capacity planner uses this to compute projected new-download
    // size separately from already-occupied space.
    public long ProjectedNewBytes
    {
        get
        {
            if (!IsSelected)
            {
                return 0;
            }

            if (ExistsOnUsb)
            {
                return 0;
            }

            if (RequiresUserSuppliedMedia)
            {
                return 0;
            }

            return SpaceEstimate.TypicalBytes is > 0 ? SpaceEstimate.TypicalBytes.Value : 0;
        }
    }
}
