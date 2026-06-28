using System.IO;
using CryptoIndicatorApp.Desktop.Configuration;
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Desktop.Tests;

public sealed class DashboardConfigurationTests
{
    [Fact]
    public void Loads_and_normalizes_proxy_options_from_config()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "appsettings.json"), """
            {
              "Dashboard": {
                "Symbol": "btcusdt",
                "Mode": "Live",
                "Proxy": {
                  "Enabled": true,
                  "Type": " http ",
                  "Host": " 127.0.0.1 ",
                  "Port": 1080
                }
              }
            }
            """);

        var options = DashboardConfiguration.Load(directory);

        Assert.Equal("BTCUSDT", options.Symbol);
        Assert.True(options.Proxy.Enabled);
        Assert.Equal("Http", options.Proxy.Type);
        Assert.Equal("127.0.0.1", options.Proxy.Host);
        Assert.Equal(1080, options.Proxy.Port);
        Assert.Equal(ContextFrame.FifteenMinutes, options.Context.Frame);
        Assert.Equal(150, options.Context.VisibleMinutes);
        Assert.Equal(24, options.Context.NormalizationHistoryHours);
        Assert.Equal(12, options.Context.MinimumNormalizationBuckets);
        Assert.Equal(60, options.Context.OpenInterestRefreshSeconds);
        Assert.Equal(TimeSpan.FromSeconds(60), options.Context.OpenInterestRefreshInterval);
    }
}
