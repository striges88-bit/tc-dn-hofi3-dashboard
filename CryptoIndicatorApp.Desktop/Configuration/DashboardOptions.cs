using CryptoIndicatorApp.Domain.Indicators;

namespace CryptoIndicatorApp.Desktop.Configuration;

public sealed class DashboardOptions
{
    public string Symbol { get; set; } = "BTCUSDT";

    public DashboardMode Mode { get; set; } = DashboardMode.Live;

    public string? ReplayPath { get; set; }

    public string RecordingPath { get; set; } = "recordings/{symbol}.jsonl";

    public int ChartWindowSeconds { get; set; } = 60;

    public ProxyOptions Proxy { get; set; } = new();

    public ContextOptions Context { get; set; } = new();

    public IndicatorParameterOptions Indicator { get; set; } = new();

    public TimeSpan ChartWindow => TimeSpan.FromSeconds(ChartWindowSeconds > 0 ? ChartWindowSeconds : 60);

    public IndicatorParameters ToIndicatorParameters()
    {
        return Indicator.ToDomain();
    }

    public void Normalize()
    {
        Symbol = string.IsNullOrWhiteSpace(Symbol)
            ? "BTCUSDT"
            : Symbol.Trim().ToUpperInvariant();

        RecordingPath = string.IsNullOrWhiteSpace(RecordingPath)
            ? "recordings/{symbol}.jsonl"
            : RecordingPath.Trim();

        ReplayPath = string.IsNullOrWhiteSpace(ReplayPath)
            ? null
            : ReplayPath.Trim();

        if (ChartWindowSeconds <= 0)
        {
            ChartWindowSeconds = 60;
        }

        Proxy ??= new ProxyOptions();
        Proxy.Normalize();

        Context ??= new ContextOptions();
        Context.Normalize();
    }
}
