using TyuStock.Core.Abstractions;
using TyuStock.Core.Models;
using TyuStock.Data.Cache;

namespace TyuStock.Data.Tests;

/// <summary>B2: SQLite K-line cache with tail-only refresh, re-adjustment detection and offline fallback.</summary>
public sealed class BarCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bar-cache-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset D0 = new(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class Inner : IMarketDataService
    {
        public List<BarRequest> Requests { get; } = [];
        public Func<BarRequest, BarSeries>? Make { get; set; }
        public bool Down { get; set; }
        public int Days { get; set; } = 40;
        public double Scale { get; set; } = 1;
        public string Source { get; set; } = "yahoo";
        public PriceAdjustment Basis { get; set; } = PriceAdjustment.Split;

        public Task<BarSeries> GetBarsAsync(BarRequest r, CancellationToken ct = default)
        {
            Requests.Add(r);
            if (Down) throw new HttpRequestException("offline");
            var bars = Enumerable.Range(0, Days).Select(d => Bar.FromSource(D0.AddDays(d), 100 + d, 101 + d, 99 + d, (100.5 + d) * Scale, 1000))
                .Where(b => r.From is null || b.Time >= r.From).ToList();
            return Task.FromResult(new BarSeries { Symbol = r.Symbol, Interval = r.Interval, Bars = bars, Source = Source, FetchedAt = D0.AddDays(Days), Adjustment = Basis });
        }
        public Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Quote>>([]);
        public Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(string symbol, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OptionChain> GetChainAsync(string symbol, DateOnly? expiration = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Fundamentals> GetFundamentalsAsync(string symbol, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FinancialStatements> GetFinancialsAsync(string symbol, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<NewsItem>> GetNewsAsync(string symbol, int count = 20, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SymbolMatch>> SearchAsync(string query, int limit = 10, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private (CachedMarketData Svc, Inner Inner) Make()
    {
        var inner = new Inner();
        return (new CachedMarketData(inner, new MarketDb(Path.Combine(_dir, "market.db"))), inner);
    }

    [Fact]
    public async Task Round_trips_bars_including_missing_fields()
    {
        var db = new MarketDb(Path.Combine(_dir, "rt.db"));
        Assert.Equal(MarketDb.SchemaVersion, db.ReadSchemaVersion());
        var bars = new[] { Bar.FromSource(D0, 1, 2, 0.5, 1.5, 10), Bar.CloseOnly(D0.AddMinutes(1), 1.6, 3) };
        db.SaveBars("AAPL", BarInterval.Minute1, bars, new BarCacheMeta("cboe", D0, D0, 15, false));
        var (back, meta) = db.LoadBars("AAPL", BarInterval.Minute1);
        Assert.Equal(2, back.Count);
        Assert.Equal(bars[0], back[0]);
        Assert.True(double.IsNaN(back[1].Open) && back[1].Incomplete);
        Assert.Equal(("cboe", 15), (meta!.Source, meta.DelayMinutes));
        Assert.Empty(db.LoadBars("AAPL", BarInterval.Day1).Bars);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Second_request_downloads_only_a_tail_and_merges()
    {
        var (svc, inner) = Make();
        var first = await svc.GetBarsAsync(new BarRequest("aapl", BarInterval.Day1));
        Assert.Equal(40, first.Bars.Count);
        inner.Days = 42;                                     // two new sessions since
        var second = await svc.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1));
        Assert.Equal(42, second.Bars.Count);
        Assert.Equal(2, inner.Requests.Count);
        Assert.NotNull(inner.Requests[1].From);              // tail only
        Assert.True(inner.Requests[1].From > D0.AddDays(30));
        Assert.Equal((1, 1), (svc.FullFetches, svc.TailFetches));
        Assert.True(second.Bars.Zip(second.Bars.Skip(1)).All(p => p.First.Time < p.Second.Time));
    }

    [Fact]
    public async Task Readjusted_history_or_new_source_forces_a_full_reload()
    {
        var (svc, inner) = Make();
        await svc.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1));
        inner.Scale = 0.5;                                    // split: every close halves
        var after = await svc.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1));
        Assert.Equal(2, svc.FullFetches);
        Assert.Equal(40, after.Bars.Count);
        Assert.Equal(100.5 * 0.5, after.Bars[0].Close, 6);    // old cached history replaced

        inner.Source = "cboe";
        await svc.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1));
        Assert.Equal(3, svc.FullFetches);
    }

    [Fact]
    public async Task Offline_serves_the_cache_and_says_so()
    {
        var (svc, inner) = Make();
        await svc.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1));
        inner.Down = true;
        var s = await svc.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1));
        Assert.True(s.OfflineCache);
        Assert.Equal(40, s.Bars.Count);
        Assert.Equal(D0.AddDays(40), s.FetchedAt);
        Assert.False(s.IsRealtime);
        await Assert.ThrowsAsync<HttpRequestException>(() => svc.GetBarsAsync(new BarRequest("MSFT", BarInterval.Day1)));
    }

    [Fact]
    public async Task Narrow_first_request_does_not_serve_a_wider_one()
    {
        var (svc, inner) = Make();
        await svc.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1, D0.AddDays(20)));   // e.g. "today's session"
        await svc.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1));                  // default window
        Assert.Equal(2, svc.FullFetches);
        Assert.Null(inner.Requests[1].From);
        var narrow = await svc.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1, D0.AddDays(30)));
        Assert.Equal(1, svc.TailFetches);                                                    // default window covers it
        Assert.All(narrow.Bars, b => Assert.True(b.Time >= D0.AddDays(30)));
    }

    // ---- F7: adjustment basis in the cache ----

    [Fact]
    public async Task Modes_are_cached_separately_and_basis_round_trips()
    {
        var (svc, inner) = Make();
        await svc.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1));
        inner.Basis = PriceAdjustment.SplitDividend;
        var total = await svc.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1, Adjust: AdjustMode.Total));
        Assert.Equal(2, svc.FullFetches);                                 // separate cache entry, not a tail of the split one
        Assert.Equal(PriceAdjustment.SplitDividend, total.Adjustment);
        var (_, meta) = svc.Db.LoadBars("AAPL", BarInterval.Day1, "total");
        Assert.Equal(PriceAdjustment.SplitDividend, meta!.Basis);
        Assert.Equal(PriceAdjustment.Split, svc.Db.LoadBars("AAPL", BarInterval.Day1).Meta!.Basis);

        inner.Down = true;                                                 // offline view keeps the basis
        var offline = await svc.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1, Adjust: AdjustMode.Total));
        Assert.True(offline.OfflineCache);
        Assert.Equal(PriceAdjustment.SplitDividend, offline.Adjustment);
    }

    [Fact]
    public async Task A_basis_change_forces_a_full_reload_instead_of_splicing()
    {
        var (svc, inner) = Make();
        await svc.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1));
        inner.Basis = PriceAdjustment.SplitDividend;                      // same source, different basis
        var s = await svc.GetBarsAsync(new BarRequest("AAPL", BarInterval.Day1));
        Assert.Equal((2, 1), (svc.FullFetches, svc.TailFetches));
        Assert.Equal(PriceAdjustment.SplitDividend, s.Adjustment);
    }

    [Fact]
    public void A_version_1_database_is_upgraded_in_place()
    {
        var path = Path.Combine(_dir, "v1.db");
        Directory.CreateDirectory(_dir);
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE schema_version (v INTEGER NOT NULL); INSERT INTO schema_version VALUES (1);
                CREATE TABLE bars (symbol TEXT NOT NULL, interval TEXT NOT NULL, adjust TEXT NOT NULL DEFAULT '',
                    t INTEGER NOT NULL, o REAL, h REAL, l REAL, c REAL NOT NULL, v REAL NOT NULL, incomplete INTEGER NOT NULL,
                    PRIMARY KEY (symbol, interval, adjust, t)) WITHOUT ROWID;
                CREATE TABLE bar_meta (symbol TEXT NOT NULL, interval TEXT NOT NULL, adjust TEXT NOT NULL DEFAULT '',
                    source TEXT NOT NULL, fetched_at INTEGER NOT NULL, as_of INTEGER, delay_minutes INTEGER, realtime INTEGER NOT NULL, covered_from INTEGER,
                    PRIMARY KEY (symbol, interval, adjust));
                INSERT INTO bar_meta VALUES ('AAPL','Day1','','yahoo',1,NULL,NULL,0,NULL);
                INSERT INTO bars VALUES ('AAPL','Day1','',1,1,1,1,1,1,0);
                """;
            cmd.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var db = new MarketDb(path);
        Assert.Equal(MarketDb.SchemaVersion, db.ReadSchemaVersion());
        var (bars, meta) = db.LoadBars("AAPL", BarInterval.Day1);
        Assert.Single(bars);
        Assert.Equal(PriceAdjustment.Unknown, meta!.Basis);               // refreshed by the next fetch
    }
}

/// <summary>B6: open interest per contract and day, compared with the previous stored day.</summary>
public class OpenInterestHistoryTests
{
    [Fact]
    public void Previous_day_is_the_latest_stored_day_before()
    {
        var dir = Directory.CreateTempSubdirectory("oi-").FullName;
        try
        {
            var db = new MarketDb(Path.Combine(dir, "m.db"));
            var d1 = new DateOnly(2026, 11, 9);
            var d2 = new DateOnly(2026, 11, 11);
            db.SaveOpenInterest("AAPL", d1, [("C1", 100), ("C2", 50)]);
            db.SaveOpenInterest("AAPL", d2, [("C1", 130)]);
            db.SaveOpenInterest("AAPL", d2, [("C1", 140)]);                    // same day: replaced
            db.SaveOpenInterest("MSFT", d1, [("C1", 999)]);
            Assert.Empty(db.LoadPreviousOpenInterest("AAPL", d1));
            Assert.Equal(new Dictionary<string, double> { ["C1"] = 100, ["C2"] = 50 }, db.LoadPreviousOpenInterest("AAPL", d2));
            Assert.Equal(new Dictionary<string, double> { ["C1"] = 140 }, db.LoadPreviousOpenInterest("AAPL", d2.AddDays(1)));
            Assert.Equal(3, db.PruneOpenInterest(d2));                             // every symbol's rows before d2
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, true);
        }
    }
}
