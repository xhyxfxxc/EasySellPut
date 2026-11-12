using TyuStock.Core.Alerts;
using TyuStock.Core.Settings;

namespace TyuStock.Core.Tests;

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
}