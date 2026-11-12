using TyuStock.Core.Settings;

namespace TyuStock.Core.Tests;

public class WatchlistStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tyu-wl-" + Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _settings;
    private readonly WatchlistStore _wl;

    public WatchlistStoreTests()
    {
        _settings = new SettingsStore(_dir);
        _settings.Load();
        _wl = new WatchlistStore(_settings);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Defaults_SeedSixSymbolsInDefaultGroup()
    {
        Assert.Equal(["SPY", "QQQ", "AAPL", "MSFT", "NVDA", "TSLA"], _wl.AllSymbols);
        Assert.Equal(WatchlistGroup.DefaultId, _wl.Groups.Single().Id);
        Assert.True(string.IsNullOrEmpty(_wl.Groups[0].Name));
    }

    [Theory]
    [InlineData("aapl", "AAPL")]
    [InlineData(" BRK.B ", "BRK.B")]
    [InlineData("brk-b", "BRK-B")]
    [InlineData("^gspc", "^GSPC")]
    public void Normalize_AcceptsCommonTickers(string raw, string expected)
    {
        Assert.True(WatchlistStore.TryNormalizeSymbol(raw, out var symbol, out var err));
        Assert.Equal(WatchlistError.None, err);
        Assert.Equal(expected, symbol);
    }

    [Theory]
    [InlineData("")]
    [InlineData("this-is-way-too-long-ticker")]
    [InlineData("AA PL")]
    [InlineData("AAPL/USD")]
    [InlineData("中文")]
    public void Normalize_RejectsBadTickers(string raw)
    {
        Assert.False(WatchlistStore.TryNormalizeSymbol(raw, out var symbol, out var err));
        Assert.Equal(WatchlistError.InvalidSymbol, err);
        Assert.Equal("", symbol);
    }

    [Fact]
    public void AddRemove_PersistsAndIsCaseInsensitive()
    {
        Assert.Equal(WatchlistError.None, _wl.AddSymbol(WatchlistGroup.DefaultId, "amd", out var symbol));
        Assert.Equal("AMD", symbol);
        Assert.Contains("AMD", _wl.AllSymbols);
        Assert.Contains("AMD", new WatchlistStore(Reload()).AllSymbols);
        Assert.True(_wl.RemoveSymbol("Amd"));
        Assert.DoesNotContain("AMD", _wl.AllSymbols);
        Assert.DoesNotContain("AMD", new WatchlistStore(Reload()).AllSymbols);
    }

    [Fact]
    public void Add_DuplicateAndCrossGroup_AreRejected()
    {
        Assert.Equal(WatchlistError.DuplicateInGroup, _wl.AddSymbol(WatchlistGroup.DefaultId, "AAPL", out _));
        Assert.Equal(WatchlistError.None, _wl.AddGroup("Tech", out var tech));
        Assert.Equal(WatchlistError.AlreadyOnWatchlist, _wl.AddSymbol(tech!.Id, "AAPL", out _));
        Assert.Equal(WatchlistError.GroupNotFound, _wl.AddSymbol("missing", "IBM", out _));
    }

    [Fact]
    public void MoveAndReorder_ChangeOrderAndPersist()
    {
        Assert.True(_wl.MoveSymbol("SPY", 1));
        Assert.Equal("QQQ", _wl.Groups[0].Symbols[0]);
        Assert.Equal("SPY", _wl.Groups[0].Symbols[1]);
        Assert.False(_wl.MoveSymbol("QQQ", -1));
        Assert.True(_wl.Reorder(WatchlistGroup.DefaultId, 1, 0));
        Assert.Equal("SPY", _wl.Groups[0].Symbols[0]);
        Assert.Equal("SPY", new WatchlistStore(Reload()).Groups[0].Symbols[0]);
    }

    [Fact]
    public void Groups_AddRenameRemove_MergesSymbols()
    {
        Assert.Equal(WatchlistError.EmptyName, _wl.AddGroup("  ", out _));
        Assert.Equal(WatchlistError.None, _wl.AddGroup("Energy", out var energy));
        Assert.NotNull(energy);
        Assert.Equal(WatchlistError.None, _wl.AddSymbol(energy.Id, "XOM", out _));
        Assert.Equal(WatchlistError.None, _wl.RenameGroup(energy.Id, "Oil"));
        Assert.Equal("Oil", _wl.FindGroup(energy.Id)!.Name);
        Assert.Equal(WatchlistError.None, _wl.RemoveGroup(energy.Id));
        Assert.Contains("XOM", _wl.FindGroup(WatchlistGroup.DefaultId)!.Symbols);
        Assert.Single(_wl.Groups);
        Assert.Equal(WatchlistError.LastGroup, _wl.RemoveGroup(WatchlistGroup.DefaultId));
        Assert.Contains("XOM", new WatchlistStore(Reload()).AllSymbols);
    }

    [Fact]
    public void Changed_FiresOnSuccessfulMutation()
    {
        var n = 0;
        _wl.Changed += (_, _) => n++;
        _wl.AddSymbol(WatchlistGroup.DefaultId, "IBM", out _);
        _wl.MoveSymbol("IBM", -1);
        _wl.RemoveSymbol("IBM");
        Assert.Equal(3, n);
    }

    private SettingsStore Reload()
    {
        var s = new SettingsStore(_dir);
        s.Load();
        return s;
    }
}