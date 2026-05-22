using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Services.NetworkPulse;

namespace VentoyToolkitSetup.Wpf.ViewModels;

public sealed class NetworkPulseViewModel : ObservableObject
{
    private readonly NetworkPulseSettingsStore _store;
    private NetworkPulseSettings _settings;
    private NetworkPulseSnapshot _last = new(
        NetworkPulseStatus.Unknown,
        NetworkPulseConfidence.Low,
        false,
        "—",
        NetworkPulseConnectionKind.Unknown,
        null,
        null,
        null,
        null,
        null,
        null,
        NetworkPulseMeasurementKind.Unavailable,
        NetworkPulseMeasurementKind.Unavailable,
        NetworkPulseMeasurementKind.Unavailable,
        null,
        null,
        DateTimeOffset.MinValue,
        string.Empty,
        "Starting…",
        "Lightweight probes only. No full saturation speed test.");

    private string _headerSummaryText = "Internet  …";
    private string _headerToolTip = "Network Pulse";
    private string _detailAdapterText = "—";
    private string _detailConnectionText = "—";
    private string _detailDownloadText = "—";
    private string _detailUploadText = "—";
    private string _detailPingText = "—";
    private string _detailJitterText = "—";
    private string _detailLossText = "—";
    private string _detailStatusText = "Unknown";
    private string _detailConfidenceText = "Low";
    private string _detailLastCheckedText = "—";
    private string _detailDataNotesText = string.Empty;
    private string _detailSafetyNotesText = string.Empty;
    private string _detailPauseReasonText = string.Empty;
    private string _detailLinkCeilingText = string.Empty;
    private string _detailGatewayText = "—";
    private string _detailDnsText = "—";
    private Geometry? _pingSparklineGeometry;
    private Visibility _detailSparklineVisibility = Visibility.Collapsed;
    private string _detailWifiContextText = string.Empty;
    private Visibility _detailWifiVisibility = Visibility.Collapsed;
    private string _detailReliabilityTierText = "Unknown";
    private string _detailReliabilityExplainText = string.Empty;
    private string _detailSmoothingNoteText = string.Empty;
    private string _detailInstantStatusText = "—";
    private string _detailSmoothedChipText = "—";
    private string _clipboardSummaryLine = string.Empty;
    private Visibility _detailPauseReasonVisibility = Visibility.Collapsed;
    private Visibility _headerWidgetVisibility = Visibility.Visible;
    private string _internetWidgetLine1 = "Internet: …";
    private string _internetWidgetLine2 = string.Empty;
    private string _internetWidgetLine3 = string.Empty;
    private Brush _headerStatusBrush = new SolidColorBrush(Color.FromRgb(232, 244, 255));
    private string _status = "Unknown";
    private double? _latencyMs;
    private double? _downloadMbps;
    private double? _uploadMbps;
    private DateTimeOffset? _lastCheckedAt;
    private bool _isMeasuring;
    private bool _isStale;
    private string _detailText = "Starting network checks.";
    private string _lastErrorCategory = string.Empty;

    public NetworkPulseViewModel(NetworkPulseSettingsStore store)
    {
        _store = store;
        _settings = store.Load();
        RefreshHeaderVisibility();
        ToggleDetailsCommand = new RelayCommand(() => IsDetailsOpen = !IsDetailsOpen);
    }

    public RelayCommand ToggleDetailsCommand { get; }

    public NetworkPulseSettings CurrentSettings => _settings;

    public Visibility HeaderWidgetVisibility
    {
        get => _headerWidgetVisibility;
        private set => SetProperty(ref _headerWidgetVisibility, value);
    }

    public string HeaderSummaryText
    {
        get => _headerSummaryText;
        private set => SetProperty(ref _headerSummaryText, value);
    }

    public string HeaderToolTip
    {
        get => _headerToolTip;
        private set => SetProperty(ref _headerToolTip, value);
    }

    private bool _isDetailsOpen;

    public bool IsDetailsOpen
    {
        get => _isDetailsOpen;
        set => SetProperty(ref _isDetailsOpen, value);
    }

    public string DetailAdapterText
    {
        get => _detailAdapterText;
        private set => SetProperty(ref _detailAdapterText, value);
    }

    public string DetailConnectionText
    {
        get => _detailConnectionText;
        private set => SetProperty(ref _detailConnectionText, value);
    }

    public string DetailDownloadText
    {
        get => _detailDownloadText;
        private set => SetProperty(ref _detailDownloadText, value);
    }

    public string DetailUploadText
    {
        get => _detailUploadText;
        private set => SetProperty(ref _detailUploadText, value);
    }

    public string DetailPingText
    {
        get => _detailPingText;
        private set => SetProperty(ref _detailPingText, value);
    }

    public string DetailJitterText
    {
        get => _detailJitterText;
        private set => SetProperty(ref _detailJitterText, value);
    }

    public string DetailLossText
    {
        get => _detailLossText;
        private set => SetProperty(ref _detailLossText, value);
    }

    public string DetailStatusText
    {
        get => _detailStatusText;
        private set => SetProperty(ref _detailStatusText, value);
    }

    public string DetailConfidenceText
    {
        get => _detailConfidenceText;
        private set => SetProperty(ref _detailConfidenceText, value);
    }

    public string DetailLastCheckedText
    {
        get => _detailLastCheckedText;
        private set => SetProperty(ref _detailLastCheckedText, value);
    }

    public string DetailDataNotesText
    {
        get => _detailDataNotesText;
        private set => SetProperty(ref _detailDataNotesText, value);
    }

    public string DetailSafetyNotesText
    {
        get => _detailSafetyNotesText;
        private set => SetProperty(ref _detailSafetyNotesText, value);
    }

    public string DetailPauseReasonText
    {
        get => _detailPauseReasonText;
        private set => SetProperty(ref _detailPauseReasonText, value);
    }

    public Visibility DetailPauseReasonVisibility
    {
        get => _detailPauseReasonVisibility;
        private set => SetProperty(ref _detailPauseReasonVisibility, value);
    }

    public string DetailLinkCeilingText
    {
        get => _detailLinkCeilingText;
        private set => SetProperty(ref _detailLinkCeilingText, value);
    }

    public string DetailGatewayText
    {
        get => _detailGatewayText;
        private set => SetProperty(ref _detailGatewayText, value);
    }

    public string DetailDnsText
    {
        get => _detailDnsText;
        private set => SetProperty(ref _detailDnsText, value);
    }

    public Geometry? PingSparklineGeometry
    {
        get => _pingSparklineGeometry;
        private set => SetProperty(ref _pingSparklineGeometry, value);
    }

    public Visibility DetailSparklineVisibility
    {
        get => _detailSparklineVisibility;
        private set => SetProperty(ref _detailSparklineVisibility, value);
    }

    public string DetailWifiContextText
    {
        get => _detailWifiContextText;
        private set => SetProperty(ref _detailWifiContextText, value);
    }

    public Visibility DetailWifiVisibility
    {
        get => _detailWifiVisibility;
        private set => SetProperty(ref _detailWifiVisibility, value);
    }

    public string DetailReliabilityTierText
    {
        get => _detailReliabilityTierText;
        private set => SetProperty(ref _detailReliabilityTierText, value);
    }

    public string DetailReliabilityExplainText
    {
        get => _detailReliabilityExplainText;
        private set => SetProperty(ref _detailReliabilityExplainText, value);
    }

    public string DetailSmoothingNoteText
    {
        get => _detailSmoothingNoteText;
        private set => SetProperty(ref _detailSmoothingNoteText, value);
    }

    public string DetailInstantStatusText
    {
        get => _detailInstantStatusText;
        private set => SetProperty(ref _detailInstantStatusText, value);
    }

    public string DetailSmoothedChipText
    {
        get => _detailSmoothedChipText;
        private set => SetProperty(ref _detailSmoothedChipText, value);
    }

    public string ClipboardSummaryLine
    {
        get => _clipboardSummaryLine;
        private set => SetProperty(ref _clipboardSummaryLine, value);
    }

    public string InternetWidgetLine1
    {
        get => _internetWidgetLine1;
        private set => SetProperty(ref _internetWidgetLine1, value);
    }

    public string InternetWidgetLine2
    {
        get => _internetWidgetLine2;
        private set => SetProperty(ref _internetWidgetLine2, value);
    }

    public string InternetWidgetLine3
    {
        get => _internetWidgetLine3;
        private set => SetProperty(ref _internetWidgetLine3, value);
    }

    public Brush HeaderStatusBrush
    {
        get => _headerStatusBrush;
        private set => SetProperty(ref _headerStatusBrush, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public double? LatencyMs
    {
        get => _latencyMs;
        private set => SetProperty(ref _latencyMs, value);
    }

    public double? DownloadMbps
    {
        get => _downloadMbps;
        private set => SetProperty(ref _downloadMbps, value);
    }

    public double? UploadMbps
    {
        get => _uploadMbps;
        private set => SetProperty(ref _uploadMbps, value);
    }

    public DateTimeOffset? LastCheckedAt
    {
        get => _lastCheckedAt;
        private set => SetProperty(ref _lastCheckedAt, value);
    }

    public bool IsMeasuring
    {
        get => _isMeasuring;
        private set => SetProperty(ref _isMeasuring, value);
    }

    public bool IsStale
    {
        get => _isStale;
        private set => SetProperty(ref _isStale, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set => SetProperty(ref _detailText, value);
    }

    public string LastErrorCategory
    {
        get => _lastErrorCategory;
        private set => SetProperty(ref _lastErrorCategory, value);
    }

    public bool NetworkPulseEnabled
    {
        get => _settings.Enabled;
        set
        {
            if (_settings.Enabled == value)
            {
                return;
            }

            _settings.Enabled = value;
            Persist();
            OnPropertyChanged(nameof(NetworkPulseEnabled));
            RefreshHeaderVisibility();
        }
    }

    public bool NetworkPulseShowInHeader
    {
        get => _settings.ShowInHeader;
        set
        {
            if (_settings.ShowInHeader == value)
            {
                return;
            }

            _settings.ShowInHeader = value;
            Persist();
            OnPropertyChanged(nameof(NetworkPulseShowInHeader));
            RefreshHeaderVisibility();
        }
    }

    public bool NetworkPulseAutoTestSpeed
    {
        get => _settings.AutoTestSpeedLightly;
        set
        {
            if (_settings.AutoTestSpeedLightly == value)
            {
                return;
            }

            _settings.AutoTestSpeedLightly = value;
            Persist();
            OnPropertyChanged(nameof(NetworkPulseAutoTestSpeed));
        }
    }

    public bool NetworkPulseUploadProbesEnabled
    {
        get => _settings.UploadProbesEnabled;
        set
        {
            if (_settings.UploadProbesEnabled == value)
            {
                return;
            }

            _settings.UploadProbesEnabled = value;
            Persist();
            OnPropertyChanged(nameof(NetworkPulseUploadProbesEnabled));
        }
    }

    public bool NetworkPulsePauseOnMetered
    {
        get => _settings.PauseOnMetered;
        set
        {
            if (_settings.PauseOnMetered == value)
            {
                return;
            }

            _settings.PauseOnMetered = value;
            Persist();
            OnPropertyChanged(nameof(NetworkPulsePauseOnMetered));
        }
    }

    public bool NetworkPulsePauseOnBatterySaver
    {
        get => _settings.PauseOnBatterySaver;
        set
        {
            if (_settings.PauseOnBatterySaver == value)
            {
                return;
            }

            _settings.PauseOnBatterySaver = value;
            Persist();
            OnPropertyChanged(nameof(NetworkPulsePauseOnBatterySaver));
        }
    }

    public bool NetworkPulsePauseDuringHostHeavy
    {
        get => _settings.PauseDuringHostHeavyNetwork;
        set
        {
            if (_settings.PauseDuringHostHeavyNetwork == value)
            {
                return;
            }

            _settings.PauseDuringHostHeavyNetwork = value;
            Persist();
            OnPropertyChanged(nameof(NetworkPulsePauseDuringHostHeavy));
        }
    }

    public int NetworkPulseModeSelectedIndex
    {
        get => _settings.Mode switch
        {
            NetworkPulseMode.Light => 0,
            NetworkPulseMode.Full => 2,
            _ => 1
        };
        set
        {
            var mode = value switch
            {
                0 => NetworkPulseMode.Light,
                2 => NetworkPulseMode.Full,
                _ => NetworkPulseMode.Balanced
            };

            if (_settings.Mode == mode)
            {
                return;
            }

            _settings.Mode = mode;
            _settings.RefreshIntervalSeconds = NetworkPulseCadence.DefaultRefreshSeconds(mode);
            _settings.SpeedProbeCadenceSeconds = NetworkPulseCadence.DefaultProbeCadenceSeconds(mode);
            Persist();
            OnPropertyChanged(nameof(NetworkPulseModeSelectedIndex));
            OnPropertyChanged(nameof(NetworkPulseIntervalSummaryText));
        }
    }

    public string NetworkPulseIntervalSummaryText =>
        $"Refresh every {_settings.RefreshIntervalSeconds}s · Speed sample at most every {_settings.SpeedProbeCadenceSeconds}s (from mode).";

    public void ApplyTestingStatus(NetworkPulseStatus status)
    {
        if (!_settings.Enabled || !_settings.ShowInHeader)
        {
            return;
        }

        if (status != NetworkPulseStatus.Testing)
        {
            return;
        }

        HeaderSummaryText = "Internet: Testing…";
        HeaderToolTip = "Network Pulse: running lightweight checks (not a full speed test).";
        InternetWidgetLine1 = "Internet: Testing…";
        InternetWidgetLine2 = "Running lightweight reachability, latency, and optional throughput samples.";
        InternetWidgetLine3 = string.Empty;
        HeaderStatusBrush = BrushForSurface("Measuring");
        Status = "Measuring";
        IsMeasuring = true;
        IsStale = false;
        DetailText = "Measuring now.";
        LastErrorCategory = string.Empty;
    }

    public void Apply(NetworkPulseUiState ui)
    {
        _last = ui.Snapshot;
        RenderFromState(ui);
    }

    private void RenderFromState(NetworkPulseUiState ui)
    {
        var s = ui.Snapshot;
        var m = ui.Smoothed;
        RefreshHeaderVisibility();

        var downShow = m.SmoothedDownMbps ?? s.DownloadMbps;
        var downKind = s.DownloadKind;
        var upShow = m.SmoothedUpMbps ?? s.UploadMbps;
        var upKind = s.UploadKind;

        var downLabel = FormatSpeedNumber(downShow, downKind);
        var upLabel = FormatSpeedNumber(upShow, upKind);
        var pingLabel = s.LatencyKind == NetworkPulseMeasurementKind.Measured && s.PingMs is > 0
            ? $"{s.PingMs.Value.ToString("0", CultureInfo.InvariantCulture)}"
            : "—";

        var chip = m.StatusChipText;

        HeaderToolTip = BuildHeaderToolTipExtended(s, ui, downLabel, upLabel, pingLabel);

        DetailAdapterText = string.IsNullOrWhiteSpace(s.AdapterName) ? "—" : s.AdapterName;
        DetailConnectionText = ConnectionLabel(s.ConnectionKind);
        DetailDownloadText = DetailMetricLine("Download", s.DownloadMbps, s.DownloadKind, isSpeed: true);
        if (m.SmoothedDownMbps is > 0 && s.DownloadKind == NetworkPulseMeasurementKind.Measured)
        {
            DetailDownloadText += $" (smoothed display ~{m.SmoothedDownMbps!.Value.ToString("0.#", CultureInfo.InvariantCulture)} Mbps)";
        }

        DetailUploadText = DetailMetricLine("Upload", s.UploadMbps, s.UploadKind, isSpeed: true);
        if (m.SmoothedUpMbps is > 0 && s.UploadKind == NetworkPulseMeasurementKind.Measured)
        {
            DetailUploadText += $" (smoothed display ~{m.SmoothedUpMbps!.Value.ToString("0.#", CultureInfo.InvariantCulture)} Mbps)";
        }

        DetailPingText = NetworkPulseQualityVocabulary.FormatPingLine(s.PingMs, s.LatencyKind);
        DetailJitterText = NetworkPulseQualityVocabulary.FormatJitterLine(s.JitterMs);
        DetailLossText = NetworkPulseQualityVocabulary.FormatLossLine(s.PacketLossPercent);

        DetailInstantStatusText = StatusLabel(s.Status);
        DetailSmoothedChipText = m.StatusChipText;
        DetailStatusText = $"{StatusLabel(s.Status)} (instant) · {m.StatusChipText} (header view)";

        DetailConfidenceText = s.Confidence.ToString();
        DetailLastCheckedText = s.LastCheckedUtc == DateTimeOffset.MinValue
            ? "—"
            : s.LastCheckedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        DetailDataNotesText = s.DataSourceNotes;
        DetailSafetyNotesText = s.SafetyNotes;
        var pause = !string.IsNullOrWhiteSpace(s.PauseReason);
        DetailPauseReasonText = s.PauseReason;
        DetailPauseReasonVisibility = pause ? Visibility.Visible : Visibility.Collapsed;
        DetailLinkCeilingText = s.AdapterLinkMbpsEstimated is > 0
            ? $"Adapter link ceiling (estimated, not internet speed): ~{s.AdapterLinkMbpsEstimated.Value.ToString("0", CultureInfo.InvariantCulture)} Mbps"
            : "Adapter link ceiling: unknown (not used as internet speed).";
        DetailGatewayText = s.GatewayPingMs is > 0
            ? $"{s.GatewayPingMs.Value.ToString("0.0", CultureInfo.InvariantCulture)} ms ICMP to default gateway (measured)"
            : "— (not measured this cycle)";
        DetailDnsText = s.DnsLookupMs is > 0
            ? $"{s.DnsLookupMs.Value.ToString("0.0", CultureInfo.InvariantCulture)} ms lookup sample (may be cached)"
            : "—";

        if (!string.IsNullOrWhiteSpace(ui.WifiContextLine))
        {
            DetailWifiContextText = ui.WifiContextLine!;
            DetailWifiVisibility = Visibility.Visible;
        }
        else
        {
            DetailWifiContextText = string.Empty;
            DetailWifiVisibility = Visibility.Collapsed;
        }

        DetailReliabilityTierText = NetworkPulseReliabilityScorer.TierLabel(ui.ReliabilityTier);
        DetailReliabilityExplainText = ui.ReliabilityExplain;
        DetailSmoothingNoteText = ui.SmoothingNote;

        PingSparklineGeometry = ui.PingSparklineGeometry;
        DetailSparklineVisibility = ui.PingSparklineGeometry is null ? Visibility.Collapsed : Visibility.Visible;

        ClipboardSummaryLine = ui.ClipboardSummaryLine;

        InternetWidgetLine1 = ui.InternetWidgetLine1;
        InternetWidgetLine2 = ui.InternetWidgetLine2;
        InternetWidgetLine3 = ui.InternetWidgetLine3;
        HeaderSummaryText = ui.InternetWidgetLine1;
        var surface = ExtractSurface(ui.InternetWidgetLine1);
        HeaderStatusBrush = BrushForSurface(surface);
        Status = surface;
        LatencyMs = s.LatencyKind == NetworkPulseMeasurementKind.Measured ? s.PingMs : null;
        DownloadMbps = s.DownloadKind == NetworkPulseMeasurementKind.Measured ? s.DownloadMbps : null;
        UploadMbps = s.UploadKind == NetworkPulseMeasurementKind.Measured ? s.UploadMbps : null;
        LastCheckedAt = s.LastCheckedUtc == DateTimeOffset.MinValue ? null : s.LastCheckedUtc;
        IsMeasuring = s.Status == NetworkPulseStatus.Testing || surface.Equals("Measuring", StringComparison.OrdinalIgnoreCase);
        IsStale = surface.Equals("Stale", StringComparison.OrdinalIgnoreCase);
        DetailText = string.IsNullOrWhiteSpace(ui.InternetWidgetLine3) ? s.DataSourceNotes : ui.InternetWidgetLine3;
        LastErrorCategory = ClassifyLastErrorCategory(s, surface);
    }

    private static string FormatSpeedNumber(double? mbps, NetworkPulseMeasurementKind kind)
    {
        if (kind == NetworkPulseMeasurementKind.Paused)
        {
            return "paused";
        }

        if (kind == NetworkPulseMeasurementKind.Unavailable || mbps is null or <= 0)
        {
            return "—";
        }

        var m = mbps.Value;
        return m >= 100 ? m.ToString("0", CultureInfo.InvariantCulture) : m.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private void RefreshHeaderVisibility()
    {
        HeaderWidgetVisibility = _settings.Enabled && _settings.ShowInHeader
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Persist()
    {
        NetworkPulseSettingsStore.ApplyDefaults(_settings);
        _store.Save(_settings);
        OnPropertyChanged(nameof(NetworkPulseIntervalSummaryText));
    }

    private static string StatusLabel(NetworkPulseStatus status) =>
        status switch
        {
            NetworkPulseStatus.Good => "Good",
            NetworkPulseStatus.Slow => "Slow",
            NetworkPulseStatus.Unstable => "Unstable",
            NetworkPulseStatus.Limited => "Limited",
            NetworkPulseStatus.Offline => "Offline",
            NetworkPulseStatus.Unknown => "Unknown",
            NetworkPulseStatus.Testing => "Testing",
            NetworkPulseStatus.Paused => "Paused",
            _ => "Unknown"
        };

    private static string ConnectionLabel(NetworkPulseConnectionKind k) =>
        k switch
        {
            NetworkPulseConnectionKind.Ethernet => "Ethernet",
            NetworkPulseConnectionKind.WiFi => "Wi-Fi",
            NetworkPulseConnectionKind.Other => "Other / mixed",
            _ => "Unknown"
        };

    private static string KindWord(NetworkPulseMeasurementKind k) =>
        k switch
        {
            NetworkPulseMeasurementKind.Measured => "Measured",
            NetworkPulseMeasurementKind.Estimated => "Estimated",
            NetworkPulseMeasurementKind.Unavailable => "not measured this cycle",
            NetworkPulseMeasurementKind.Paused => "probe paused",
            _ => "not measured this cycle"
        };

    private static string FormatSpeedLabel(double? mbps, NetworkPulseMeasurementKind kind)
    {
        if (kind == NetworkPulseMeasurementKind.Paused)
        {
            return "paused";
        }

        if (kind == NetworkPulseMeasurementKind.Unavailable || mbps is null or <= 0)
        {
            return "—";
        }

        var m = mbps.Value;
        var text = m >= 100 ? m.ToString("0", CultureInfo.InvariantCulture) : m.ToString("0.#", CultureInfo.InvariantCulture);
        return $"{text} Mbps";
    }

    private static string DetailMetricLine(string label, double? mbps, NetworkPulseMeasurementKind kind, bool isSpeed)
    {
        if (!isSpeed)
        {
            return "—";
        }

        if (kind == NetworkPulseMeasurementKind.Paused)
        {
            return $"{label}: — (probe paused)";
        }

        if (kind == NetworkPulseMeasurementKind.Unavailable || mbps is null or <= 0)
        {
            return $"{label}: — (not measured this cycle)";
        }

        var m = mbps.Value;
        var text = m >= 100 ? m.ToString("0", CultureInfo.InvariantCulture) : m.ToString("0.#", CultureInfo.InvariantCulture);
        return $"{label}: {text} Mbps (lightweight sample)";
    }

    private static string BuildHeaderToolTipExtended(NetworkPulseSnapshot s, NetworkPulseUiState ui, string down, string up, string ping)
    {
        var reach = s.InternetReachable ? "Reachable" : "Not reachable (HTTPS probe)";
        var rel = NetworkPulseReliabilityScorer.TierLabel(ui.ReliabilityTier);
        return
            $"Network Pulse — {reach}. " +
            $"Down {down} ({KindWord(s.DownloadKind)}), Up {up} ({KindWord(s.UploadKind)}), Ping {ping} ms ({KindWord(s.LatencyKind)}). " +
            $"Instant: {StatusLabel(s.Status)}. Header chip: {ui.Smoothed.StatusChipText}. Reliability: {rel}. " +
            ui.SmoothingNote.TrimEnd() +
            " Adapter link speed is never shown as internet speed.";
    }

    private static string ExtractSurface(string line1)
    {
        const string prefix = "Internet:";
        if (!line1.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return "Unknown";
        }

        var value = line1[prefix.Length..].Trim();
        var dot = value.IndexOf('·');
        return (dot >= 0 ? value[..dot] : value).Trim();
    }

    // Two-part surfaces such as "Online — checks inconsistent" share their colour with the
    // base ("Online" → green-ish). Map by checking the first word so future variations don't
    // silently fall to the default neutral colour.
    private static SolidColorBrush BrushForSurface(string surface)
    {
        var head = ExtractSurfaceHead(surface);
        return head switch
        {
            "Online" => new SolidColorBrush(Color.FromRgb(134, 239, 172)),
            "Measuring" => new SolidColorBrush(Color.FromRgb(186, 230, 253)),
            "Stale" => new SolidColorBrush(Color.FromRgb(253, 224, 71)),
            "Limited" => new SolidColorBrush(Color.FromRgb(251, 191, 36)),
            "Offline" => new SolidColorBrush(Color.FromRgb(252, 165, 165)),
            "Disabled" => new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            _ => new SolidColorBrush(Color.FromRgb(232, 244, 255))
        };
    }

    private static string ExtractSurfaceHead(string surface)
    {
        if (string.IsNullOrEmpty(surface))
        {
            return string.Empty;
        }

        // Match the part before an em-dash, en-dash, hyphen, or " (": "Online — checks inconsistent"
        // and "Online (verification mismatch)" both map to "Online".
        var separators = new[] { " — ", " – ", " - ", " (" };
        foreach (var sep in separators)
        {
            var idx = surface.IndexOf(sep, StringComparison.Ordinal);
            if (idx > 0)
            {
                return surface[..idx].Trim();
            }
        }

        return surface.Trim();
    }

    private static string ClassifyLastErrorCategory(NetworkPulseSnapshot s, string surface)
    {
        if (surface.Equals("Offline", StringComparison.OrdinalIgnoreCase))
        {
            return "NoUsableRouteOrProbe";
        }

        // "Online — checks inconsistent" is informational, not an error. Keep the technical
        // category empty so error-banner consumers (clipboard, logs) do not over-react.
        if (surface.StartsWith("Online", StringComparison.OrdinalIgnoreCase) &&
            surface.Contains("checks", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (surface.Equals("Limited", StringComparison.OrdinalIgnoreCase))
        {
            return s.InternetReachable ? "MixedProbeSignals" : "HttpsProbeFailed";
        }

        if (s.DataSourceNotes.Contains("Download sample failed", StringComparison.OrdinalIgnoreCase))
        {
            return "SpeedProbeUnavailable";
        }

        return string.Empty;
    }
}
