using System.Text.Json.Serialization;

namespace Meili.Core.Settings;

public enum AppThemeMode { System, Light, Dark }

public enum UpDownColorMode
{
    /// <summary>红涨绿跌（中国大陆习惯）</summary>
    RedUp,
    /// <summary>绿涨红跌（美股习惯）</summary>
    GreenUp,
}

public enum ProxyMode
{
    /// <summary>跟随 Windows 系统代理设置。</summary>
    System,
    /// <summary>自动探测本机 Clash/V2Ray 等代理端口，找不到则跟随系统。</summary>
    AutoDetect,
    /// <summary>手动指定代理地址。</summary>
    Manual,
    /// <summary>不使用任何代理。</summary>
    Direct,
}

public sealed class NetworkSettings
{
    public ProxyMode ProxyMode { get; set; } = ProxyMode.AutoDetect;
    /// <summary>例如 http://127.0.0.1:7890 或 socks5://127.0.0.1:7891</summary>
    public string ManualProxy { get; set; } = "";
    public int RequestTimeoutSeconds { get; set; } = 15;
    public int MaxRetries { get; set; } = 2;
}

public sealed class AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    /// <summary>"zh-CN" 或 "en-US"</summary>
    public string Language { get; set; } = "zh-CN";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppThemeMode Theme { get; set; } = AppThemeMode.System;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UpDownColorMode UpDownColors { get; set; } = UpDownColorMode.RedUp;
    /// <summary>
    /// E1: true once the user picked up/down colours. Until then the colours follow the interface language
    /// (Chinese → red up, English → green up, the US convention).
    /// </summary>
    public bool UpDownColorsChosen { get; set; }
    /// <summary>D7: the user closed the dashboard's getting-started card.</summary>
    public bool GuideDismissed { get; set; }
    /// <summary>The user dismissed the first-time hint on the strategy P/L page.</summary>
    public bool StrategyHintDismissed { get; set; }

    /// <summary>E1: colours to use for <paramref name="language"/> (default: the saved language).</summary>
    public UpDownColorMode EffectiveUpDownColors(string? language = null) =>
        UpDownColorsChosen ? UpDownColors : (language ?? Language) == "en-US" ? UpDownColorMode.GreenUp : UpDownColorMode.RedUp;
    public NetworkSettings Network { get; set; } = new();
    /// <summary>Flattened symbol list (kept in sync with <see cref="WatchlistGroups"/> for older files and the dashboard).</summary>
    public List<string> Watchlist { get; set; } = ["SPY", "QQQ", "AAPL", "MSFT", "NVDA", "TSLA"];
    /// <summary>Grouped watchlists. When empty at load, a default group is created from <see cref="Watchlist"/>.</summary>
    public List<WatchlistGroup> WatchlistGroups { get; set; } = [];
    public WindowPlacement? Window { get; set; }
    public AlertSettings Alerts { get; set; } = new();
    public PricingSettings Pricing { get; set; } = new();
    public ChartSettings Charts { get; set; } = new();
    public OptionsPageSettings Options { get; set; } = new();
    /// <summary>D4: what the window's close button does. Default is <see cref="CloseAction.Exit"/> (the close button quits). The user can still choose to hide to the tray, in Settings.</summary>
    public CloseAction CloseAction { get; set; } = CloseAction.Exit;

    public static IReadOnlyList<string> SupportedLanguages { get; } = ["zh-CN", "en-US"];

    /// <summary>Clamp out-of-range values (e.g. a hand-edited file) to safe defaults.</summary>
    public AppSettings Normalize()
    {
        if (!SupportedLanguages.Contains(Language)) Language = "zh-CN";
        Network ??= new NetworkSettings();
        Network.RequestTimeoutSeconds = Math.Clamp(Network.RequestTimeoutSeconds, 3, 120);
        Network.MaxRetries = Math.Clamp(Network.MaxRetries, 0, 5);
        Network.ManualProxy ??= "";
        Pricing ??= new PricingSettings();
        Charts ??= new ChartSettings();
        if (!double.IsFinite(Pricing.FixedRatePercent)) Pricing.FixedRatePercent = 4.0;
        Pricing.FixedRatePercent = Math.Clamp(Pricing.FixedRatePercent, -1, 20);
        if (!Enum.IsDefined(Pricing.RateMode)) Pricing.RateMode = RiskFreeRateMode.Treasury13Week;
        if (!Enum.IsDefined(CloseAction)) CloseAction = CloseAction.Exit;
        if (!Enum.IsDefined(UpDownColors)) UpDownColors = UpDownColorMode.RedUp;
        // Files from before E1: a non-default choice can only have come from the user.
        if (UpDownColors != UpDownColorMode.RedUp) UpDownColorsChosen = true;
        Alerts ??= new AlertSettings();
        Alerts.MaxQuoteDelayMinutes = Math.Clamp(Alerts.MaxQuoteDelayMinutes, 1, 240);
        Alerts.FailureThreshold = Math.Clamp(Alerts.FailureThreshold, 1, 20);
        Watchlist ??= [];
        Watchlist = CleanSymbols(Watchlist);
        WatchlistGroups ??= [];
        foreach (var g in WatchlistGroups)
        {
            g.Id = string.IsNullOrWhiteSpace(g.Id) ? Guid.NewGuid().ToString("N")[..8] : g.Id.Trim();
            g.Name = (g.Name ?? "").Trim();
            g.Symbols = CleanSymbols(g.Symbols);
        }
        WatchlistGroups = WatchlistGroups.Where(g => !string.IsNullOrWhiteSpace(g.Id)).ToList();
        if (WatchlistGroups.Count == 0)
        {
            if (Watchlist.Count == 0)
                Watchlist = ["SPY", "QQQ", "AAPL", "MSFT", "NVDA", "TSLA"];
            WatchlistGroups.Add(new WatchlistGroup { Id = WatchlistGroup.DefaultId, Name = "", Symbols = [.. Watchlist] });
        }
        Watchlist = WatchlistGroups.SelectMany(g => g.Symbols).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return this;
    }

    internal static List<string> CleanSymbols(IEnumerable<string>? raw) =>
        (raw ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList();
}

/// <summary>One named tab on the watchlist. An empty <see cref="Name"/> is shown as the localized default-group label.</summary>
public sealed class WatchlistGroup
{
    public const string DefaultId = "default";
    public string Id { get; set; } = DefaultId;
    public string Name { get; set; } = "";
    public List<string> Symbols { get; set; } = [];
}

public enum RiskFreeRateMode { Treasury13Week, Fixed }

/// <summary>K-line page preferences.</summary>
public sealed class ChartSettings
{
    /// <summary>F7: request dividend-adjusted (adjclose, total-return) daily+ bars instead of split-only.</summary>
    public bool DividendAdjusted { get; set; }

    /// <summary>G3: indicator parameters, edited on the K-line page.</summary>
    public IndicatorSettings Indicators { get; set; } = new();
}

/// <summary>G3: MA/EMA/BOLL/MACD/RSI/KDJ parameters (defaults are the common 20/20/20·2/12·26·9/14/9·3·3).</summary>
public sealed record IndicatorSettings
{
    public int MaPeriod { get; init; } = 20;
    public int EmaPeriod { get; init; } = 20;
    public int BollPeriod { get; init; } = 20;
    public double BollWidth { get; init; } = 2.0;
    public int MacdFast { get; init; } = 12;
    public int MacdSlow { get; init; } = 26;
    public int MacdSignal { get; init; } = 9;
    public int RsiPeriod { get; init; } = 14;
    public int KdjPeriod { get; init; } = 9;
    public int KdjM1 { get; init; } = 3;
    public int KdjM2 { get; init; } = 3;

    /// <summary>Clamps typed values into usable ranges (periods 1–250, band width 0.5–5, MACD fast below slow).</summary>
    public IndicatorSettings Normalized()
    {
        static int P(int v) => Math.Clamp(v, 1, 250);
        var fast = P(MacdFast);
        var slow = Math.Max(P(MacdSlow), fast + 1);
        return this with
        {
            MaPeriod = P(MaPeriod), EmaPeriod = P(EmaPeriod), BollPeriod = Math.Max(2, P(BollPeriod)),
            BollWidth = Math.Clamp(Math.Round(BollWidth, 2), 0.5, 5), MacdFast = fast, MacdSlow = Math.Min(slow, 251), MacdSignal = P(MacdSignal),
            RsiPeriod = P(RsiPeriod), KdjPeriod = P(KdjPeriod), KdjM1 = P(KdjM1), KdjM2 = P(KdjM2),
        };
    }
}

/// <summary>Options page preferences.</summary>
public sealed class OptionsPageSettings
{
    /// <summary>E6: compact T-table preset (narrow columns; volume/gamma/theta/vega hidden) for small or scaled screens.</summary>
    public bool CompactColumns { get; set; }
}

/// <summary>F5: risk-free rate for locally computed IV / Greeks / strategy curves.</summary>
public sealed class PricingSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RiskFreeRateMode RateMode { get; set; } = RiskFreeRateMode.Treasury13Week;
    /// <summary>Used in Fixed mode, and as the fallback when the 13-week T-bill yield cannot be fetched.</summary>
    public double FixedRatePercent { get; set; } = 4.0;
}

/// <summary>Alert monitoring guards (F3, F10).</summary>
public sealed class AlertSettings
{
    /// <summary>Quotes known to be older than this (declared delay or measured lag) never trigger an alert.</summary>
    public int MaxQuoteDelayMinutes { get; set; } = 20;
    /// <summary>Consecutive failed checks before the user is told monitoring is interrupted.</summary>
    public int FailureThreshold { get; set; } = 3;
}

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>D4: close-button behaviour.</summary>
public enum CloseAction { Ask, MinimizeToTray, Exit }

/// <summary>D4: what actually happens when the window is asked to close.</summary>
public enum CloseOutcome { Exit, HideToTray, Ask }

public static class CloseDecision
{
    /// <param name="exitRequested">Exit from the tray menu or after the user answered "exit".</param>
    /// <param name="exitOnClose">The <c>--exit-on-close</c> launch option (tests, verification runs).</param>
    public static CloseOutcome Decide(CloseAction setting, bool exitRequested, bool exitOnClose) =>
        exitRequested || exitOnClose ? CloseOutcome.Exit : setting switch
        {
            CloseAction.Exit => CloseOutcome.Exit,
            CloseAction.MinimizeToTray => CloseOutcome.HideToTray,
            _ => CloseOutcome.Ask,
        };
}
