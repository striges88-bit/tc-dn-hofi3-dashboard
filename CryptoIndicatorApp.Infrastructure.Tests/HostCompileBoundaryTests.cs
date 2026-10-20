using System.Xml.Linq;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class HostCompileBoundaryTests
{
    [Fact]
    public void Desktop_project_does_not_compile_against_Binance_Net_or_dependency_injection()
    {
        var desktop = LoadProject("CryptoIndicatorApp.Desktop/CryptoIndicatorApp.Desktop.csproj");
        var packages = PackageIncludes(desktop);

        Assert.DoesNotContain("Binance.Net", packages);
        Assert.DoesNotContain("Microsoft.Extensions.DependencyInjection", packages);
    }

    [Fact]
    public void Infrastructure_keeps_Binance_Net_compile_assets_private()
    {
        var infrastructure = LoadProject("CryptoIndicatorApp.Infrastructure/CryptoIndicatorApp.Infrastructure.csproj");
        var package = infrastructure
            .Descendants("PackageReference")
            .Single(element => element.Attribute("Include")?.Value == "Binance.Net");

        Assert.Equal("compile", package.Element("PrivateAssets")?.Value);
        Assert.DoesNotContain("System.Text.Json", PackageIncludes(infrastructure));
        Assert.DoesNotContain("System.Threading.Channels", PackageIncludes(infrastructure));
    }

    private static XDocument LoadProject(string relativePath)
    {
        return XDocument.Load(Path.Combine(FindRepositoryRoot(), relativePath));
    }

    private static IReadOnlyList<string> PackageIncludes(XDocument project)
    {
        return project
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();
    }

    private static string FindRepositoryRoot()
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

        throw new InvalidOperationException("Repository root was not found from the test output directory.");
    }
}
