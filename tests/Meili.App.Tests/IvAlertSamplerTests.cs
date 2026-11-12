using Meili.App.Services;
using Meili.Core.Alerts;
using Meili.Core.Pricing;
using Meili.Core.Settings;
using Meili.Data.Cache;

namespace Meili.App.Tests;

/// <summary>B1: the IV sampler stores readings, fires IV alerts on a cross, and the alerts page shows the readings.</summary>
public class IvAlertSamplerTests
{
    private sealed class ScriptedIv : IIvSource
    {
        public Queue<IvSample?> Next { get; } = new();
        public Task<IvSample?> GetAsync(string symbol, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult(Next.Count > 0 ? Next.Dequeue() : null);
    }

    [Fact]
    public void Samples_are_stored_and_a_cross_fires_once()
    {
        WpfHost.Run(async () =>
        {
            var store = AppServices.Get<AlertStore>();
            var db = AppServices.Get<MarketDb>();
            var src = new ScriptedIv();
            var sampler = new IvAlertSampler(store, src, AppServices.Get<NotificationService>(), AppServices.Get<SettingsStore>(), db,
                AppServices.Get<AppLaunchOptions>());
            var vm = new AlertsViewModel(store, AppServices.Get<WatchlistStore>(), AppServices.Get<LocalizationService>(),
                AppServices.Get<AlertMonitor>(), AppServices.Get<SettingsStore>(), sampler);
            var alert = store.Upsert(new PriceAlert { Symbol = "IVZZ", Kind = AlertKind.IvAbove, Threshold = 40 });
            var t0 = DateTimeOffset.Now;
            try
            {
                src.Next.Enqueue(new IvSample("IVZZ", 0.35, IvBasis.Iv30, t0.AddMinutes(-15), "cboe"));
                await sampler.SampleAsync(t0);
                Assert.True(store.Find(alert.Id)!.HasSeenQuote);
                Assert.Equal(35, store.Find(alert.Id)!.LastSeenIv!.Value, 6);

                src.Next.Enqueue(null);                                         // source down: recorded as failed
                await sampler.SampleAsync(t0.AddMinutes(5));
                Assert.Contains("IVZZ", sampler.Failed);
                vm.RefreshIvNote();
                Assert.True(vm.HasIvNote);
                Assert.Contains(string.Format(AppServices.Get<LocalizationService>()["Al_IvFailed"], "IVZZ"), vm.IvNote);

                src.Next.Enqueue(new IvSample("IVZZ", 0.42, IvBasis.Iv30, t0.AddMinutes(-5), "cboe"));
                await sampler.SampleAsync(t0.AddMinutes(10));
                var after = store.Find(alert.Id)!;
                Assert.False(after.Enabled);                                    // fired (Once)
                Assert.Contains(store.History, h => h.AlertId == alert.Id && Math.Abs(h.Value - 42) < 1e-9 && h.Source.Contains("IV30"));
                Assert.Equal(42, sampler.Latest["IVZZ"].Percent, 6);
                Assert.DoesNotContain("IVZZ", sampler.Failed);
                vm.RefreshIvNote();
                Assert.False(vm.HasIvNote);                                     // the only IV alert is now disabled

                var stored = db.LoadIvSamples("IVZZ", t0.AddHours(-1));
                Assert.Equal(2, stored.Count);
                Assert.All(stored, s => Assert.Equal("Iv30", s.Kind));
            }
            finally { store.Remove(alert.Id); }
        });
    }

    [Fact]
    public void Atm_fallback_uses_the_chain_when_iv30_is_unavailable()
    {
        WpfHost.Run(async () =>
        {
            var src = new IvSource((_, _) => Task.FromResult<IReadOnlyList<Meili.Core.Models.Quote>>([]),
                WpfHost.Data, AppServices.Get<RiskFreeRateProvider>());
            var s = await src.GetAsync("MSFT", DateTimeOffset.Now);
            Assert.NotNull(s);
            Assert.Equal(IvBasis.AtmNearest30, s!.Basis);
            Assert.Equal(0.25, s.Iv, 6);                                        // fake chain quotes 25% everywhere
            Assert.InRange(s.DaysToExpiry!.Value, 29, 31);
        });
    }

    [Fact]
    public void Iv_kinds_are_offered_and_use_a_percent_hint()
    {
        WpfHost.Run(() =>
        {
            var vm = AppServices.Get<AlertsViewModel>();
            var loc = AppServices.Get<LocalizationService>();
            vm.SelectedKind = vm.KindOptions.First(k => k.Kind == AlertKind.IvBelow);
            Assert.True(vm.IsIvKind);
            Assert.False(vm.IsQuoteKind);
            Assert.Equal(loc["Al_IvThresholdHint"], vm.ThresholdHint);
            vm.SelectedKind = vm.KindOptions.First();
        });
    }
}
