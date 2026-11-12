using Meili.App.Services;
using Meili.App.ViewModels;
using Meili.Core.Abstractions;
using Meili.Core.Quotes;

namespace Meili.App.Tests;

/// <summary>C1: pages share quotes through the hub instead of polling separately.</summary>
public class QuoteHubIntegrationTests
{
    [Fact]
    public void Pages_share_one_fetch_and_follow_the_pump()
    {
        WpfHost.Run(async () =>
        {
            var fake = (FakeMarketDataService)AppServices.Get<IMarketDataService>();
            var hub = AppServices.Get<QuoteHub>();
            var watch = AppServices.Get<WatchlistViewModel>();
            await watch.ActivateAsync();
            var afterWatch = fake.QuoteCalls;
            var dash = AppServices.Get<DashboardViewModel>();
            await dash.ActivateAsync();                       // same symbols, inside the TTL: no new request
            Assert.Equal(afterWatch, fake.QuoteCalls);
            Assert.All(dash.Quotes, c => Assert.NotEqual("—", c.PriceText));

            await hub.PollAsync();                            // the app-level tick: exactly one request for both pages
            Assert.Equal(afterWatch + 1, fake.QuoteCalls);

            dash.Deactivate();
            watch.Deactivate();
            Assert.Empty(hub.SubscribedSymbols);                // hidden pages stop asking (alerts are off in tests)
        });
    }
}
