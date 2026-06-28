using CryptoIndicatorApp.Application.Sessions;

namespace CryptoIndicatorApp.Application.Tests;

public class ApplicationBoundaryTests
{
    [Fact]
    public void ApplicationAssemblyDoesNotReferenceInfrastructure()
    {
        var references = typeof(ReplayIndicatorSession)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.DoesNotContain("CryptoIndicatorApp.Infrastructure", references);
    }
}
