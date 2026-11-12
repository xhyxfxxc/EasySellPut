using System.Collections.Concurrent;

namespace TyuStock.Data.Http;

/// <summary>Per-source minimum spacing between request starts (simple and predictable for small free APIs).</summary>
public sealed class RateLimiter
{
    private sealed class Slot { public readonly SemaphoreSlim Gate = new(1, 1); public DateTime Next = DateTime.MinValue; }
    private readonly ConcurrentDictionary<string, Slot> _slots = new();
    private readonly ConcurrentDictionary<string, TimeSpan> _spacing = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;

    public RateLimiter(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public void Configure(string sourceId, TimeSpan minSpacing) => _spacing[sourceId] = minSpacing;

    public TimeSpan SpacingFor(string sourceId) => _spacing.TryGetValue(sourceId, out var s) ? s : TimeSpan.FromMilliseconds(100);

    public async Task WaitAsync(string sourceId, CancellationToken ct)
    {
        var slot = _slots.GetOrAdd(sourceId, _ => new Slot());
        await slot.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            if (slot.Next > now) await Task.Delay(slot.Next - now, _time, ct).ConfigureAwait(false);
            slot.Next = _time.GetUtcNow().UtcDateTime + SpacingFor(sourceId);
        }
        finally { slot.Gate.Release(); }
    }
}
