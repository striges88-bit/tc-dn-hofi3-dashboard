using Binance.Net.Objects.Options;
using CryptoIndicatorApp.Infrastructure.Binance;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class BinanceProxyOptionsTests
{
    [Fact]
    public void Enabled_http_proxy_is_applied_to_rest_and_socket_options()
    {
        var connectionOptions = new BinanceConnectionOptions
        {
            Proxy = new BinanceProxyOptions
            {
                Enabled = true,
                Type = "Http",
                Host = "127.0.0.1",
                Port = 1080
            }
        };
        var restOptions = new BinanceRestOptions();
        var socketOptions = new BinanceSocketOptions();

        connectionOptions.ApplyTo(restOptions);
        connectionOptions.ApplyTo(socketOptions);

        Assert.NotNull(restOptions.Proxy);
        Assert.Equal("http://127.0.0.1", restOptions.Proxy.Host);
        Assert.Equal(1080, restOptions.Proxy.Port);
        Assert.NotNull(socketOptions.Proxy);
        Assert.Equal("http://127.0.0.1", socketOptions.Proxy.Host);
        Assert.Equal(1080, socketOptions.Proxy.Port);
    }

    [Fact]
    public void Disabled_proxy_leaves_rest_and_socket_options_without_proxy()
    {
        var connectionOptions = new BinanceConnectionOptions();
        var restOptions = new BinanceRestOptions();
        var socketOptions = new BinanceSocketOptions();

        connectionOptions.ApplyTo(restOptions);
        connectionOptions.ApplyTo(socketOptions);

        Assert.Null(restOptions.Proxy);
        Assert.Null(socketOptions.Proxy);
    }

    [Fact]
    public void Unsupported_proxy_type_fails_fast()
    {
        var connectionOptions = new BinanceConnectionOptions
        {
            Proxy = new BinanceProxyOptions
            {
                Enabled = true,
                Type = "Socks5",
                Host = "127.0.0.1",
                Port = 1080
            }
        };

        var error = Assert.Throws<NotSupportedException>(() =>
            connectionOptions.ApplyTo(new BinanceRestOptions()));
        Assert.Contains("HTTP", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Socks5", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
