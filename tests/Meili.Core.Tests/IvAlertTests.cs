using Meili.Core.Alerts;
using Meili.Core.Settings;

namespace Meili.Core.Tests;

/// <summary>B1: IV alerts — crossing, first-sample baseline, basis changes, staleness and store routing.</summary>
public class IvAlertTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 16, 0, 0, TimeSpan.Zero);

    private static IvSample S(double iv, IvBasis basis = IvBasis.Iv30, DateTimeOffset? asOf = null) =>
        new("AAPL", iv, basis, asOf ?? Now.AddMinutes(-16), "cboe");

    private static PriceAlert Step(PriceAlert a, IvSample s, out AlertEvalResult r)
    {
        r = AlertEvaluator.EvaluateIv(a, s, Now);
        return r.Next;
    }

    [Fact]
    public void Iv_above_fires_only_on_an_upward_cross_after_the_first_sample()
    {
        var a = new PriceAlert { Symbol = "AAPL", Kind = AlertKind.IvAbove, Threshold = 40 };
        a = Step(a, S(0.45), out var r);                      // already above: first sample only records
        Assert.Equal(AlertEvalStatus.WaitingForSample, r.Status);
        Assert.Equal(45, a.LastSeenIv!.Value, 6);
        a = Step(a, S(0.38), out r);
        Assert.False(r.Fired);
        a = Step(a, S(0.401), out r);
        Assert.True(r.Fired);
        Assert.Equal(40.1, r.Value!.Value, 6);
        Assert.False(a.Enabled);                              // Once
    }

    [Fact]
    public void Iv_below_fires_on_a_downward_cross()
    {
        var a = new PriceAlert { Symbol = "AAPL", Kind = AlertKind.IvBelow, Threshold = 25 };
        a = Step(a, S(0.30), out _);
        a = Step(a, S(0.26), out var r);
        Assert.False(r.Fired);
        Step(a, S(0.249), out r);
        Assert.True(r.Fired);
    }

    [Fact]
    public void Switching_between_iv30_and_atm_rebaselines_instead_of_firing()
    {
        var a = new PriceAlert { Symbol = "AAPL", Kind = AlertKind.IvAbove, Threshold = 40 };
        a = Step(a, S(0.35), out _);
        a = Step(a, S(0.43, IvBasis.AtmNearest30), out var r);  // ATM is a different measure: not a cross
        Assert.False(r.Fired);
        Assert.Equal(AlertEvalStatus.WaitingForSample, r.Status);
        Assert.Equal(IvBasis.AtmNearest30, a.LastSeenIvBasis);
        a = Step(a, S(0.39, IvBasis.AtmNearest30), out r);
        Assert.False(r.Fired);
        Step(a, S(0.41, IvBasis.AtmNearest30), out r);
        Assert.True(r.Fired);
    }

    [Fact]
    public void Invalid_samples_and_quote_evaluation_never_fire_iv_alerts()
    {
        var a = new PriceAlert { Symbol = "AAPL", Kind = AlertKind.IvAbove, Threshold = 40, HasSeenQuote = true, LastSeenIv = 30, LastSeenIvBasis = IvBasis.Iv30 };
        Assert.Equal(AlertEvalStatus.InvalidQuote, AlertEvaluator.EvaluateIv(a, null, Now).Status);
        Assert.Equal(AlertEvalStatus.InvalidQuote, AlertEvaluator.EvaluateIv(a, S(double.NaN), Now).Status);
        Assert.Equal(AlertEvalStatus.InvalidQuote, AlertEvaluator.EvaluateIv(a, S(0), Now).Status);
        var q = new Meili.Core.Models.Quote { Symbol = "AAPL", Price = 999, Volume = 1e9, ChangePercent = 99 };
        Assert.Equal(AlertEvalStatus.InvalidQuote, AlertEvaluator.Evaluate(a, q, Now).Status);
        var price = new PriceAlert { Symbol = "AAPL", Kind = AlertKind.PriceAbove, Threshold = 1 };
        Assert.Equal(AlertEvalStatus.InvalidQuote, AlertEvaluator.EvaluateIv(price, S(0.5), Now).Status);
    }

    [Fact]
    public void Stale_samples_are_gated_by_the_alert_delay_limit()
    {
        Assert.Equal(AlertQuoteUse.Usable, IvSampleGate.Check(S(0.3, asOf: Now.AddMinutes(-16)), Now, 20));
        Assert.Equal(AlertQuoteUse.TooStale, IvSampleGate.Check(S(0.3, asOf: Now.AddMinutes(-90)), Now, 20));
        Assert.Equal(AlertQuoteUse.UsableDelayUnknown, IvSampleGate.Check(new IvSample("AAPL", 0.3, IvBasis.Iv30, null, "x"), Now, 20));
    }

    [Fact]
    public void Store_routes_iv_symbols_away_from_the_quote_stream()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ivstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AlertStore(dir);
            store.Load();
            store.Upsert(new PriceAlert { Symbol = "AAPL", Kind = AlertKind.IvAbove, Threshold = 40 });
            store.Upsert(new PriceAlert { Symbol = "MSFT", Kind = AlertKind.PriceAbove, Threshold = 400 });
            store.Upsert(new PriceAlert { Symbol = "TSLA", Kind = AlertKind.IvBelow, Threshold = 50, Enabled = false });
            Assert.Equal(["MSFT"], store.EnabledSymbols);
            Assert.Equal(["AAPL"], store.IvSymbols);

            var reloaded = new AlertStore(dir);
            reloaded.Load();
            Assert.Contains(reloaded.Alerts, a => a.Kind == AlertKind.IvAbove && a.Threshold == 40);
        }
        finally { Directory.Delete(dir, true); }
    }
}
