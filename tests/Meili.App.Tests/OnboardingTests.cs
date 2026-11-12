using System.IO;
using Meili.App.Services;
using Meili.Core.Abstractions;
using Meili.Core.Quotes;
using Meili.Core.Models;
using Meili.Core.Settings;
using Meili.Data.Routing;

namespace Meili.App.Tests;

/// <summary>D7: getting-started card and the no-proxy explanation when every source fails.</summary>
public class OnboardingTests
{
    [Fact]
    public void Guide_shows_network_state_and_stays_closed_once_dismissed()
    {
        WpfHost.Run(() =>
        {
            var dash = AppServices.Get<DashboardViewModel>();
            var loc = AppServices.Get<LocalizationService>();
            var settings = AppServices.Get<SettingsStore>();
            try
            {
                Assert.Equal(!settings.Current.GuideDismissed, dash.ShowGuide);
                dash.DismissGuideCommand.Execute(null);
                Assert.False(dash.ShowGuide);
                Assert.True(settings.Current.GuideDismissed);
                Assert.True(new SettingsStore(Path.GetDirectoryName(settings.FilePath)!).Load().GuideDismissed);
            }
            finally
            {
                settings.Update(s => s.GuideDismissed = false);
                dash.ShowGuide = true;
            }
        });
    }

    [Fact]
    public void All_sources_failing_without_a_proxy_explains_the_proxy_and_redetects()
    {
        WpfHost.Run(async () =>
        {
            var fake = (FakeMarketDataService)AppServices.Get<IMarketDataService>();
            var hub = AppServices.Get<QuoteHub>();
            var dash = AppServices.Get<DashboardViewModel>();
            var loc = AppServices.Get<LocalizationService>();
            var net = WpfHost.Network;
            try
            {
                await dash.ActivateAsync();
                net.NoProxyDetected = true;
                fake.QuoteFailure = new AllSourcesFailedException(DataCapability.Quote, [("yahoo", "offline")]);
                await hub.PollAsync();
                Assert.True(dash.ProxyMissing);
                Assert.Equal(loc["Dash_NoProxyBody"], dash.FailureBody);
                Assert.Equal(loc["Dash_GuideNetNone"], dash.GuideNetworkText);

                // The user starts Clash and re-detects: the proxy is found and quotes come back.
                net.NoProxyDetected = false;
                fake.QuoteFailure = null;
                var before = net.Reconfigured;
                await dash.RedetectProxyCommand.ExecuteAsync(null);
                Assert.Equal(before + 1, net.Reconfigured);
                Assert.Equal(string.Format(loc["Dash_GuideNetOk"], "127.0.0.1:7890"), dash.GuideNetworkText);
                await hub.PollAsync();
                Assert.False(dash.AllSourcesFailed);
                Assert.False(dash.ProxyMissing);

                // With a proxy present the generic failure text is used.
                fake.QuoteFailure = new AllSourcesFailedException(DataCapability.Quote, [("yahoo", "offline")]);
                await hub.PollAsync();
                Assert.False(dash.ProxyMissing);
                Assert.Equal(loc["Dash_AllFailedBody"], dash.FailureBody);
            }
            finally
            {
                fake.QuoteFailure = null;
                net.NoProxyDetected = false;
                await hub.PollAsync();
                dash.Deactivate();
            }
        });
    }
}
