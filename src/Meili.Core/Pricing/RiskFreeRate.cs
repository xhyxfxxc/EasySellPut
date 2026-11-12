using Meili.Core.Abstractions;
using Meili.Core.Diagnostics;
using Meili.Core.Settings;

namespace Meili.Core.Pricing;

public enum RateOrigin
{
    /// <summary>13-week US T-bill yield (^IRX), fetched live.</summary>
    Treasury13Week,
    /// <summary>The user's fixed rate (chosen in settings).</summary>
    Fixed,
    /// <summary>^IRX was wanted but could not be fetched; the fixed rate is used instead.</summary>
    FixedFallback,
}

/// <summary>A risk-free rate as a decimal fraction (0.0412 = 4.12 %) and where it came from.</summary>
public sealed record RiskFreeRate(double Rate, RateOrigin Origin, DateTimeOffset? AsOf)
{
    public double Percent => Rate * 100;
}

/// <summary>
/// F5: one place that decides the risk-free rate for options and strategy pricing. In the default mode it reads the
/// 13-week T-bill yield (^IRX, quoted in percent) once and keeps it for <see cref="Ttl"/>; if that fails it falls back to
/// the configured fixed rate and says so, so the UI can label "r = x % (fixed fallback)".
/// </summary>
public sealed class RiskFreeRateProvider
{
    public const string TreasurySymbol = "^IRX";
    private readonly IMarketDataService _data;
    private readonly Func<PricingSettings> _settings;
    private RiskFreeRate? _cached;
    private DateTimeOffset _cachedAt;
    private DateTimeOffset _failedAt = DateTimeOffset.MinValue;

    public RiskFreeRateProvider(IMarketDataService data, Func<PricingSettings> settings)
    {
        _data = data;
        _settings = settings;
    }

    public TimeSpan Ttl { get; init; } = TimeSpan.FromHours(6);
    /// <summary>After a failed fetch, do not try again for this long (avoid a request per chain refresh when offline).</summary>
    public TimeSpan RetryAfterFailure { get; init; } = TimeSpan.FromMinutes(15);
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>The rate to use right now without waiting (last known, or the fixed rate).</summary>
    public RiskFreeRate Current
    {
        get
        {
            var s = _settings();
            if (s.RateMode == RiskFreeRateMode.Fixed) return Fixed(s, RateOrigin.Fixed);
            return _cached ?? Fixed(s, RateOrigin.FixedFallback);
        }
    }

    public async Task<RiskFreeRate> GetAsync(CancellationToken ct = default)
    {
        var s = _settings();
        if (s.RateMode == RiskFreeRateMode.Fixed) return Fixed(s, RateOrigin.Fixed);
        var now = Now();
        if (_cached is { } c && now - _cachedAt < Ttl) return c;
        if (now - _failedAt < RetryAfterFailure) return _cached ?? Fixed(s, RateOrigin.FixedFallback);
        try
        {
            var quotes = await _data.GetQuotesAsync([TreasurySymbol], ct);
            var q = quotes.FirstOrDefault(x => string.Equals(x.Symbol, TreasurySymbol, StringComparison.OrdinalIgnoreCase));
            // ^IRX is quoted in percent (e.g. 4.12). Reject nonsense instead of pricing with it.
            if (q is { Price: > -1 and < 25 } && double.IsFinite(q.Price))
            {
                _cached = new RiskFreeRate(q.Price / 100.0, RateOrigin.Treasury13Week, q.AsOf ?? now);
                _cachedAt = now;
                return _cached;
            }
            AppLog.Warn("pricing", $"{TreasurySymbol} quote unusable: {q?.Price}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { AppLog.Warn("pricing", $"{TreasurySymbol} fetch failed: {ex.Message}"); }
        _failedAt = now;
        return _cached ?? Fixed(s, RateOrigin.FixedFallback);
    }

    private static RiskFreeRate Fixed(PricingSettings s, RateOrigin origin) => new(s.FixedRatePercent / 100.0, origin, null);
}
