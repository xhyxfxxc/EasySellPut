using System.Net;
using System.Text;
using System.Text.Json;
using Meili.Core.Settings;
using Meili.Data.Http;

namespace Meili.Data.Tests;

internal static class Fx
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    public static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));
    public static string Text(string name) => File.ReadAllText(PathOf(name));
    public static JsonDocument Json(string name) => JsonDocument.Parse(Bytes(name));
}

/// <summary>Live network test: runs only when MEILI_LIVE=1 (e.g. build\test-live.ps1).</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MEILI_LIVE") != "1") Skip = "live test (set MEILI_LIVE=1)";
    }
}

/// <summary>Scripted HTTP handler: returns queued responses (or throws) and records requests.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _script = new();
    public List<HttpRequestMessage> Requests { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage>? Fallback { get; set; }

    public FakeHandler Enqueue(HttpStatusCode code, string body = "{}") { _script.Enqueue(_ => Resp(code, body)); return this; }
    public FakeHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> f) { _script.Enqueue(f); return this; }
    public FakeHandler Throw(Exception ex) { _script.Enqueue(_ => throw ex); return this; }

    public static HttpResponseMessage Resp(HttpStatusCode code, string body) =>
        new(code) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };
    public static HttpResponseMessage Resp(HttpStatusCode code, byte[] body) => new(code) { Content = new ByteArrayContent(body) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        var f = _script.Count > 0 ? _script.Dequeue() : Fallback ?? (_ => Resp(HttpStatusCode.OK, "{}"));
        return Task.FromResult(f(request));
    }
}

internal static class TestHttp
{
    public static HttpGateway Gateway(FakeHandler h, int retries = 2) =>
        new(new NetworkSettings { MaxRetries = retries, RequestTimeoutSeconds = 5 }, null, null, _ => h);

    private static readonly Lazy<HttpGateway> _live = new(() =>
    {
        var s = new NetworkSettings { ProxyMode = ProxyMode.AutoDetect, MaxRetries = 2, RequestTimeoutSeconds = 25 };
        return new HttpGateway(s, ProxyDetector.ResolveAsync(s).GetAwaiter().GetResult());
    });
    public static HttpGateway Live() => _live.Value;
}