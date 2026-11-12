using TyuStock.Analytics;
using TyuStock.Core.Abstractions;
using TyuStock.Core.Alerts;
using TyuStock.Core.Diagnostics;
using TyuStock.Core.Models;
using TyuStock.Core.Pricing;
using CRight = TyuStock.Core.Models.OptionRight;

namespace TyuStock.App.Services;

/// <summary>B1: where IV alerts get their readings.</summary>
public interface IIvSource
{
    Task<IvSample?> GetAsync(string symbol, DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>
/// CBOE's IV30 index first (it comes with CBOE's delayed quote); otherwise ATM IV of the expiration nearest 30 days,
/// from whichever options source the router reaches, solved locally with the F5 risk-free rate when the source has no IV.
/// </summary>
public sealed class IvSource(
    Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<Quote>>> iv30Quotes,
    IMarketDataService data,
    RiskFreeRateProvider rates) : IIvSource
{
    public async Task<IvSample?> GetAsync(string symbol, DateTimeOffset now, CancellationToken ct = default)
    {
        try
        {
            var q = (await iv30Quotes([symbol], ct)).FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            if (IvFilter.Clean(q?.ImpliedVolatility30) is { } iv30)
                return new IvSample(symbol, iv30, IvBasis.Iv30, q!.AsOf, q.Source);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { AppLog.Warn("iv", $"IV30 for {symbol} failed: {ex.Message}"); }
        return await AtmAsync(symbol, now, ct);
    }

    private async Task<IvSample?> AtmAsync(string symbol, DateTimeOffset now, CancellationToken ct)
    {
        var exps = await data.GetExpirationsAsync(symbol, ct);
        if (AtmVolatility.PickExpiration(exps, now) is not { } exp) return null;
        var chain = await data.GetChainAsync(symbol, exp, ct);
        var spot = chain.UnderlyingPrice is > 0 ? chain.UnderlyingPrice.Value
            : (await data.GetQuotesAsync([symbol], ct)).FirstOrDefault()?.Price ?? 0;
        var rate = await rates.GetAsync(ct);
        var candidates = chain.Contracts.Where(c => c.Expiration == exp)
            .Select(c => new AtmCandidate(c.Right == CRight.Call ? TyuStock.Analytics.OptionRight.Call : TyuStock.Analytics.OptionRight.Put, c.Strike,
                c.ImpliedVolatility, c.Bid, c.Ask, c.Last))
            .ToList();
        if (AtmVolatility.Estimate(candidates, spot, exp, now, rate.Rate) is not { } est) return null;
        return new IvSample(symbol, est.Iv, IvBasis.AtmNearest30, chain.AsOf, chain.Source, exp, TyuStock.Analytics.OptionTime.DaysToExpiry(exp, now));
    }
}
