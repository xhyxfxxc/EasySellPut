using TyuStock.Core.Time;

namespace TyuStock.Core.Tests;

public class MarketClockTests
{
    private static DateTimeOffset Et(int y, int mo, int d, int h, int mi) => MarketClock.FromEastern(new DateTime(y, mo, d, h, mi, 0));

    [Theory]
    [InlineData(2026, 11, 10, 6, 53, MarketSession.PreMarket)]
    [InlineData(2026, 11, 10, 9, 30, MarketSession.Regular)]
    [InlineData(2026, 11, 10, 15, 59, MarketSession.Regular)]
    [InlineData(2026, 11, 10, 16, 0, MarketSession.AfterHours)]
    [InlineData(2026, 11, 10, 20, 0, MarketSession.Closed)]
    [InlineData(2026, 11, 10, 3, 59, MarketSession.Closed)]
    [InlineData(2026, 11, 14, 11, 0, MarketSession.Closed)] // Saturday
    [InlineData(2026, 11, 26, 11, 0, MarketSession.Closed)] // Thanksgiving
    public void Sessions(int y, int mo, int d, int h, int mi, MarketSession expected) =>
        Assert.Equal(expected, MarketClock.GetSession(Et(y, mo, d, h, mi)));

    [Fact]
    public void Holidays2026_MatchNyseCalendar()
    {
        var h = MarketClock.Holidays(2026);
        DateOnly[] expected =
        [
            new(2026, 1, 1), new(2026, 1, 19), new(2026, 2, 16), new(2026, 4, 3), new(2026, 5, 25),
            new(2026, 6, 19), new(2026, 7, 3), new(2026, 9, 7), new(2026, 11, 26), new(2026, 12, 25),
        ];
        Assert.Equal(expected.OrderBy(x => x), h.OrderBy(x => x));
    }

    [Fact]
    public void Eastern_HandlesDst()
    {
        // 2026-11-10 is EST (UTC-5); 2026-07-01 is EDT (UTC-4).
        Assert.Equal(TimeSpan.FromHours(-5), Et(2026, 11, 10, 9, 30).Offset);
        Assert.Equal(TimeSpan.FromHours(-4), Et(2026, 7, 1, 9, 30).Offset);
    }

    [Fact]
    public void BeijingEvening_IsUsPreMarket()
    {
        // 19:53 Beijing (UTC+8) on 2026-11-10 == 06:53 ET
        var bj = new DateTimeOffset(2026, 11, 10, 19, 53, 0, TimeSpan.FromHours(8));
        Assert.Equal(MarketSession.PreMarket, MarketClock.GetSession(bj));
    }
    [Theory]
    [InlineData(2026, 11, 10, 10, 0, 5)]
    [InlineData(2026, 11, 10, 6, 53, 10)]
    [InlineData(2026, 11, 10, 16, 30, 10)]
    [InlineData(2026, 11, 14, 11, 0, 60)]
    public void QuoteRefreshInterval_MatchesSession(int y, int mo, int d, int h, int mi, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), MarketClock.QuoteRefreshInterval(Et(y, mo, d, h, mi)));
}