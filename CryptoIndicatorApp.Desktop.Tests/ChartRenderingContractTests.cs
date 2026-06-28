using System.IO;

namespace CryptoIndicatorApp.Desktop.Tests;

public sealed class ChartRenderingContractTests
{
    [Fact]
    public void Chart_uses_raw_tfi_overlay_contract()
    {
        var workspace = FindWorkspaceRoot();
        var xaml = File.ReadAllText(Path.Combine(workspace, "CryptoIndicatorApp.Desktop", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(workspace, "CryptoIndicatorApp.Desktop", "MainWindow.xaml.cs"));

        Assert.Contains("Text=\"TFI\"", xaml);
        Assert.DoesNotContain("TFI/Theta", xaml);
        Assert.DoesNotContain("ToTfiConfirmationStrength", codeBehind);
    }

    [Fact]
    public void Chart_includes_neutral_zero_line_contract()
    {
        var workspace = FindWorkspaceRoot();
        var xaml = File.ReadAllText(Path.Combine(workspace, "CryptoIndicatorApp.Desktop", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(workspace, "CryptoIndicatorApp.Desktop", "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"ZeroLine\"", xaml);
        Assert.Contains("Stroke=\"#CBD5E1\"", xaml);
        Assert.Contains("BuildZeroLine", codeBehind);
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CryptoIndicatorApp.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find workspace root.");
    }
}
