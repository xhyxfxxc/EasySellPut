namespace Meili.App.Services;

/// <summary>Bridge between the settings UI and the network layer (implemented once the data layer is wired in).</summary>
public interface INetworkController
{
    string EffectiveDescription { get; }
    /// <summary>D7: auto-detect is on and found no local proxy, so overseas sources go out directly.</summary>
    bool NoProxyDetected { get; }
    /// <summary>Re-reads proxy settings, re-detects proxies and rebuilds HTTP clients. Returns a human-readable description.</summary>
    Task<string> ReconfigureAsync();
}
