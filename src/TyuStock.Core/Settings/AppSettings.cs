using System.Text.Json.Serialization;

namespace TyuStock.Core.Settings;

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
    public NetworkSettings Network { get; set; } = new();
    public List<string> Watchlist { get; set; } = ["SPY", "QQQ", "AAPL", "MSFT", "NVDA", "TSLA"];
    public WindowPlacement? Window { get; set; }

    public static IReadOnlyList<string> SupportedLanguages { get; } = ["zh-CN", "en-US"];

    /// <summary>Clamp out-of-range values (e.g. a hand-edited file) to safe defaults.</summary>
    public AppSettings Normalize()
    {
        if (!SupportedLanguages.Contains(Language)) Language = "zh-CN";
        Network ??= new NetworkSettings();
        Network.RequestTimeoutSeconds = Math.Clamp(Network.RequestTimeoutSeconds, 3, 120);
        Network.MaxRetries = Math.Clamp(Network.MaxRetries, 0, 5);
        Network.ManualProxy ??= "";
        Watchlist ??= [];
        Watchlist = Watchlist.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList();
        return this;
    }
}

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}
