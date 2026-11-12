namespace Meili.Core.Time;

public enum MarketSession { Closed, PreMarket, Regular, AfterHours }

/// <summary>US equity market calendar in America/New_York (NYSE/Nasdaq full-day holidays; early closes not modelled).</summary>
public static class MarketClock
{
    public static readonly TimeZoneInfo Eastern = FindEastern();
    private static readonly TimeSpan PreOpen = new(4, 0, 0), Open = new(9, 30, 0), Close = new(16, 0, 0), PostClose = new(20, 0, 0);

    private static TimeZoneInfo FindEastern()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        throw new TimeZoneNotFoundException("US Eastern time zone not found");
    }

    public static DateTime ToEastern(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, Eastern).DateTime;

    /// <summary>Interpret an Eastern wall-clock time as an absolute instant.</summary>
    public static DateTimeOffset FromEastern(DateTime easternWallClock)
    {
        var unspecified = DateTime.SpecifyKind(easternWallClock, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, Eastern.GetUtcOffset(unspecified));
    }

    public static MarketSession GetSession(DateTimeOffset now)
    {
        var et = ToEastern(now);
        if (!IsTradingDay(DateOnly.FromDateTime(et))) return MarketSession.Closed;
        var t = et.TimeOfDay;
        if (t >= PreOpen && t < Open) return MarketSession.PreMarket;
        if (t >= Open && t < Close) return MarketSession.Regular;
        if (t >= Close && t < PostClose) return MarketSession.AfterHours;
        return MarketSession.Closed;
    }

    /// <summary>Adaptive quote poll interval: 5 s regular, 10 s extended hours, 60 s when closed.</summary>
    public static TimeSpan QuoteRefreshInterval(DateTimeOffset now) => GetSession(now) switch
    {
        MarketSession.Regular => TimeSpan.FromSeconds(5),
        MarketSession.PreMarket or MarketSession.AfterHours => TimeSpan.FromSeconds(10),
        _ => TimeSpan.FromSeconds(60),
    };

    public static bool IsTradingDay(DateOnly d) =>
        d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !Holidays(d.Year).Contains(d);

    public static HashSet<DateOnly> Holidays(int year)
    {
        var h = new HashSet<DateOnly>();
        // New Year's Day: if on Saturday, NYSE does not observe on the prior Friday.
        var ny = new DateOnly(year, 1, 1);
        if (ny.DayOfWeek == DayOfWeek.Sunday) h.Add(ny.AddDays(1)); else if (ny.DayOfWeek != DayOfWeek.Saturday) h.Add(ny);
        h.Add(NthWeekday(year, 1, DayOfWeek.Monday, 3));   // MLK
        h.Add(NthWeekday(year, 2, DayOfWeek.Monday, 3));   // Presidents
        h.Add(EasterSunday(year).AddDays(-2));             // Good Friday
        h.Add(LastWeekday(year, 5, DayOfWeek.Monday));     // Memorial
        if (year >= 2022) h.Add(Observed(new DateOnly(year, 6, 19))); // Juneteenth
        h.Add(Observed(new DateOnly(year, 7, 4)));
        h.Add(NthWeekday(year, 9, DayOfWeek.Monday, 1));   // Labor
        h.Add(NthWeekday(year, 11, DayOfWeek.Thursday, 4)); // Thanksgiving
        h.Add(Observed(new DateOnly(year, 12, 25)));
        return h;
    }

    private static DateOnly Observed(DateOnly d) => d.DayOfWeek switch
    {
        DayOfWeek.Saturday => d.AddDays(-1),
        DayOfWeek.Sunday => d.AddDays(1),
        _ => d,
    };

    private static DateOnly NthWeekday(int y, int m, DayOfWeek dow, int n)
    {
        var d = new DateOnly(y, m, 1);
        while (d.DayOfWeek != dow) d = d.AddDays(1);
        return d.AddDays(7 * (n - 1));
    }

    private static DateOnly LastWeekday(int y, int m, DayOfWeek dow)
    {
        var d = new DateOnly(y, m, DateTime.DaysInMonth(y, m));
        while (d.DayOfWeek != dow) d = d.AddDays(-1);
        return d;
    }

    private static DateOnly EasterSunday(int y)
    {
        int a = y % 19, b = y / 100, c = y % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3,
            hh = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - hh - k) % 7,
            m = (a + 11 * hh + 22 * l) / 451, month = (hh + l - 7 * m + 114) / 31, day = (hh + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(y, month, day);
    }
}
