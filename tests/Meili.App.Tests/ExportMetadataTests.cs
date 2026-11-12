using Meili.App.Services;
using Meili.Core.Models;
using Meili.Core.Pricing;

namespace Meili.App.Tests;

/// <summary>F9: exports carry source, data time, export time, timeliness and pricing assumptions.</summary>
public class ExportMetadataTests
{
    [Fact]
    public void Chain_export_metadata_states_source_delay_times_and_rate()
    {
        WpfHost.Run(() =>
        {
            var loc = AppServices.Get<LocalizationService>();
            var now = new DateTimeOffset(2026, 11, 12, 13, 0, 0, TimeSpan.Zero);
            var chain = new OptionChain
            {
                Underlying = "AAPL", Source = "cboe", DelayMinutes = 15, AsOf = now.AddMinutes(-15), FetchedAt = now.AddSeconds(-3),
                Expirations = [], Contracts = [],
            };
            var rate = AppServices.Get<RiskFreeRateProvider>().Current;
            var meta = ExportUi.Metadata(loc, chain, rate, "note", now).ToDictionary(k => k.Key, k => k.Value);

            Assert.Equal(SourceNamesFor(loc, "cboe"), meta[loc["Ex_MetaSource"]]);
            Assert.Equal(string.Format(loc["Quote_DelayedN"], 15), meta[loc["Ex_MetaTiming"]]);
            Assert.Equal("2026-11-12 07:45:00", meta[loc["Ex_MetaAsOf"]]);           // ET
            Assert.True(meta.ContainsKey(loc["Ex_MetaExported"]));
            Assert.True(meta.ContainsKey(loc["Ex_MetaFetched"]));
            Assert.Equal(PricingText.Model(rate, loc), meta[loc["Ex_MetaRate"]]);
            Assert.Equal("note", meta[loc["Ex_MetaNote"]]);

            var bare = ExportUi.Metadata(loc, null, now: now);
            Assert.Single(bare);                                                        // export time only
        });
    }

    private static string SourceNamesFor(LocalizationService loc, string id) => Meili.Shared.ViewModels.SourceNames.Display(id, loc);
}
