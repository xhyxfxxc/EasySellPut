namespace TyuStock.Analytics;

/// <summary>Time-to-expiry helpers for option pricing (calendar-day, ACT/365 by default).</summary>
public static class OptionTime
{
    /// <summary>Standard equity/ETF option expiry: 16:00 New York time on the expiration date.</summary>
    public static readonly TimeOnly MarketClose = new(16, 0);

    /// <summary>AM-settled index options (e.g. standard monthly SPX) settle on the opening print: 09:30 New York time.</summary>
    public static readonly TimeOnly MarketOpen = new(9, 30);

    public const double DaysPerYear = 365.0;

    /// <summary>Expiry instant: <paramref name="expiry"/> at <paramref name="settlementTime"/> (default 16:00) US/Eastern.</summary>
    public static DateTimeOffset ExpiryInstant(DateOnly expiry, TimeOnly? settlementTime = null)
        => UsEastern.ToInstant(expiry, settlementTime ?? MarketClose);

    /// <summary>
    /// Years from <paramref name="now"/> until the expiry instant (fractional calendar days / 365), never negative.
    /// Returns 0 once the option has expired.
    /// </summary>
    public static double YearsToExpiry(DateOnly expiry, DateTimeOffset now, TimeOnly? settlementTime = null, double daysPerYear = DaysPerYear)
        => YearsBetween(now, ExpiryInstant(expiry, settlementTime), daysPerYear);

    /// <summary>Non-negative years between two instants.</summary>
    public static double YearsBetween(DateTimeOffset from, DateTimeOffset to, double daysPerYear = DaysPerYear)
    {
        if (!(daysPerYear > 0)) throw new ArgumentOutOfRangeException(nameof(daysPerYear));
        double days = (to - from).TotalDays;
        return days > 0 ? days / daysPerYear : 0.0;
    }

    /// <summary>Whole calendar days to expiry ("DTE") as shown in option chains, by New York date; 0 on expiration day, negative after.</summary>
    public static int DaysToExpiry(DateOnly expiry, DateTimeOffset now)
        => expiry.DayNumber - DateOnly.FromDateTime(UsEastern.ToEastern(now).DateTime).DayNumber;
}