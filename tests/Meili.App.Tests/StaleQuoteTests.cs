using Meili.App.Services;
using Meili.Core.Abstractions;
using Meili.Core.Quotes;
using Meili.Core.Models;
using Meili.Data.Routing;

namespace Meili.App.Tests;

/// <summary>D8: stale marker beside prices and the all-sources-failed InfoBar.</summary>
public class StaleQuoteTests
{
    [Fact]
    public void Old_prices_are_flagged_and_total_failure_opens_the_infobar()
    {
        WpfHost.Run(async () =>
        {
            var fake = (FakeMarketDataService)AppServices.Get<IMarketDataService>();
            var hub = AppServices.Get<QuoteHub>();
            var dash = AppServices.Get<DashboardViewModel>();
            var watch = AppServices.Get<WatchlistViewModel>();
            await dash.ActivateAsync();
            await watch.ActivateAsync();
            Assert.All(dash.Quotes, c => Assert.False(c.IsStale));
            Assert.All(dash.Quotes, c => Assert.EndsWith(" ET", c.PriceTimeText));
            Assert.False(dash.AllSourcesFailed);

            // Prices that were fetched 10 minutes ago and could not be refreshed since.
            fake.QuoteFactory = s => FakeMarketDataService.MakeQuote(s) with { FetchedAt = DateTimeOffset.UtcNow.AddMinutes(-10) };
            await hub.PollAsync();
            Assert.All(dash.Quotes, c => Assert.True(c.IsStale));
            Assert.All(watch.Rows, r => Assert.True(r.IsStale));
            Assert.Contains("10", dash.Quotes[0].StaleTip);

            fake.QuoteFailure = new AllSourcesFailedException(DataCapability.Quote, [("yahoo", "offline"), ("sina", "offline")]);
            await hub.PollAsync();
            Assert.True(dash.AllSourcesFailed);
            Assert.True(watch.AllSourcesFailed);

            fake.QuoteFailure = null;
            fake.QuoteFactory = null;
            await hub.PollAsync();
            Assert.False(dash.AllSourcesFailed);
            Assert.False(watch.AllSourcesFailed);
            Assert.All(dash.Quotes, c => Assert.False(c.IsStale));
            dash.Deactivate();
            watch.Deactivate();
        });
    }
}
