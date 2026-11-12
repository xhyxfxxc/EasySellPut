using TyuStock.App.Services;
using TyuStock.App.ViewModels;
using TyuStock.Core.Alerts;
using TyuStock.Core.Models;
using TyuStock.Core.Quotes;
using TyuStock.Core.Settings;
using TyuStock.Core.Time;

namespace TyuStock.App.Tests;

/// <summary>F3 stale-quote gate and F10 interruption banner, driven through the monitor's batch handler.</summary>
public class AlertMonitorGuardTests
{
    private static readonly DateTimeOffset InSession = MarketClock.FromEastern(new DateTime(2026, 11, 12, 11, 0, 0));

    [Fact]
    public void Stale_quotes_are_skipped_and_failures_raise_the_banner_then_clear()
    {
        WpfHost.Run(() =>
        {
            var store = AppServices.Get<AlertStore>();
            var monitor = AppServices.Get<AlertMonitor>();
            var vm = AppServices.Get<AlertsViewModel>();
            var alert = store.Upsert(new PriceAlert { Symbol = "ZZZT", Kind = AlertKind.PriceAbove, Threshold = 50 });
            try
            {
                var stale = new Quote { Symbol = "ZZZT", Price = 60, Source = "sina", AsOf = InSession.AddMinutes(-45) };
                monitor.OnBatchCore(new QuoteBatch(["ZZZT"], [stale], null, InSession), InSession);
                Assert.Contains("ZZZT", monitor.LastCheck!.SkippedStale);
                Assert.False(store.Find(alert.Id)!.HasSeenQuote);            // not evaluated at all
                vm.RefreshMonitorState();
                Assert.True(vm.HasStaleNote);

                var fresh = stale with { AsOf = InSession.AddMinutes(-1) };
                monitor.OnBatchCore(new QuoteBatch(["ZZZT"], [fresh], null, InSession), InSession);
                Assert.True(store.Find(alert.Id)!.HasSeenQuote);
                vm.RefreshMonitorState();
                Assert.False(vm.HasStaleNote);

                var err = new InvalidOperationException("all sources down");
                for (int i = 0; i < 3; i++)
                    monitor.OnBatchCore(new QuoteBatch(["ZZZT"], [], err, InSession.AddMinutes(i)), InSession.AddMinutes(i));
                Assert.True(monitor.Health.Interrupted);
                Assert.True(vm.MonitorDown);                                // HealthChanged → banner
                Assert.Contains("all sources down", vm.MonitorText);

                monitor.OnBatchCore(new QuoteBatch(["ZZZT"], [fresh], null, InSession.AddMinutes(5)), InSession.AddMinutes(5));
                vm.RefreshMonitorState();
                Assert.False(vm.MonitorDown);

                // Batches that do not concern alert symbols never count as failures.
                monitor.OnBatchCore(new QuoteBatch(["OTHER"], [], err, InSession), InSession);
                Assert.Equal(0, monitor.Health.ConsecutiveFailures);
            }
            finally { store.Remove(alert.Id); }
        });
    }

    [Fact]
    public void Max_delay_choice_is_persisted()
    {
        WpfHost.Run(() =>
        {
            var vm = AppServices.Get<AlertsViewModel>();
            var settings = AppServices.Get<SettingsStore>();
            vm.MaxDelayMinutes = 30;
            Assert.Equal(30, settings.Current.Alerts.MaxQuoteDelayMinutes);
            vm.MaxDelayMinutes = 20;
        });
    }
}
