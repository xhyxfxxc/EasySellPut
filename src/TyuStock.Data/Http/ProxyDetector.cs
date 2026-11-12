using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using TyuStock.Core.Settings;

namespace TyuStock.Data.Http;

/// <summary>Outcome of proxy resolution: which proxy (if any) requests will use and why.</summary>
public sealed record ProxyResolution(ProxyMode Mode, Uri? ProxyUri, bool UseSystemProxy, string Origin)
{
    public string Describe() => Mode switch
    {
        ProxyMode.Direct => "Direct",
        ProxyMode.Manual => $"Manual {ProxyUri}",
        ProxyMode.System => UseSystemProxy ? "System proxy" : "Direct",
        _ => ProxyUri is not null ? $"Auto {ProxyUri} ({Origin})" : $"Auto: none found, {(UseSystemProxy ? "system" : "direct")}",
    };
}

/// <summary>
/// Finds a usable local proxy (Clash / Clash Verge / V2RayN etc.). Checks, in order: the Windows proxy setting
/// (even when switched off, Clash leaves its port there), HTTPS_PROXY/HTTP_PROXY/ALL_PROXY, then well-known local ports.
/// A candidate is accepted only if its TCP port is actually listening.
/// </summary>
public static class ProxyDetector
{
    /// <summary>Well-known local proxy ports: Clash (7890), Clash Verge Rev (7897), V2RayN (10809 http / 10808 socks), generic socks (1080).</summary>
    public static readonly (int Port, string Scheme, string Name)[] CommonPorts =
    [
        (7890, "http", "Clash"), (7897, "http", "Clash Verge"), (10809, "http", "V2RayN"), (8889, "http", "local"),
        (10808, "socks5", "V2RayN socks"), (7891, "socks5", "Clash socks"), (1080, "socks5", "socks"),
    ];

    public static async Task<ProxyResolution> ResolveAsync(NetworkSettings settings, CancellationToken ct = default)
    {
        switch (settings.ProxyMode)
        {
            case ProxyMode.Direct:
                return new(ProxyMode.Direct, null, false, "user");
            case ProxyMode.System:
                return new(ProxyMode.System, null, true, "windows");
            case ProxyMode.Manual:
                if (TryParseProxy(settings.ManualProxy, out var manual)) return new(ProxyMode.Manual, manual, false, "user");
                return new(ProxyMode.Manual, null, true, "invalid manual proxy, using system");
        }

        foreach (var (uri, origin) in Candidates())
        {
            if (await IsListeningAsync(uri.Host, uri.Port, TimeSpan.FromMilliseconds(300), ct))
                return new(ProxyMode.AutoDetect, uri, false, origin);
        }
        return new(ProxyMode.AutoDetect, null, true, "none");
    }

    internal static IEnumerable<(Uri Uri, string Origin)> Candidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            var reg = ReadWindowsProxyServer();
            if (reg is not null && TryParseWindowsProxyServer(reg, out var r) && IsLoopback(r) && seen.Add(r.Authority))
                yield return (r, "Windows proxy setting");
        }
        foreach (var name in new[] { "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY", "https_proxy", "http_proxy", "all_proxy" })
        {
            var v = Environment.GetEnvironmentVariable(name);
            if (TryParseProxy(v, out var e) && seen.Add(e.Authority)) yield return (e, name);
        }
        foreach (var (port, scheme, label) in CommonPorts)
        {
            var u = new Uri($"{scheme}://127.0.0.1:{port}");
            if (seen.Add(u.Authority)) yield return (u, label);
        }
    }

    public static bool TryParseProxy(string? text, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (!text.Contains("://")) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var u) || u.Port <= 0) return false;
        if (u.Scheme is not ("http" or "https" or "socks5" or "socks4" or "socks4a")) return false;
        uri = new Uri($"{u.Scheme}://{u.Host}:{u.Port}");
        return true;
    }

    /// <summary>Parses the Windows "ProxyServer" value: "host:port" or "http=host:port;https=host:port;socks=host:port".</summary>
    public static bool TryParseWindowsProxyServer(string value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!value.Contains('=')) return TryParseProxy(value, out uri);
        var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].ToLowerInvariant(), p => p[1]);
        if (parts.TryGetValue("https", out var h) && TryParseProxy(h, out uri)) return true;
        if (parts.TryGetValue("http", out h) && TryParseProxy(h, out uri)) return true;
        if (parts.TryGetValue("socks", out h) && TryParseProxy("socks5://" + h, out uri)) return true;
        return false;
    }

    private static bool IsLoopback(Uri u) =>
        u.IsLoopback || (IPAddress.TryParse(u.Host, out var ip) && IPAddress.IsLoopback(ip));

    [SupportedOSPlatform("windows")]
    private static string? ReadWindowsProxyServer()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            return key?.GetValue("ProxyServer") as string;
        }
        catch { return null; } // expected: no readable proxy setting simply means "no system proxy"
    }

    public static async Task<bool> IsListeningAsync(string host, int port, TimeSpan timeout, CancellationToken ct = default)
    {
        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try { await client.ConnectAsync(host, port, cts.Token); return client.Connected; }
        catch { return false; } // expected: a refused/timed-out probe is the answer ("not listening")
    }
}
