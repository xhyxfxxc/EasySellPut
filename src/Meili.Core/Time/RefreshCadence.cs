namespace Meili.Core.Time;

/// <summary>C3: how often pages poll each kind of data, aligned with how often the source can actually change.</summary>
public static class RefreshCadence
{
    /// <summary>
    /// Option chains: CBOE's free chain is 15-minute delayed and ~1.5 MB (AAPL) to ~14 MB (SPX) per download,
    /// so the options and strategy pages refresh every 90 s instead of 45 s.
    /// </summary>
    public static readonly TimeSpan OptionChain = TimeSpan.FromSeconds(90);

    /// <summary>The chain cache lives slightly shorter than one refresh tick, so every tick gets one fresh download and none is wasted.</summary>
    public static readonly TimeSpan OptionChainCache = OptionChain - TimeSpan.FromSeconds(5);
}
