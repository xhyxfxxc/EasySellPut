using System.Text;
using System.Text.Json;
using Meili.Core.Abstractions;
using Meili.Core.Models;
using Meili.Data.Http;

namespace Meili.Data.Providers;

/// <summary>Shared plumbing for providers: HTTP through the gateway, caching and JSON parsing with uniform errors.</summary>
public abstract class ProviderBase : IDataProvider
{
    protected readonly HttpGateway Http;
    protected readonly ResponseCache Cache;

    protected ProviderBase(HttpGateway http, ResponseCache cache)
    {
        Http = http;
        Cache = cache;
    }

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract DataCapability Capabilities { get; }

    /// <summary>Minimum spacing between requests to this source.</summary>
    public virtual TimeSpan MinRequestSpacing => TimeSpan.FromMilliseconds(150);

    protected virtual IReadOnlyDictionary<string, string>? DefaultHeaders => null;

    public void RegisterRateLimit() => Http.Limiter.Configure(Id, MinRequestSpacing);

    protected Task<HttpPayload> GetAsync(string url, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null,
        Func<int, bool>? acceptStatus = null, TimeSpan? timeout = null) =>
        Http.GetAsync(Id, url, headers ?? DefaultHeaders, ct, timeout, acceptStatus);

    protected async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null,
        Func<int, bool>? acceptStatus = null, TimeSpan? timeout = null)
    {
        var payload = await GetAsync(url, ct, headers, acceptStatus, timeout).ConfigureAwait(false);
        return ParseJson(payload.Body);
    }

    protected async Task<string> GetTextAsync(string url, CancellationToken ct, Encoding? encoding = null, IReadOnlyDictionary<string, string>? headers = null)
    {
        var payload = await GetAsync(url, ct, headers).ConfigureAwait(false);
        return payload.Text(encoding);
    }

    protected JsonDocument ParseJson(byte[] body)
    {
        try { return JsonDocument.Parse(body, new JsonDocumentOptions { AllowTrailingCommas = true }); }
        catch (JsonException ex)
        {
            var head = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 120));
            var kind = head.TrimStart().StartsWith('<') ? DataErrorKind.Blocked : DataErrorKind.Parse;
            throw new DataSourceException(Id, kind, $"invalid JSON ({head.ReplaceLineEndings(" ")})", null, ex);
        }
    }

    protected JsonDocument ParseJson(string text) => ParseJson(Encoding.UTF8.GetBytes(text));

    protected DataSourceException NoData(string what) => new(Id, DataErrorKind.NoData, what);
    protected DataSourceException Unsupported(string what) => new(Id, DataErrorKind.Unsupported, what);

    protected static string Enc(string s) => Uri.EscapeDataString(s);
    protected static string Norm(string symbol) => symbol.Trim().ToUpperInvariant();
}
