namespace Meili.Analytics.Tests;

public class WhatIfTests
{
    private static readonly DateOnly Exp = new(2026, 12, 18);
    private static readonly StrategyValuation Live = StrategyValuation.AtDate(new DateOnly(2026, 11, 13), 0.04);

    private static OptionStrategy Covered()
        => StrategyPresets.CoveredCall(62.04, new LegQuote(63, 0.42, 0.25), Exp);

    [Fact]
    public void Expiry_covered_call_matches_leg_intrinsics()
    {
        var s = Covered();
        var snap = WhatIf.Capture(s, 65, valuation: null);
        // Stock: 100 * (65 - 62.04) = 296; short 63 call intrinsic 2, P/L = -100*(2-0.42) = -158; combo 138.
        Assert.Equal(65, snap.Underlying);
        Assert.Equal(296, snap.Legs[0].Pnl, 6);
        Assert.Equal(2, snap.Legs[1].TheoreticalPrice, 6);
        Assert.Equal(-158, snap.Legs[1].Pnl, 6);
        Assert.Equal(138, snap.CombinationPnl, 6);
        Assert.Equal(s.PnlAtExpiry(65), snap.CombinationPnl, 9);
        Assert.Equal(s.PnlAtExpiry(65) + s.NetCost, snap.CombinationValue, 9);
    }

    [Fact]
    public void Expiry_otm_short_call_keeps_the_credit()
    {
        var s = Covered();
        var snap = WhatIf.Capture(s, 50, null);
        Assert.Equal(0, snap.Legs[1].TheoreticalPrice, 9);
        Assert.Equal(42, snap.Legs[1].Pnl, 6); // keep the 0.42 credit * 100
        Assert.Equal(s.PnlAtExpiry(50), snap.CombinationPnl, 9);
    }

    [Fact]
    public void Pre_expiry_uses_bsm_and_matches_pnl_at()
    {
        var s = Covered();
        var val = Live with { DefaultVolatility = 0.25 };
        var snap = WhatIf.Capture(s, 64, val);
        Assert.Equal(s.ValueAt(64, val), snap.CombinationValue, 9);
        Assert.Equal(s.PnlAt(64, val), snap.CombinationPnl, 9);
        Assert.False(snap.Legs[1].Facts.AtExpiry);
        Assert.True(snap.Legs[1].TheoreticalPrice > 1); // still has time value above the $1 intrinsic
    }

    [Fact]
    public void Explainer_covers_covered_call_and_stock_cases()
    {
        var s = Covered();
        var itm = WhatIf.Capture(s, 65, null);
        var otm = WhatIf.Capture(s, 50, null);
        Assert.Equal("St_Why_StockGain", WhatIfExplainer.Describe(itm.Legs[0]).Key);
        Assert.Equal("St_Why_ShortCallItmExpiry", WhatIfExplainer.Describe(itm.Legs[1]).Key);
        Assert.Equal("St_Why_StockLoss", WhatIfExplainer.Describe(otm.Legs[0]).Key);
        Assert.Equal("St_Why_ShortOtmExpiry", WhatIfExplainer.Describe(otm.Legs[1]).Key);

        var live = WhatIf.Capture(s, 65, Live with { DefaultVolatility = 0.25 });
        Assert.Equal("St_Why_ShortItmLive", WhatIfExplainer.Describe(live.Legs[1]).Key);

        foreach (var leg in itm.Legs.Concat(otm.Legs).Concat(live.Legs))
        {
            string text = WhatIfExplainer.Format(leg, Loc);
            Assert.False(string.IsNullOrWhiteSpace(text), WhatIfExplainer.Describe(leg).Key);
            Assert.DoesNotContain("St_", text);
        }
    }

    [Fact]
    public void Calendar_pnl_at_near_expiry_values_the_far_leg()
    {
        var near = new DateOnly(2026, 12, 18);
        var far = new DateOnly(2027, 1, 15);
        var s = StrategyPresets.CalendarCall(new LegQuote(100, 3, 0.25), near, new LegQuote(100, 5, 0.25), far);
        Assert.True(s.HasMixedExpiries);
        var atNear = StrategyValuation.AtDate(near, 0.04) with { DefaultVolatility = 0.25 };
        double pnl = s.PnlAt(100, atNear);
        // Near call expires ATM (worth 0); far call still has time value; net credit was -200 (short 3, long 5) * 100? Cost = -3*100 + 5*100 = 200 debit.
        Assert.Equal(0, OptionStrategy.LegGreeks(s.Legs[0], 100, atNear).Price, 9);
        double farPx = OptionStrategy.LegGreeks(s.Legs[1], 100, atNear).Price;
        Assert.True(farPx > 0);
        Assert.Equal(farPx * 100 - 200, pnl, 6);
        var snap = WhatIf.Capture(s, 100, atNear);
        Assert.Equal(pnl, snap.CombinationPnl, 9);
        Assert.True(snap.Legs[0].Facts.AtExpiry);
        Assert.False(snap.Legs[1].Facts.AtExpiry);
    }

    private static string Loc(string key) => key switch
    {
        "St_Call" => "认购",
        "St_Put" => "认沽",
        "St_Buy" => "买入",
        "St_Sell" => "卖出",
        "St_Stock" => "正股",
        "St_Why_StockGain" => "股价到 {0}，相对成本 {1} 账面盈利 {2}",
        "St_Why_StockLoss" => "股价到 {0}，相对成本 {1} 账面亏损 {2}",
        "St_Why_ShortCallItmExpiry" => "卖出的 {0} 认购已实值 {1} 美元，买方会行权，上行利润被锁住",
        "St_Why_ShortPutItmExpiry" => "卖出的 {0} 认沽已实值 {1} 美元，买方会行权",
        "St_Why_ShortOtmExpiry" => "仍虚值，到期作废，你保留全部权利金",
        "St_Why_LongItmExpiry" => "买入的 {0} {1} 已实值 {2} 美元，内在价值 {3}",
        "St_Why_LongOtmExpiry" => "仍虚值，到期作废，已付权利金全部损失",
        "St_Why_ShortItmLive" => "卖出的 {0} {1} 已实值 {2} 美元，理论价 {3}",
        "St_Why_ShortOtmLive" => "卖出的 {0} {1} 仍虚值，理论价 {2}",
        "St_Why_LongItmLive" => "买入的 {0} {1} 已实值 {2} 美元，理论价 {3}",
        "St_Why_LongOtmLive" => "买入的 {0} {1} 仍虚值，理论价 {2}",
        _ => key,
    };
}
