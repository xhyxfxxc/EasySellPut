using System.Collections.Concurrent;
using Meili.Core.Abstractions;
using Meili.Core.Models;
using Meili.Data.Http;

namespace Meili.Data.Routing;

public enum HealthState { Unknown, Healthy, Degraded, Down }

/// <summary>Live health of one source, shown on the data-source status page and used by the circuit breaker.</summary>
public sealed class SourceHealth
{
    internal readonly object Gate = new();
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required DataCapability Capabilities { get; init; }
    public int Successes { get; internal set; }
    public int Failures { get; internal set; }
    public int ConsecutiveFailures { get; internal set; }
    public TimeSpan? LastLatency { get; internal set; }
    public TimeSpan? AverageLatency { get; internal set; }
    public string? LastError { get; internal set; }
    public DateTimeOffset? LastErrorAt { get; internal set; }
    public DateTimeOffset? LastSuccessAt { get; internal set; }
    public DateTimeOffset? LastUsedAt { get; internal set; }
    public DateTimeOffset? CircuitOpenUntil { get; internal set; }

    public HealthState State(DateTimeOffset now)
    {
        if (CircuitOpenUntil is { } u && u > now) return HealthState.Down;
        if (Successes == 0 && Failures == 0) return HealthState.Unknown;
        if (ConsecutiveFailures >= HealthTracker.FailuresToOpen) return HealthState.Down;
        if (ConsecutiveFailures > 0) return Successes == 0 ? HealthState.Down : HealthState.Degraded;
        if (LastErrorAt is { } e && now - e < TimeSpan.FromMinutes(2)) return HealthState.Degraded;
        return HealthState.Healthy;
    }
}

/// <summary>
/// Tracks per-source outcomes and implements a simple circuit breaker: after <see cref="FailuresToOpen"/> consecutive
/// hard failures a source is skipped for 30 s, doubling up to 5 min; after that one trial call is let through (half-open).
/// </summary>
public sealed class HealthTracker
{
    public const int FailuresToOpen = 3;
    private readonly ConcurrentDictionary<string, SourceHealth> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = [];
    private readonly TimeProvider _time;
    public event Action<SourceHealth>? Changed;

    public HealthTracker(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public void Register(IDataProvider p)
    {
        if (_sources.TryAdd(p.Id, new SourceHealth { Id = p.Id, DisplayName = p.DisplayName, Capabilities = p.Capabilities }))
            lock (_order) _order.Add(p.Id);
    }

    public IReadOnlyList<SourceHealth> All { get { lock (_order) return _order.Select(id => _sources[id]).ToList(); } }
    public SourceHealth? Get(string id) => _sources.TryGetValue(id, out var h) ? h : null;
    public DateTimeOffset Now => _time.GetUtcNow();

    /// <summary>Feed per-attempt HTTP outcomes (latency and transport errors) from the gateway.</summary>
    public void Attach(HttpGateway gateway) => gateway.Completed += e =>
    {
        if (Get(e.SourceId) is not { } h) return;
        lock (h.Gate)
        {
            if (e.Success) Latency(h, e.Latency);
            else { h.LastError = e.Error; h.LastErrorAt = Now; }
        }
        Changed?.Invoke(h);
    };

    public bool IsAvailable(string id)
    {
        if (Get(id) is not { } h) return true;
        lock (h.Gate) return h.CircuitOpenUntil is not { } u || u <= Now;
    }

    public void RecordSuccess(string id, TimeSpan elapsed)
    {
        if (Get(id) is not { } h) return;
        lock (h.Gate)
        {
            h.Successes++; h.ConsecutiveFailures = 0; h.CircuitOpenUntil = null;
            h.LastSuccessAt = h.LastUsedAt = Now;
            if (h.LastLatency is null) Latency(h, elapsed);
        }
        Changed?.Invoke(h);
    }

    /// <param name="hard">False for "this source simply has no data for that symbol" — recorded, but it does not trip the breaker.</param>
    public void RecordFailure(string id, string error, bool hard = true)
    {
        if (Get(id) is not { } h) return;
        lock (h.Gate)
        {
            h.LastError = error; h.LastErrorAt = Now;
            if (hard)
            {
                h.Failures++; h.ConsecutiveFailures++;
                if (h.ConsecutiveFailures >= FailuresToOpen)
                {
                    var n = h.ConsecutiveFailures - FailuresToOpen;
                    h.CircuitOpenUntil = Now + TimeSpan.FromSeconds(Math.Min(300, 30 * Math.Pow(2, Math.Min(n, 4))));
                }
            }
        }
        Changed?.Invoke(h);
    }

    public void Reset()
    {
        foreach (var h in _sources.Values)
            lock (h.Gate) { h.ConsecutiveFailures = 0; h.CircuitOpenUntil = null; }
    }

    private static void Latency(SourceHealth h, TimeSpan t)
    {
        h.LastLatency = t;
        h.AverageLatency = h.AverageLatency is { } a ? TimeSpan.FromMilliseconds(a.TotalMilliseconds * 0.7 + t.TotalMilliseconds * 0.3) : t;
    }
}