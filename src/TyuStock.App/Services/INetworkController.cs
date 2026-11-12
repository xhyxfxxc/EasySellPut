namespace TyuStock.App.Services;

/// <summary>Bridge between the settings UI and the network layer (implemented once the data layer is wired in).</summary>
public interface INetworkController
{
    string EffectiveDescription { get; }
    /// <summary>Re-reads proxy settings, re-detects proxies and rebuilds HTTP clients. Returns a human-readable description.</summary>
    Task<string> ReconfigureAsync();
}
