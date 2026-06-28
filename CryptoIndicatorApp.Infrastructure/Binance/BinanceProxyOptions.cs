using Binance.Net.Objects.Options;
using CryptoExchange.Net.Objects;

namespace CryptoIndicatorApp.Infrastructure.Binance;

public sealed class BinanceConnectionOptions
{
    public BinanceProxyOptions Proxy { get; set; } = new();

    public void ApplyTo(BinanceRestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var proxy = Proxy.ToApiProxy();
        if (proxy is not null)
        {
            options.Proxy = proxy;
        }
    }

    public void ApplyTo(BinanceSocketOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var proxy = Proxy.ToApiProxy();
        if (proxy is not null)
        {
            options.Proxy = proxy;
        }
    }
}

public sealed class BinanceProxyOptions
{
    public bool Enabled { get; set; }

    public string Type { get; set; } = "Http";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public ApiProxy? ToApiProxy()
    {
        if (!Enabled)
        {
            return null;
        }

        var normalizedType = string.IsNullOrWhiteSpace(Type)
            ? "Http"
            : Type.Trim();
        if (!string.Equals(normalizedType, "Http", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Proxy type '{normalizedType}' is not supported. Binance.Net/CryptoExchange.Net ApiProxy exposes HTTP-style host/port proxy settings; use a local HTTP proxy bridge if your ShadowSocks endpoint is SOCKS-only.");
        }

        var normalizedHost = NormalizeHttpHost(Host);
        if (string.IsNullOrWhiteSpace(normalizedHost))
        {
            throw new InvalidOperationException("Proxy host is required when proxy is enabled.");
        }

        if (Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("Proxy port must be between 1 and 65535 when proxy is enabled.");
        }

        return new ApiProxy(normalizedHost, Port);
    }

    private static string NormalizeHttpHost(string host)
    {
        var normalizedHost = host.Trim();
        if (string.IsNullOrWhiteSpace(normalizedHost))
        {
            return string.Empty;
        }

        return normalizedHost.Contains("://", StringComparison.Ordinal)
            ? normalizedHost
            : $"http://{normalizedHost}";
    }
}
