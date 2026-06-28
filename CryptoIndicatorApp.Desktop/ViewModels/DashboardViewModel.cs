using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using CryptoIndicatorApp.Application.Charts;
using CryptoIndicatorApp.Application.Context;
using CryptoIndicatorApp.Desktop.Configuration;
using CryptoIndicatorApp.Domain.Context;
using CryptoIndicatorApp.Domain.Indicators;
using CryptoIndicatorApp.Domain.OrderBooks;

namespace CryptoIndicatorApp.Desktop.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<DashboardMode> AvailableModes =
        new[] { DashboardMode.Live, DashboardMode.Replay };

    private readonly ChartSampleBuffer _chartBuffer;
    private string _selectedSymbol;
    private DashboardMode _selectedMode;
    private ContextFrame _selectedContextFrame;
    private IReadOnlyList<string> _symbols;
    private IReadOnlyList<string> _filteredSymbols;
    private string _symbolSearchText;
    private bool _isApplyingSymbolSearch;
    private bool _isRunning;
    private bool _isRefreshingSymbols;
    private string _connectionStatus = "Idle";
    private string _symbolRefreshStatus = "Not refreshed";
    private string _bookHealthText = "No snapshot";
    private string _lastUpdateIdText = "n/a";
    private string _latencyText = "n/a";
    private string _zOfiText = "0.0000";
    private string _tfiText = "0.0000";
    private string _signalText = SignalState.Neutral.ToString();
    private string _signalVisualDirection = "Neutral";
    private double _signalVisualIntensity;
    private Brush _signalBackgroundBrush = Brushes.White;
    private bool _isAlwaysOnTop;
    private IReadOnlyList<ContextTileViewModel> _liquidationTiles = Array.Empty<ContextTileViewModel>();
    private IReadOnlyList<ContextTileViewModel> _openInterestTiles = Array.Empty<ContextTileViewModel>();
    private string _liquidationStatus = "Not started";
    private string _openInterestStatus = "Not started";
    private IReadOnlyList<ChartSample> _chartSamples = Array.Empty<ChartSample>();

    public DashboardViewModel(DashboardOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Normalize();
        _chartBuffer = new ChartSampleBuffer(Options.ChartWindow);
        _selectedSymbol = Options.Symbol;
        _selectedMode = Options.Mode;
        _selectedContextFrame = Options.Context.Frame;
        _symbols = new[] { Options.Symbol };
        _filteredSymbols = _symbols;
        _symbolSearchText = Options.Symbol;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DashboardOptions Options { get; }

    public IReadOnlyList<DashboardMode> Modes => AvailableModes;

    public IReadOnlyList<ContextFrame> ContextFrames { get; } =
        new[] { ContextFrame.FifteenMinutes, ContextFrame.FiveMinutes };

    public string LiquidationHelpText { get; } =
        "Binance force-order stream: observed largest liquidation order per symbol in each 1000 ms interval; not total liquidation volume. No event is sent when no liquidation occurs.";

    public string OpenInterestHelpText { get; } =
        "Open interest context: Binance REST open-interest statistics, refreshed periodically and timestamp-deduplicated.";

    public IReadOnlyList<string> Symbols
    {
        get => _symbols;
        private set => SetField(ref _symbols, value);
    }

    public IReadOnlyList<string> FilteredSymbols
    {
        get => _filteredSymbols;
        private set => SetField(ref _filteredSymbols, value);
    }

    public string SymbolSearchText
    {
        get => _symbolSearchText;
        set
        {
            var normalized = NormalizeSymbol(value);
            if (!SetField(ref _symbolSearchText, normalized))
            {
                return;
            }

            FilteredSymbols = FilterSymbols(normalized, Symbols);
            _isApplyingSymbolSearch = true;
            try
            {
                SelectSearchMatch(normalized);
            }
            finally
            {
                _isApplyingSymbolSearch = false;
            }

            NotifyCommandStateChanged();
        }
    }

    public string SelectedSymbol
    {
        get => _selectedSymbol;
        set
        {
            var normalized = NormalizeSymbol(value);
            if (!SetField(ref _selectedSymbol, normalized))
            {
                return;
            }

            Options.Symbol = normalized;
            if (!_isApplyingSymbolSearch && !string.Equals(_symbolSearchText, normalized, StringComparison.Ordinal))
            {
                _symbolSearchText = normalized;
                OnPropertyChanged(nameof(SymbolSearchText));
            }

            OnPropertyChanged(nameof(Symbol));
            OnPropertyChanged(nameof(RecordingPath));
            NotifyCommandStateChanged();
        }
    }

    public string Symbol => SelectedSymbol;

    public DashboardMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (!SetField(ref _selectedMode, value))
            {
                return;
            }

            Options.Mode = value;
            OnPropertyChanged(nameof(ModeText));
            OnPropertyChanged(nameof(ReplayPath));
            OnPropertyChanged(nameof(ReplayPathDisplay));
            OnPropertyChanged(nameof(ReplayPathOpacity));
            ResetContextSamples();
        }
    }

    public string ModeText => SelectedMode.ToString();

    public ContextFrame SelectedContextFrame
    {
        get => _selectedContextFrame;
        set
        {
            if (!SetField(ref _selectedContextFrame, value))
            {
                return;
            }

            Options.Context.Frame = value;
        }
    }

    public string ReplayPath => ReplayPathDisplay;

    public string ReplayPathDisplay
    {
        get
        {
            if (SelectedMode == DashboardMode.Live)
            {
                return "Live mode";
            }

            return string.IsNullOrWhiteSpace(Options.ReplayPath)
                ? "Replay file not configured"
                : Options.ReplayPath;
        }
    }

    public double ReplayPathOpacity => SelectedMode == DashboardMode.Live ? 0.55d : 1d;

    public string RecordingPath => ResolveRecordingPath(SelectedSymbol);

    public bool IsAlwaysOnTop
    {
        get => _isAlwaysOnTop;
        set => SetField(ref _isAlwaysOnTop, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetField(ref _isRunning, value))
            {
                return;
            }

            NotifyCommandStateChanged();
            OnPropertyChanged(nameof(IsConfigurationEnabled));
        }
    }

    public bool IsRefreshingSymbols
    {
        get => _isRefreshingSymbols;
        private set => SetField(ref _isRefreshingSymbols, value);
    }

    public bool CanStart => !IsRunning && HasValidSymbolSelection();

    public bool CanStop => IsRunning;

    public bool CanRestart => HasValidSymbolSelection();

    public bool IsConfigurationEnabled => !IsRunning;

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetField(ref _connectionStatus, value);
    }

    public string SymbolRefreshStatus
    {
        get => _symbolRefreshStatus;
        private set => SetField(ref _symbolRefreshStatus, value);
    }

    public string BookHealthText
    {
        get => _bookHealthText;
        private set => SetField(ref _bookHealthText, value);
    }

    public string LastUpdateIdText
    {
        get => _lastUpdateIdText;
        private set => SetField(ref _lastUpdateIdText, value);
    }

    public string LatencyText
    {
        get => _latencyText;
        private set => SetField(ref _latencyText, value);
    }

    public string ZOfiText
    {
        get => _zOfiText;
        private set => SetField(ref _zOfiText, value);
    }

    public string TfiText
    {
        get => _tfiText;
        private set => SetField(ref _tfiText, value);
    }

    public string SignalText
    {
        get => _signalText;
        private set => SetField(ref _signalText, value);
    }

    public string SignalVisualDirection
    {
        get => _signalVisualDirection;
        private set => SetField(ref _signalVisualDirection, value);
    }

    public double SignalVisualIntensity
    {
        get => _signalVisualIntensity;
        private set => SetField(ref _signalVisualIntensity, value);
    }

    public Brush SignalBackgroundBrush
    {
        get => _signalBackgroundBrush;
        private set => SetField(ref _signalBackgroundBrush, value);
    }

    public IReadOnlyList<ContextTileViewModel> LiquidationTiles
    {
        get => _liquidationTiles;
        private set => SetField(ref _liquidationTiles, value);
    }

    public IReadOnlyList<ContextTileViewModel> OpenInterestTiles
    {
        get => _openInterestTiles;
        private set => SetField(ref _openInterestTiles, value);
    }

    public string LiquidationStatus
    {
        get => _liquidationStatus;
        private set => SetField(ref _liquidationStatus, value);
    }

    public string OpenInterestStatus
    {
        get => _openInterestStatus;
        private set => SetField(ref _openInterestStatus, value);
    }

    public IReadOnlyList<ChartSample> ChartSamples
    {
        get => _chartSamples;
        private set => SetField(ref _chartSamples, value);
    }

    public void SetSymbols(IEnumerable<string> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        var normalizedSymbols = symbols
            .Select(NormalizeSymbol)
            .Where(symbol => symbol.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (normalizedSymbols.Length == 0)
        {
            return;
        }

        Symbols = normalizedSymbols;
        FilteredSymbols = normalizedSymbols;

        if (!normalizedSymbols.Contains(SelectedSymbol, StringComparer.OrdinalIgnoreCase))
        {
            SelectedSymbol = normalizedSymbols[0];
        }
    }

    public void MarkSymbolsRefreshing()
    {
        IsRefreshingSymbols = true;
        SymbolRefreshStatus = "Refreshing";
    }

    public void MarkSymbolsRefreshed(int count)
    {
        IsRefreshingSymbols = false;
        SymbolRefreshStatus = $"{count.ToString(CultureInfo.InvariantCulture)} active perpetual symbols";
    }

    public void MarkSymbolsRefreshError(string message)
    {
        IsRefreshingSymbols = false;
        SymbolRefreshStatus = string.IsNullOrWhiteSpace(message) ? "Refresh failed" : message;
    }

    public void ResetSamples()
    {
        _chartBuffer.Clear();
        ChartSamples = Array.Empty<ChartSample>();
        BookHealthText = "No snapshot";
        LastUpdateIdText = "n/a";
        LatencyText = "n/a";
        ZOfiText = "0.0000";
        TfiText = "0.0000";
        SignalText = SignalState.Neutral.ToString();
        UpdateSignalVisual(SignalState.Neutral, zOfi: 0m, tfi: 0m);
        ResetContextSamples();
    }

    public void MarkRunning()
    {
        IsRunning = true;
        ConnectionStatus = "Running";
    }

    public void MarkStopping()
    {
        ConnectionStatus = "Stopping";
    }

    public void MarkStopped()
    {
        IsRunning = false;
        ConnectionStatus = "Stopped";
    }

    public void MarkCompleted()
    {
        IsRunning = false;
        ConnectionStatus = "Completed";
    }

    public void MarkError(string message)
    {
        IsRunning = false;
        ConnectionStatus = string.IsNullOrWhiteSpace(message) ? "Error" : message;
    }

    public void ApplySample(IndicatorSample sample)
    {
        BookHealthText = FormatBookHealth(sample.BookHealth);
        LastUpdateIdText = sample.BookHealth.LastUpdateId?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
        LatencyText = FormatLatency(sample.ExchangeToReceiveLatency);
        ZOfiText = FormatDecimal(sample.ZOfi);
        TfiText = FormatDecimal(sample.Tfi);
        SignalText = sample.Signal.ToString();
        UpdateSignalVisual(sample.Signal, sample.ZOfi, sample.Tfi);

        _chartBuffer.Add(sample);
        ChartSamples = _chartBuffer.Snapshot();
    }

    public void ApplyContextSample(ContextModuleSample sample)
    {
        LiquidationTiles = sample.LiquidationTiles.Select(ContextTileViewModel.FromTile).ToArray();
        OpenInterestTiles = sample.OpenInterestTiles.Select(ContextTileViewModel.FromTile).ToArray();
        LiquidationStatus = sample.LiquidationStatus;
        OpenInterestStatus = sample.OpenInterestStatus;
    }

    public void MarkContextLiveOnly()
    {
        LiquidationStatus = "Live context only";
        OpenInterestStatus = "Live context only";
        LiquidationTiles = Array.Empty<ContextTileViewModel>();
        OpenInterestTiles = Array.Empty<ContextTileViewModel>();
    }

    public void MarkContextError(string message)
    {
        var text = string.IsNullOrWhiteSpace(message) ? "Context error" : message;
        LiquidationStatus = text;
        OpenInterestStatus = text;
    }

    private static IReadOnlyList<string> FilterSymbols(string searchText, IReadOnlyList<string> symbols)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return symbols;
        }

        var prefixMatches = symbols
            .Where(symbol => symbol.StartsWith(searchText, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (prefixMatches.Length > 0)
        {
            return prefixMatches;
        }

        return symbols
            .Where(symbol => symbol.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private void SelectSearchMatch(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return;
        }

        var exactMatch = Symbols.FirstOrDefault(
            symbol => string.Equals(symbol, searchText, StringComparison.OrdinalIgnoreCase));
        if (exactMatch is not null)
        {
            SelectedSymbol = exactMatch;
            return;
        }

        if (FilteredSymbols.Count == 1)
        {
            SelectedSymbol = FilteredSymbols[0];
        }
    }

    private bool HasValidSymbolSelection()
    {
        if (string.IsNullOrWhiteSpace(SelectedSymbol))
        {
            return false;
        }

        if (Symbols.Count > 0 && !Symbols.Contains(SelectedSymbol, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SymbolSearchText))
        {
            return true;
        }

        if (string.Equals(SymbolSearchText, SelectedSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return FilteredSymbols.Count == 1
            && string.Equals(FilteredSymbols[0], SelectedSymbol, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatBookHealth(BookHealth health)
    {
        if (health.IsCrossed)
        {
            return "Crossed";
        }

        if (health.IsStale)
        {
            return "Stale";
        }

        if (health.IsSynced)
        {
            return "Synced";
        }

        return string.IsNullOrWhiteSpace(health.Reason) ? "Unsynced" : health.Reason;
    }

    private void ResetContextSamples()
    {
        LiquidationTiles = Array.Empty<ContextTileViewModel>();
        OpenInterestTiles = Array.Empty<ContextTileViewModel>();
        LiquidationStatus = SelectedMode == DashboardMode.Live ? "Not started" : "Live context only";
        OpenInterestStatus = SelectedMode == DashboardMode.Live ? "Not started" : "Live context only";
    }

    private static string FormatLatency(TimeSpan? latency)
    {
        if (latency is null)
        {
            return "n/a";
        }

        return $"{Math.Round(latency.Value.TotalMilliseconds):0} ms";
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.0000", CultureInfo.InvariantCulture);
    }

    private void UpdateSignalVisual(SignalState signal, decimal zOfi, decimal tfi)
    {
        if (signal == SignalState.LongCandidate)
        {
            SignalVisualDirection = "Long";
            SignalVisualIntensity = CalculateSignalIntensity(zOfi, tfi, direction: 1);
            SignalBackgroundBrush = CreateSignalBrush(SignalVisualIntensity, isLong: true);
            return;
        }

        if (signal == SignalState.ShortCandidate)
        {
            SignalVisualDirection = "Short";
            SignalVisualIntensity = CalculateSignalIntensity(zOfi, tfi, direction: -1);
            SignalBackgroundBrush = CreateSignalBrush(SignalVisualIntensity, isLong: false);
            return;
        }

        SignalVisualDirection = "Neutral";
        SignalVisualIntensity = 0d;
        SignalBackgroundBrush = Brushes.White;
    }

    private double CalculateSignalIntensity(decimal zOfi, decimal tfi, int direction)
    {
        var thetaZ = Math.Max(0.0001m, Options.Indicator.ThetaZ);
        var thetaTfi = Math.Max(0.0001m, Options.Indicator.ThetaTfi);
        var zComponent = Math.Abs(zOfi) / thetaZ;
        var tfiComponent = Math.Sign(tfi) == direction
            ? Math.Abs(tfi) / thetaTfi
            : 0m;
        var intensity = (double)((zComponent * 0.65m) + (tfiComponent * 0.35m));

        return Math.Clamp(intensity, 0d, 1d);
    }

    private static Brush CreateSignalBrush(double intensity, bool isLong)
    {
        var target = isLong
            ? Color.FromRgb(34, 197, 94)
            : Color.FromRgb(239, 68, 68);
        var mix = Math.Clamp(0.12d + (intensity * 0.58d), 0d, 0.7d);
        var color = Blend(Color.FromRgb(255, 255, 255), target, mix);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        static byte Channel(byte a, byte b, double amount)
        {
            return (byte)Math.Round(a + ((b - a) * amount));
        }

        return Color.FromRgb(
            Channel(from.R, to.R, amount),
            Channel(from.G, to.G, amount),
            Channel(from.B, to.B, amount));
    }

    private string ResolveRecordingPath(string symbol)
    {
        if (string.IsNullOrWhiteSpace(Options.RecordingPath))
        {
            return Path.Combine("recordings", $"{symbol.ToLowerInvariant()}.jsonl");
        }

        return Options.RecordingPath.Replace(
            "{symbol}",
            symbol.ToLowerInvariant(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSymbol(string? symbol)
    {
        return string.IsNullOrWhiteSpace(symbol)
            ? string.Empty
            : symbol.Trim().ToUpperInvariant();
    }

    private void NotifyCommandStateChanged()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanRestart));
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
