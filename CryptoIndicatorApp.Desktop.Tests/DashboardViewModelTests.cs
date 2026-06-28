using CryptoIndicatorApp.Application.Context;
using CryptoIndicatorApp.Desktop.Configuration;
using CryptoIndicatorApp.Desktop.ViewModels;
using CryptoIndicatorApp.Domain.Context;
using CryptoIndicatorApp.Domain.Indicators;
using CryptoIndicatorApp.Domain.OrderBooks;

namespace CryptoIndicatorApp.Desktop.Tests;

public sealed class DashboardViewModelTests
{
    [Fact]
    public void View_model_defaults_to_live_mode_and_single_selected_symbol()
    {
        var viewModel = new DashboardViewModel(new DashboardOptions
        {
            Symbol = "xanusdt"
        });

        Assert.Equal("XANUSDT", viewModel.SelectedSymbol);
        Assert.Equal("XANUSDT", viewModel.Symbol);
        Assert.Equal(DashboardMode.Live, viewModel.SelectedMode);
        Assert.Equal("Live", viewModel.ModeText);
        Assert.Equal(new[] { "XANUSDT" }, viewModel.Symbols);
        Assert.Equal(new[] { "XANUSDT" }, viewModel.FilteredSymbols);
        Assert.True(viewModel.CanStart);
        Assert.False(viewModel.CanStop);
        Assert.True(viewModel.IsConfigurationEnabled);
    }

    [Fact]
    public void Set_symbols_keeps_one_active_selected_symbol()
    {
        var viewModel = new DashboardViewModel(new DashboardOptions
        {
            Symbol = "XANUSDT"
        });

        viewModel.SetSymbols(new[] { "PLAYUSDT", "ESPORTSUSDT", "PLAYUSDT" });

        Assert.Equal(new[] { "ESPORTSUSDT", "PLAYUSDT" }, viewModel.Symbols);
        Assert.Equal(new[] { "ESPORTSUSDT", "PLAYUSDT" }, viewModel.FilteredSymbols);
        Assert.Equal("ESPORTSUSDT", viewModel.SelectedSymbol);
        Assert.Equal("recordings/esportsusdt.jsonl", viewModel.RecordingPath);
    }

    [Fact]
    public void Symbol_search_filters_and_selects_matching_symbol_without_scrolling()
    {
        var viewModel = new DashboardViewModel(new DashboardOptions
        {
            Symbol = "BTCUSDT"
        });
        viewModel.SetSymbols(new[] { "ESPORTSUSDT", "PLAYUSDT", "XANUSDT" });

        viewModel.SymbolSearchText = "xan";

        Assert.Equal("XAN", viewModel.SymbolSearchText);
        Assert.Equal(new[] { "XANUSDT" }, viewModel.FilteredSymbols);
        Assert.Equal("XANUSDT", viewModel.SelectedSymbol);

        viewModel.SymbolSearchText = "playusdt";

        Assert.Equal("PLAYUSDT", viewModel.SymbolSearchText);
        Assert.Equal(new[] { "PLAYUSDT" }, viewModel.FilteredSymbols);
        Assert.Equal("PLAYUSDT", viewModel.SelectedSymbol);
    }

    [Fact]
    public void Invalid_symbol_search_does_not_start_previous_selection()
    {
        var viewModel = new DashboardViewModel(new DashboardOptions
        {
            Symbol = "BTCUSDT"
        });
        viewModel.SetSymbols(new[] { "BTCUSDT", "XANUSDT" });

        viewModel.SymbolSearchText = "NO_SUCH_SYMBOL";

        Assert.Empty(viewModel.FilteredSymbols);
        Assert.False(viewModel.CanStart);
    }

    [Fact]
    public void Running_state_updates_command_availability()
    {
        var viewModel = new DashboardViewModel(new DashboardOptions
        {
            Symbol = "BTCUSDT"
        });

        viewModel.MarkRunning();

        Assert.True(viewModel.IsRunning);
        Assert.False(viewModel.CanStart);
        Assert.True(viewModel.CanStop);
        Assert.False(viewModel.IsConfigurationEnabled);

        viewModel.MarkStopped();

        Assert.False(viewModel.IsRunning);
        Assert.True(viewModel.CanStart);
        Assert.False(viewModel.CanStop);
        Assert.True(viewModel.IsConfigurationEnabled);
    }

    [Fact]
    public void Always_on_top_toggle_defaults_off_and_notifies_changes()
    {
        var viewModel = new DashboardViewModel(new DashboardOptions
        {
            Symbol = "BTCUSDT"
        });
        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changedProperties.Add(args.PropertyName);
            }
        };

        Assert.False(viewModel.IsAlwaysOnTop);

        viewModel.IsAlwaysOnTop = true;

        Assert.True(viewModel.IsAlwaysOnTop);
        Assert.Contains(nameof(DashboardViewModel.IsAlwaysOnTop), changedProperties);
    }

    [Fact]
    public void Apply_sample_updates_status_metrics_and_chart_points()
    {
        var options = new DashboardOptions
        {
            Symbol = "BTCUSDT",
            Mode = DashboardMode.Replay,
            ReplayPath = "session.jsonl",
            RecordingPath = "recordings/btcusdt.jsonl",
            ChartWindowSeconds = 60
        };
        var viewModel = new DashboardViewModel(options);
        var timestamp = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        var sample = new IndicatorSample(
            timestamp,
            Hofi: 12.5m,
            Nofi: 0.01m,
            ZOfi: 2.34567m,
            Tfi: -0.125m,
            SignalState.ShortCandidate,
            new BookHealth(true, false, false, 101, 1, null),
            TimeSpan.FromMilliseconds(42));

        viewModel.MarkRunning();
        viewModel.ApplySample(sample);

        Assert.Equal("BTCUSDT", viewModel.Symbol);
        Assert.Equal("Replay", viewModel.ModeText);
        Assert.Equal("Running", viewModel.ConnectionStatus);
        Assert.Equal("Synced", viewModel.BookHealthText);
        Assert.Equal("42 ms", viewModel.LatencyText);
        Assert.Equal("2.3457", viewModel.ZOfiText);
        Assert.Equal("-0.1250", viewModel.TfiText);
        Assert.Equal("ShortCandidate", viewModel.SignalText);
        Assert.Equal("Short", viewModel.SignalVisualDirection);
        Assert.InRange(viewModel.SignalVisualIntensity, 0.01d, 1d);
        Assert.Equal("101", viewModel.LastUpdateIdText);
        Assert.Single(viewModel.ChartSamples);
    }

    [Fact]
    public void Replay_path_display_is_mode_aware()
    {
        var liveViewModel = new DashboardViewModel(new DashboardOptions
        {
            Symbol = "BTCUSDT",
            Mode = DashboardMode.Live,
            ReplayPath = null
        });

        Assert.Equal("Live mode", liveViewModel.ReplayPathDisplay);
        Assert.InRange(liveViewModel.ReplayPathOpacity, 0.1d, 0.99d);

        var replayWithoutPath = new DashboardViewModel(new DashboardOptions
        {
            Symbol = "BTCUSDT",
            Mode = DashboardMode.Replay,
            ReplayPath = null
        });

        Assert.Equal("Replay file not configured", replayWithoutPath.ReplayPathDisplay);
        Assert.Equal(1d, replayWithoutPath.ReplayPathOpacity);

        var replayWithPath = new DashboardViewModel(new DashboardOptions
        {
            Symbol = "BTCUSDT",
            Mode = DashboardMode.Replay,
            ReplayPath = "session.jsonl"
        });

        Assert.Equal("session.jsonl", replayWithPath.ReplayPathDisplay);
        Assert.Equal(1d, replayWithPath.ReplayPathOpacity);
    }

    [Fact]
    public void Signal_visual_intensity_uses_existing_zofi_and_tfi_without_changing_text()
    {
        var viewModel = new DashboardViewModel(new DashboardOptions
        {
            Symbol = "BTCUSDT",
            Indicator =
            {
                ThetaZ = 2.0m,
                ThetaTfi = 0.15m
            }
        });
        var timestamp = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");

        viewModel.ApplySample(new IndicatorSample(
            timestamp,
            Hofi: 0m,
            Nofi: 0m,
            ZOfi: 4.0m,
            Tfi: 0.30m,
            SignalState.LongCandidate,
            BookHealth.Empty,
            ExchangeToReceiveLatency: null));

        Assert.Equal("LongCandidate", viewModel.SignalText);
        Assert.Equal("Long", viewModel.SignalVisualDirection);
        Assert.InRange(viewModel.SignalVisualIntensity, 0.95d, 1d);

        viewModel.ApplySample(new IndicatorSample(
            timestamp.AddMilliseconds(100),
            Hofi: 0m,
            Nofi: 0m,
            ZOfi: 0.25m,
            Tfi: 0.01m,
            SignalState.Neutral,
            BookHealth.Empty,
            ExchangeToReceiveLatency: null));

        Assert.Equal("Neutral", viewModel.SignalText);
        Assert.Equal("Neutral", viewModel.SignalVisualDirection);
        Assert.Equal(0d, viewModel.SignalVisualIntensity);
    }

    [Fact]
    public void Context_tile_projection_keeps_direction_and_gradient_intensity()
    {
        var viewModel = new DashboardViewModel(new DashboardOptions());
        var start = DateTimeOffset.Parse("2026-05-26T12:00:00Z");

        viewModel.ApplyContextSample(new ContextModuleSample(
            "XANUSDT",
            ContextFrame.FifteenMinutes,
            start,
            new[]
            {
                new ContextTile(
                    start,
                    start.AddMinutes(15),
                    RawDelta: 100m,
                    NormalizedDelta: 0.001m,
                    ContextDirection.Positive,
                    Intensity: 0.75d,
                    IsReady: true,
                    Status: "Ready")
            },
            new[]
            {
                new ContextTile(
                    start,
                    start.AddMinutes(15),
                    RawDelta: -200m,
                    NormalizedDelta: -0.002m,
                    ContextDirection.Negative,
                    Intensity: 0.5d,
                    IsReady: true,
                    Status: "Ready")
            },
            "Ready",
            "Ready"));

        Assert.Single(viewModel.LiquidationTiles);
        Assert.Equal("Ready", viewModel.LiquidationStatus);
        Assert.Equal("Ready", viewModel.OpenInterestStatus);
        Assert.Equal(0.75d, viewModel.LiquidationTiles[0].Intensity);
        Assert.Equal(ContextDirection.Positive, viewModel.LiquidationTiles[0].Direction);
    }

    [Fact]
    public void Context_tooltips_explain_liquidation_stream_limits()
    {
        var viewModel = new DashboardViewModel(new DashboardOptions());

        Assert.Contains("largest", viewModel.LiquidationHelpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1000 ms", viewModel.LiquidationHelpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not total", viewModel.LiquidationHelpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REST", viewModel.OpenInterestHelpText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_sample_keeps_only_configured_chart_window()
    {
        var viewModel = new DashboardViewModel(new DashboardOptions
        {
            Symbol = "BTCUSDT",
            ChartWindowSeconds = 60
        });
        var start = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");

        viewModel.ApplySample(CreateSample(start, 1m));
        viewModel.ApplySample(CreateSample(start.AddSeconds(30), 2m));
        viewModel.ApplySample(CreateSample(start.AddSeconds(61), 3m));

        Assert.Collection(
            viewModel.ChartSamples,
            point => Assert.Equal(2m, point.ZOfi),
            point => Assert.Equal(3m, point.ZOfi));
    }

    private static IndicatorSample CreateSample(DateTimeOffset timestamp, decimal zOfi)
    {
        return new IndicatorSample(
            timestamp,
            Hofi: 0m,
            Nofi: 0m,
            ZOfi: zOfi,
            Tfi: 0m,
            SignalState.Neutral,
            BookHealth.Empty,
            ExchangeToReceiveLatency: null);
    }
}
