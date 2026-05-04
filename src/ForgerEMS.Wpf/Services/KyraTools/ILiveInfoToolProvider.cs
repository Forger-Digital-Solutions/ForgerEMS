namespace VentoyToolkitSetup.Wpf.Services.KyraTools;

/// <summary>
/// Future hook for first-class live info (weather, web search). Current releases route live data through <see cref="IKyraTool"/> adapters.
/// </summary>
public interface ILiveInfoToolProvider
{
    string ProviderId { get; }

    bool SupportsWeather { get; }

    bool SupportsWebSearch { get; }
}

/// <summary>Placeholder until a real broker wires remote live-info providers.</summary>
public sealed class DisabledLiveInfoToolProvider : ILiveInfoToolProvider
{
    public string ProviderId => "disabled";

    public bool SupportsWeather => false;

    public bool SupportsWebSearch => false;
}
