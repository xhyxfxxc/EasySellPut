namespace TyuStock.Analytics;

/// <summary>US/Eastern (New York) time-zone helpers, DST-aware.</summary>
public static class UsEastern
{
    private static readonly Lazy<TimeZoneInfo> Zone = new(Resolve);

    /// <summary>America/New_York ("Eastern Standard Time" on Windows). Falls back to a built-in rule set (post-2007 US DST) if the OS has no such zone.</summary>
    public static TimeZoneInfo TimeZone => Zone.Value;

    /// <summary>Converts an instant to New York local time.</summary>
    public static DateTimeOffset ToEastern(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, TimeZone);

    /// <summary>The instant at which the New York wall clock shows <paramref name="date"/> <paramref name="time"/>.</summary>
    public static DateTimeOffset ToInstant(DateOnly date, TimeOnly time) => ToInstant(date, time, TimeZone);

    /// <summary>
    /// The instant at which <paramref name="tz"/>'s wall clock shows the given date and time. Wall times skipped by a
    /// spring-forward transition are moved forward by the gap; ambiguous (fall-back) times resolve to the earlier instant.
    /// </summary>
    public static DateTimeOffset ToInstant(DateOnly date, TimeOnly time, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local)) local = local.AddHours(1);
        TimeSpan offset = tz.IsAmbiguousTime(local) ? tz.GetAmbiguousTimeOffsets(local).Max() : tz.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2007, 1, 1), DateTime.MaxValue.Date, TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday));
        return TimeZoneInfo.CreateCustomTimeZone("US-Eastern-Builtin", TimeSpan.FromHours(-5), "US Eastern", "EST", "EDT", new[] { rule });
    }
}