using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Meili.Core.Settings;

namespace Meili.Data.Http;

public sealed record HttpPayload(int StatusCode, byte[] Body, TimeSpan Latency, HttpResponseHeaders? Headers, string? ContentType)
{
    public string Text(Encoding? encoding = null) => (encoding ?? Encoding.UTF8).GetString(Body);
}

/// <summary>Raised after every HTTP attempt so the health tracker / status page can show latency and errors.</summary>
public sealed record RequestCompleted(string SourceId, string Url, bool Success, int? StatusCode, TimeSpan Latency, string? Error, int Attempt);

/// <summary>
/// Single entry point for all outbound HTTP: one pooled handler per proxy configuration, browser-like default headers,
/// shared cookies (Yahoo needs them), per-source rate limiting, per-request timeout and bounded retries with backoff.
/// </summary>
public sealed class HttpGateway : IDisposable
{
    public const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    private readonly RateLimiter _limiter;
    private readonly CookieContainer _cookies = new();
    private HttpClient _client;
    private NetworkSettings _settings;
    private readonly Func<ProxyResolution, HttpMessageHandler>? _handlerFactory;

    public ProxyResolution Proxy { get; private set; }
    public CookieContainer Cookies => _cookies;
    public RateLimiter Limiter => _limiter;
    public event Action<RequestCompleted>? Completed;

    static HttpGateway() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // GBK for Sina

    /// <param name="handlerFactory">Test hook: supplies the message handler instead of a real socket handler.</param>
    public HttpGateway(NetworkSettings settings, ProxyResolution? proxy = null, RateLimiter? limiter = null,
        Func<ProxyResolution, HttpMessageHandler>? handlerFactory = null)
    {
        _settings = settings;
        _handlerFactory = handlerFactory;
        _limiter = limiter ?? new RateLimiter();
        Proxy = proxy ?? new ProxyResolution(ProxyMode.System, null, true, "default");
        _client = Build(Proxy);
    }

    /// <summary>Re-resolve the proxy and swap in a new handler. In-flight requests on the old client complete normally.</summary>
    public async Task<ProxyResolution> ReconfigureAsync(NetworkSettings settings, CancellationToken ct = default)
    {
        _settings = settings;
        var resolution = await ProxyDetector.ResolveAsync(settings, ct).ConfigureAwait(false);
        var old = _client;
        _client = Build(resolution);
        Proxy = resolution;
        _ = Task.Delay(TimeSpan.FromSeconds(60)).ContinueWith(_ => old.Dispose(), TaskScheduler.Default);
        return resolution;
    }

    public NetworkSettings Settings => _settings;

    private HttpClient Build(ProxyResolution p)
    {
        if (_handlerFactory is not null) return new HttpClient(_handlerFactory(p), disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = _cookies,
            UseCookies = true,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            MaxConnectionsPerServer = 6,
            AllowAutoRedirect = true,
        };
        if (p.ProxyUri is not null) { handler.UseProxy = true; handler.Proxy = new WebProxy(p.ProxyUri); }
        else if (p.UseSystemProxy) { handler.UseProxy = true; handler.Proxy = HttpClient.DefaultProxy; }
        else handler.UseProxy = false;

        var c = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return c;
    }

    /// <summary>GET with retries. Throws <see cref="DataSourceException"/> on final failure. Non-2xx responses are failures unless <paramref name="acceptStatus"/> allows them.</summary>
    public async Task<HttpPayload> GetAsync(string sourceId, string url, IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken ct = default, TimeSpan? timeout = null, Func<int, bool>? acceptStatus = null, int? maxRetries = null)
    {
        var retries = maxRetries ?? _settings.MaxRetries;
        var perTry = timeout ?? TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds);
        DataSourceException? last = null;
        for (var attempt = 0; attempt <= retries; attempt++)
        {
            if (attempt > 0)
            {
                var backoff = TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 200));
                if (last?.Kind == DataErrorKind.RateLimited) backoff += TimeSpan.FromSeconds(2);
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }
            await _limiter.WaitAsync(sourceId, ct).ConfigureAwait(false);
            var sw = Stopwatch.StartNew();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Accept.ParseAdd("application/json, text/plain, */*");
                if (headers is not null)
                    foreach (var (k, v) in headers)
                    {
                        if (k.Equals("Accept", StringComparison.OrdinalIgnoreCase)) req.Headers.Accept.Clear();
                        if (k.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) req.Headers.UserAgent.Clear();
                        req.Headers.TryAddWithoutValidation(k, v);
                    }
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(perTry);
                using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
                var body = await resp.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
                sw.Stop();
                var code = (int)resp.StatusCode;
                if (resp.IsSuccessStatusCode || acceptStatus?.Invoke(code) == true)
                {
                    Completed?.Invoke(new(sourceId, url, true, code, sw.Elapsed, null, attempt));
                    return new HttpPayload(code, body, sw.Elapsed, resp.Headers, resp.Content.Headers.ContentType?.MediaType);
                }
                var kind = code switch
                {
                    429 => DataErrorKind.RateLimited,
                    401 or 403 => DataErrorKind.Blocked,
                    _ => DataErrorKind.Http,
                };
                last = new DataSourceException(sourceId, kind, $"HTTP {code}", code);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                last = new DataSourceException(sourceId, DataErrorKind.Timeout, $"timed out after {perTry.TotalSeconds:0}s");
            }
            catch (HttpRequestException ex)
            {
                last = new DataSourceException(sourceId, DataErrorKind.Network, ex.InnerException?.Message ?? ex.Message, null, ex);
            }
            catch (IOException ex)
            {
                last = new DataSourceException(sourceId, DataErrorKind.Network, ex.Message, null, ex);
            }
            Completed?.Invoke(new(sourceId, url, false, last.StatusCode, sw.Elapsed, last.Message, attempt));
            if (!last.IsTransient) break;
        }
        throw last!;
    }

    public void Dispose() => _client.Dispose();
}
