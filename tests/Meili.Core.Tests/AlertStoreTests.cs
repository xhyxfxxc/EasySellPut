using Meili.Core.Alerts;
using Meili.Core.Settings;

namespace Meili.Core.Tests;

public class AlertStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tyu-alerts-" + Guid.NewGuid().ToString("N"));
    public AlertStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static PriceAlert Rule(string symbol = "AAPL", AlertKind kind = AlertKind.PriceAbove, double threshold = 180) => new()
    {
        Symbol = symbol,
        Kind = kind,
        Threshold = threshold,
        Repeat = AlertRepeatMode.Once,
        CooldownMinutes = 15,
    };

    [Fact]
    public void Upsert_PersistsAndReloads()
    {
        var store = new AlertStore(_dir);
        store.Load();
        var saved = store.Upsert(Rule("aapl", AlertKind.PriceBelow, 150));
        Assert.Equal("AAPL", saved.Symbol);
        Assert.False(string.IsNullOrWhiteSpace(saved.Id));

        var again = new AlertStore(_dir).Load();
        var loaded = again.Alerts.Single();
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal("AAPL", loaded.Symbol);
        Assert.Equal(AlertKind.PriceBelow, loaded.Kind);
        Assert.Equal(150, loaded.Threshold);
    }

    [Fact]
    public void Normalize_ClampsCooldown_DropsEmptySymbol_CapsHistory()
    {
        var store = new AlertStore(_dir);
        store.Load();
        store.Upsert(new PriceAlert { Symbol = "  spy ", CooldownMinutes = 0 });
        store.Upsert(new PriceAlert { Symbol = "   ", Threshold = 1 }); // dropped
        var results = new List<(PriceAlert, AlertHistoryEntry?)>();
        var rule = store.Alerts.Single();
        for (var i = 0; i < 250; i++)
        {
            results.Add((rule, new AlertHistoryEntry
            {
                AlertId = rule.Id,
                Symbol = "SPY",
                Kind = AlertKind.PriceAbove,
                Threshold = 1,
                Value = i,
                FiredAt = DateTimeOffset.UtcNow.AddMinutes(i),
            }));
        }
        store.ApplyResults(results);
        Assert.Equal(15, store.Alerts.Single().CooldownMinutes);
        Assert.Equal("SPY", store.Alerts.Single().Symbol);
        Assert.Equal(AlertStore.MaxHistory, store.History.Count);
        Assert.Equal(249, store.History[0].Value); // newest first
    }

    [Fact]
    public void SetEnabled_True_ClearsHasSeenQuote()
    {
        var store = new AlertStore(_dir);
        store.Load();
        var a = store.Upsert(new PriceAlert { Symbol = "MSFT", HasSeenQuote = true, LastSeenPrice = 400, Enabled = false });
        store.SetEnabled(a.Id, true);
        var loaded = store.Find(a.Id)!;
        Assert.True(loaded.Enabled);
        Assert.False(loaded.HasSeenQuote);
    }

    [Fact]
    public void Remove_DeletesRule()
    {
        var store = new AlertStore(_dir);
        store.Load();
        var a = store.Upsert(Rule("NVDA"));
        Assert.True(store.Remove(a.Id));
        Assert.Empty(store.Alerts);
        Assert.Empty(new AlertStore(_dir).Load().Alerts);
    }

    [Fact]
    public void CorruptFile_IsBackedUp_AndDefaultsUsed()
    {
        var store = new AlertStore(_dir);
        File.WriteAllText(store.FilePath, "{ this is not json");
        var doc = store.Load();
        Assert.Empty(doc.Alerts);
        Assert.NotEmpty(Directory.GetFiles(_dir, "alerts.json.corrupt-*"));
    }

    [Fact]
    public void EnabledSymbols_AreDistinctAndIgnoreDisabled()
    {
        var store = new AlertStore(_dir);
        store.Load();
        store.Upsert(Rule("AAPL"));
        store.Upsert(Rule("aapl", AlertKind.PriceBelow, 100));
        var off = store.Upsert(Rule("TSLA"));
        store.SetEnabled(off.Id, false);
        Assert.Equal(["AAPL"], store.EnabledSymbols);
    }

    // ---- C4: write-coalescing ----

    private static (PriceAlert, AlertHistoryEntry?) Seen(PriceAlert a, double price)
    {
        var n = a.Clone(); n.HasSeenQuote = true; n.LastSeenPrice = price;
        return (n, null);
    }

    [Fact]
    public void Bookkeeping_updates_stay_in_memory_until_the_flush_interval()
    {
        var now = new DateTimeOffset(2026, 11, 12, 14, 0, 0, TimeSpan.Zero);
        var store = new AlertStore(_dir) { Clock = () => now };
        store.Load();
        var a = store.Upsert(new PriceAlert { Symbol = "AAPL", Threshold = 300 });
        var writes = store.WriteCount;

        for (var i = 0; i < 50; i++) store.ApplyResults([Seen(store.Find(a.Id)!, 200 + i)]);
        Assert.Equal(writes, store.WriteCount);             // 50 batches, no disk writes
        Assert.True(store.IsDirty);
        Assert.Equal(249, store.Find(a.Id)!.LastSeenPrice);  // but memory is current

        now += AlertStore.FlushInterval;
        store.ApplyResults([Seen(store.Find(a.Id)!, 250)]);
        Assert.Equal(writes + 1, store.WriteCount);
        Assert.False(store.IsDirty);
        Assert.Equal(250, new AlertStore(_dir).Load().Alerts.Single().LastSeenPrice);
    }

    [Fact]
    public void Fires_are_written_immediately_and_flush_persists_pending_state()
    {
        var store = new AlertStore(_dir);
        store.Load();
        var a = store.Upsert(new PriceAlert { Symbol = "MSFT", Threshold = 1 });
        store.ApplyResults([Seen(store.Find(a.Id)!, 5)]);
        Assert.True(store.IsDirty);
        Assert.Equal(0, new AlertStore(_dir).Load().Alerts.Single().LastSeenPrice ?? 0);

        store.Flush();
        Assert.False(store.IsDirty);
        Assert.Equal(5, new AlertStore(_dir).Load().Alerts.Single().LastSeenPrice);

        var fired = store.Find(a.Id)!; fired.Enabled = false; fired.LastFiredAt = DateTimeOffset.UtcNow;
        var writes = store.WriteCount;
        store.ApplyResults([(fired, new AlertHistoryEntry { AlertId = a.Id, Symbol = "MSFT", FiredAt = DateTimeOffset.UtcNow })]);
        Assert.Equal(writes + 1, store.WriteCount);
        var reloaded = new AlertStore(_dir).Load();
        Assert.False(reloaded.Alerts.Single().Enabled);
        Assert.Single(reloaded.History);
    }

    [Fact]
    public void Flush_when_clean_does_not_write()
    {
        var store = new AlertStore(_dir);
        store.Load();
        store.Upsert(new PriceAlert { Symbol = "SPY" });
        var writes = store.WriteCount;
        store.Flush();
        Assert.False(store.FlushIfDue());
        Assert.Equal(writes, store.WriteCount);
    }
}
