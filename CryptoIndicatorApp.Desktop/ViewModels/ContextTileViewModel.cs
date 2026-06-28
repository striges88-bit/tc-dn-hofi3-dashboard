using System.Globalization;
using System.Windows.Media;
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Desktop.ViewModels;

public sealed record ContextTileViewModel(
    string Label,
    string ValueText,
    ContextDirection Direction,
    double Intensity,
    Brush BackgroundBrush)
{
    public static ContextTileViewModel FromTile(ContextTile tile)
    {
        var label = tile.BucketStart.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        var value = tile.NormalizedDelta is null
            ? tile.Status
            : tile.NormalizedDelta.Value.ToString("0.0000%", CultureInfo.InvariantCulture);

        return new ContextTileViewModel(
            label,
            value,
            tile.Direction,
            tile.Intensity,
            CreateBrush(tile.Direction, tile.Intensity));
    }

    private static Brush CreateBrush(ContextDirection direction, double intensity)
    {
        var target = direction == ContextDirection.Positive
            ? Color.FromRgb(34, 197, 94)
            : direction == ContextDirection.Negative
                ? Color.FromRgb(239, 68, 68)
                : Color.FromRgb(229, 231, 235);
        var amount = direction is ContextDirection.Positive or ContextDirection.Negative
            ? Math.Clamp(0.12d + (intensity * 0.68d), 0d, 0.8d)
            : 0.4d;
        var brush = new SolidColorBrush(Blend(Color.FromRgb(255, 255, 255), target, amount));
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
}
