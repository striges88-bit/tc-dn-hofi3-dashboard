using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CryptoIndicatorApp.Application.Charts;
using CryptoIndicatorApp.Application.Context;
using CryptoIndicatorApp.Application.MarketData;
using CryptoIndicatorApp.Application.Sessions;
using CryptoIndicatorApp.Desktop.Composition;
using CryptoIndicatorApp.Desktop.Configuration;
using CryptoIndicatorApp.Desktop.Rendering;
using CryptoIndicatorApp.Desktop.ViewModels;
using CryptoIndicatorApp.Infrastructure.Binance;

namespace CryptoIndicatorApp.Desktop;

public partial class MainWindow : Window
{
    private readonly DashboardOptions _options;
    private readonly DashboardViewModel _viewModel;
    private CancellationTokenSource? _sessionCancellation;
    private Task? _sessionTask;
    private CancellationTokenSource? _contextCancellation;
    private Task? _contextTask;

    public MainWindow()
    {
        InitializeComponent();

        _options = DashboardConfiguration.Load();
        _viewModel = new DashboardViewModel(_options);
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = _viewModel;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _sessionCancellation?.Cancel();
        _contextCancellation?.Cancel();
        _sessionCancellation?.Dispose();
        _contextCancellation?.Dispose();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        StartSelectedSession();
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await StopCurrentSessionAsync();
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        await StopCurrentSessionAsync();
        StartSelectedSession();
    }

    private async void RefreshSymbolsButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MarkSymbolsRefreshing();

        try
        {
            using var provider = new BinanceNetUsdFuturesMarketDataClient(new BinanceConnectionOptions
            {
                Proxy = BinanceProxyOptionsMapper.ToInfrastructure(_options.Proxy)
            });

            var symbols = await provider.GetActivePerpetualSymbolsAsync();
            _viewModel.SetSymbols(symbols);
            _viewModel.MarkSymbolsRefreshed(symbols.Count);
        }
        catch (Exception ex)
        {
            _viewModel.MarkSymbolsRefreshError(ex.Message);
        }
    }

    private void StartSelectedSession()
    {
        if (!_viewModel.CanStart)
        {
            return;
        }

        CleanupCompletedSession();
        _viewModel.ResetSamples();

        _sessionCancellation = new CancellationTokenSource();
        var cancellationToken = _sessionCancellation.Token;
        var symbol = _viewModel.SelectedSymbol;

        _sessionTask = _viewModel.SelectedMode == DashboardMode.Live
            ? StartLiveSessionAsync(symbol, cancellationToken)
            : StartReplaySessionAsync(symbol, cancellationToken);
    }

    private async Task StopCurrentSessionAsync()
    {
        if (_sessionCancellation is null || _sessionTask is null || _sessionTask.IsCompleted)
        {
            await StopContextSessionAsync();
            CleanupCompletedSession();
            _viewModel.MarkStopped();
            return;
        }

        _viewModel.MarkStopping();
        _sessionCancellation.Cancel();
        _contextCancellation?.Cancel();

        try
        {
            await _sessionTask;
        }
        finally
        {
            await StopContextSessionAsync();
            CleanupCompletedSession();
        }
    }

    private async Task StartReplaySessionAsync(string symbol, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ReplayPath))
        {
            _viewModel.MarkError("Replay path not configured");
            return;
        }

        var replayPath = ResolvePath(_options.ReplayPath);
        if (!File.Exists(replayPath))
        {
            _viewModel.MarkError("Replay file not found");
            return;
        }

        var source = JsonlMarketEventSource.Create(replayPath);
        var session = new ReplayIndicatorSession(
            symbol,
            source,
            _options.ToIndicatorParameters());

        _viewModel.MarkContextLiveOnly();
        await RunSessionAsync(session.RunAsync(cancellationToken), cancellationToken);
    }

    private async Task StartLiveSessionAsync(string symbol, CancellationToken cancellationToken)
    {
        var recordingPath = ResolvePath(_viewModel.RecordingPath);

        using var source = BinanceLiveMarketEventSource.Create(symbol, _options.Proxy);
        IMarketEventRecorder recorder = JsonlMarketEventRecorder.Create(recordingPath);
        var session = new LiveIndicatorSession(
            symbol,
            source,
            recorder,
            _options.ToIndicatorParameters());

        _contextCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _contextTask = StartContextSessionAsync(symbol, _contextCancellation.Token);

        try
        {
            await RunSessionAsync(session.RunAsync(cancellationToken), cancellationToken);
        }
        finally
        {
            _contextCancellation.Cancel();
            await StopContextSessionAsync();
        }
    }

    private async Task StartContextSessionAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            using var source = BinanceContextDataSource.Create(_options.Proxy);
            var context = _options.Context;
            var session = new ContextModuleSession(
                symbol,
                context.Frame,
                source,
                context.VisibleDuration,
                context.NormalizationHistory,
                context.MinimumNormalizationBuckets,
                context.NormalizationFloor,
                context.OpenInterestHistoryLimit,
                context.OpenInterestRefreshInterval);

            await foreach (var sample in session.RunAsync(cancellationToken).WithCancellation(cancellationToken))
            {
                await Dispatcher.InvokeAsync(() => _viewModel.ApplyContextSample(sample));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => _viewModel.MarkContextError(ex.Message));
        }
    }

    private async Task StopContextSessionAsync()
    {
        if (_contextCancellation is null && _contextTask is null)
        {
            return;
        }

        _contextCancellation?.Cancel();

        try
        {
            if (_contextTask is not null)
            {
                await _contextTask;
            }
        }
        finally
        {
            _contextCancellation?.Dispose();
            _contextCancellation = null;
            _contextTask = null;
        }
    }

    private async Task RunSessionAsync(
        IAsyncEnumerable<CryptoIndicatorApp.Domain.Indicators.IndicatorSample> samples,
        CancellationToken cancellationToken)
    {
        try
        {
            _viewModel.MarkRunning();
            await Task.Run(async () =>
            {
                await foreach (var sample in samples.WithCancellation(cancellationToken))
                {
                    await Dispatcher.InvokeAsync(() => _viewModel.ApplySample(sample));
                }
            }, cancellationToken);
            _viewModel.MarkCompleted();
        }
        catch (OperationCanceledException)
        {
            _viewModel.MarkStopped();
        }
        catch (Exception ex)
        {
            _viewModel.MarkError(ex.Message);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardViewModel.ChartSamples))
        {
            RenderChart();
        }
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderChart();
    }

    private void RenderChart()
    {
        var samples = _viewModel.ChartSamples;
        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;

        RenderZeroLine(width, height);

        if (samples.Count == 0 || width <= 1 || height <= 1)
        {
            ZOfiLine.Points = new PointCollection();
            TfiLine.Points = new PointCollection();
            return;
        }

        var maxAbs = ChartGeometryBuilder.CalculateMaxAbs(samples, sample => sample.ZOfi, sample => sample.Tfi);

        ZOfiLine.Points = new PointCollection(ChartGeometryBuilder.BuildPoints(samples, sample => sample.ZOfi, width, height, maxAbs));
        TfiLine.Points = new PointCollection(ChartGeometryBuilder.BuildPoints(samples, sample => sample.Tfi, width, height, maxAbs));
    }

    private void RenderZeroLine(double width, double height)
    {
        var points = ChartGeometryBuilder.BuildZeroLine(width, height);
        if (points.Count < 2)
        {
            ZeroLine.X1 = 0d;
            ZeroLine.X2 = 0d;
            ZeroLine.Y1 = 0d;
            ZeroLine.Y2 = 0d;
            return;
        }

        ZeroLine.X1 = points[0].X;
        ZeroLine.Y1 = points[0].Y;
        ZeroLine.X2 = points[1].X;
        ZeroLine.Y2 = points[1].Y;
    }

    private static string ResolvePath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private void CleanupCompletedSession()
    {
        if (_sessionTask is { IsCompleted: false })
        {
            return;
        }

        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
        _sessionTask = null;
    }
}
