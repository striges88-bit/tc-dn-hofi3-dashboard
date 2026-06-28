using CryptoIndicatorApp.Infrastructure.Binance;
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.LiveDryRun;

public sealed class DryRunOptions
{
    public string Symbol { get; private init; } = "BTCUSDT";

    public TimeSpan Duration { get; private init; } = TimeSpan.FromSeconds(10);

    public string OutputPath { get; private init; } = string.Empty;

    public string InputPath { get; private init; } = string.Empty;

    public bool ReplayOnly { get; private init; }

    public bool ContextOnly { get; private init; }

    public ContextFrame ContextFrame { get; private init; } = ContextFrame.FifteenMinutes;

    public int OpenInterestHistoryLimit { get; private init; } = 288;

    public BinanceProxyOptions Proxy { get; private init; } = new();

    public static DryRunOptions Parse(string[] args)
    {
        var values = ParseArguments(args);
        var symbol = GetValue(values, "symbol", "BTCUSDT").Trim().ToUpperInvariant();
        var seconds = ParsePositiveInt(GetValue(values, "seconds", "10"), "seconds");
        var outputPath = GetValue(
            values,
            "output",
            Path.Combine("recordings", $"live-dry-run-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.jsonl"));
        var inputPath = GetValue(values, "input", string.Empty);
        var replayOnly = ParseBool(GetValue(values, "replay-only", "false"), "replay-only")
            || !string.IsNullOrWhiteSpace(inputPath);
        var contextOnly = ParseBool(GetValue(values, "context-only", "false"), "context-only");

        if (replayOnly && string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Replay-only mode requires --input path.");
        }

        var proxyEnabled = ParseBool(GetValue(
            values,
            "proxy-enabled",
            Environment.GetEnvironmentVariable("TC_PROXY_ENABLED") ?? "false"),
            "proxy-enabled");

        var proxy = new BinanceProxyOptions
        {
            Enabled = proxyEnabled,
            Type = GetValue(values, "proxy-type", Environment.GetEnvironmentVariable("TC_PROXY_TYPE") ?? "Http"),
            Host = GetValue(values, "proxy-host", Environment.GetEnvironmentVariable("TC_PROXY_HOST") ?? string.Empty),
            Port = ParseNonNegativeInt(GetValue(values, "proxy-port", Environment.GetEnvironmentVariable("TC_PROXY_PORT") ?? "0"), "proxy-port")
        };

        return new DryRunOptions
        {
            Symbol = symbol,
            Duration = TimeSpan.FromSeconds(seconds),
            OutputPath = Path.GetFullPath(outputPath),
            InputPath = string.IsNullOrWhiteSpace(inputPath) ? string.Empty : Path.GetFullPath(inputPath),
            ReplayOnly = replayOnly,
            ContextOnly = contextOnly,
            ContextFrame = ParseContextFrame(GetValue(values, "frame", "15m")),
            OpenInterestHistoryLimit = ParsePositiveInt(GetValue(values, "oi-limit", "288"), "oi-limit"),
            Proxy = proxy
        };
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{argument}'. Use --name value pairs.");
            }

            var name = argument[2..];
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Empty option name.");
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[name] = "true";
                continue;
            }

            values[name] = args[++index];
        }

        return values;
    }

    private static string GetValue(Dictionary<string, string> values, string name, string fallback)
    {
        return values.TryGetValue(name, out var value)
            ? value
            : fallback;
    }

    private static int ParsePositiveInt(string value, string name)
    {
        if (!int.TryParse(value, out var parsed) || parsed <= 0)
        {
            throw new ArgumentException($"Option '{name}' must be a positive integer.");
        }

        return parsed;
    }

    private static int ParseNonNegativeInt(string value, string name)
    {
        if (!int.TryParse(value, out var parsed) || parsed < 0)
        {
            throw new ArgumentException($"Option '{name}' must be a non-negative integer.");
        }

        return parsed;
    }

    private static bool ParseBool(string value, string name)
    {
        if (!bool.TryParse(value, out var parsed))
        {
            throw new ArgumentException($"Option '{name}' must be true or false.");
        }

        return parsed;
    }

    private static ContextFrame ParseContextFrame(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "5m" or "5" or "fiveminutes" => ContextFrame.FiveMinutes,
            "15m" or "15" or "fifteenminutes" => ContextFrame.FifteenMinutes,
            _ => throw new ArgumentException("Option 'frame' must be 5m or 15m.")
        };
    }
}
