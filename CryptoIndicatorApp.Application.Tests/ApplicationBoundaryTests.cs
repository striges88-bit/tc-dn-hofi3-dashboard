using System.Reflection;
using CryptoIndicatorApp.Application.Sessions;
using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Application.Tests;

public class ApplicationBoundaryTests
{
    private static readonly HashSet<string> FrameworkAssemblies = LoadFrameworkAssemblies();

    [Fact]
    public void ApplicationAssemblyDoesNotReferenceInfrastructure()
    {
        var references = typeof(ReplayIndicatorSession)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.DoesNotContain("CryptoIndicatorApp.Infrastructure", references);
    }

    [Fact]
    public void DomainAssemblyReferencesOnlyTheFramework()
    {
        Assert.Empty(UnexpectedReferences(typeof(DepthUpdateEvent).Assembly));
    }

    [Fact]
    public void ApplicationAssemblyReferencesOnlyDomainAndTheFramework()
    {
        Assert.Empty(UnexpectedReferences(typeof(ReplayIndicatorSession).Assembly, "CryptoIndicatorApp.Domain"));
    }

    private static string[] UnexpectedReferences(Assembly assembly, string? allowedProject = null)
    {
        return assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => !FrameworkAssemblies.Contains(name)
                && name != "netstandard"
                && name != allowedProject)
            .ToArray();
    }

    private static HashSet<string> LoadFrameworkAssemblies()
    {
        var frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException("Shared framework directory was not found.");

        return Directory
            .EnumerateFiles(frameworkDirectory, "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
