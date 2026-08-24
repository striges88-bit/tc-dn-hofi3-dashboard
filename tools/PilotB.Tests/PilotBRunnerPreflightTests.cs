using CryptoIndicatorApp.PilotB;
using PilotBRunnerPreflightFixture = CryptoIndicatorApp.PilotB.Tests.PilotBRunnerTestFixture;

namespace CryptoIndicatorApp.PilotB.Tests;

public sealed class PilotBRunnerPreflightTests
{
    [Fact]
    public async Task ImmutablePrerequisiteFailure_ThrowsTypedExceptionWithoutCreatingEvidence()
    {
        using var fixture = PilotBRunnerPreflightFixture.Create();
        var request = fixture.CreateRequest() with { ExecutablePath = "fake.exe" };

        var exception = await Assert.ThrowsAsync<PilotBPreflightException>(
            () => new PilotBRunner().RunAsync(request));

        Assert.Equal([PilotBPreflightReasonCodes.ExecutableNotAbsolute], exception.ReasonCodes);
        Assert.False(File.Exists(request.ArtifactDirectory));
        Assert.False(Directory.Exists(request.ArtifactDirectory));
    }

    [Theory]
    [InlineData("empty-directory")]
    [InlineData("nonempty-directory")]
    [InlineData("file")]
    [InlineData("directory-reparse")]
    public async Task InitialArtifactPathExists_IsRejectedWithoutMutation(string pathKind)
    {
        using var fixture = PilotBRunnerPreflightFixture.Create();
        var request = fixture.CreateRequest();
        var sentinelPath = fixture.CreateExistingArtifactPath(request.ArtifactDirectory, pathKind);

        var exception = await Assert.ThrowsAsync<PilotBPreflightException>(
            () => new PilotBRunner().RunAsync(request));

        Assert.Equal([PilotBPreflightReasonCodes.ArtifactPathAlreadyExists], exception.ReasonCodes);
        Assert.True(File.Exists(request.ArtifactDirectory) || Directory.Exists(request.ArtifactDirectory));
        if (sentinelPath is not null)
        {
            Assert.Equal("sentinel", File.ReadAllText(sentinelPath));
        }
        if (pathKind == "directory-reparse")
        {
            Assert.True((File.GetAttributes(request.ArtifactDirectory) & FileAttributes.ReparsePoint) != 0);
        }
    }

    [Theory]
    [InlineData("CONTROL")]
    [InlineData("Treatment")]
    [InlineData(" treatment ")]
    public async Task ArmIdRequiresExactLowercaseWireValue(string armId)
    {
        using var fixture = PilotBRunnerPreflightFixture.Create();
        fixture.ReplaceManifestValue("\"arm_id\": \"treatment\"", $"\"arm_id\": \"{armId}\"");
        var request = fixture.CreateRequest();

        var exception = await Assert.ThrowsAsync<PilotBPreflightException>(
            () => new PilotBRunner().RunAsync(request));

        Assert.Equal([PilotBPreflightReasonCodes.InvalidArmId], exception.ReasonCodes);
        Assert.False(Directory.Exists(request.ArtifactDirectory));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\\t")]
    public async Task RepositoryRootRejectsEmptyOrWhitespaceValue(string repositoryRoot)
    {
        using var fixture = PilotBRunnerPreflightFixture.Create();
        var escapedFixtureRoot = fixture.FixtureRoot.Replace("\\", "\\\\");
        fixture.ReplaceManifestValue(
            $"\"repository_root\": \"{escapedFixtureRoot}\"",
            $"\"repository_root\": \"{repositoryRoot}\"");
        var request = fixture.CreateRequest();

        var exception = await Assert.ThrowsAsync<PilotBPreflightException>(
            () => new PilotBRunner().RunAsync(request));

        Assert.Equal([PilotBPreflightReasonCodes.MalformedManifest], exception.ReasonCodes);
        Assert.False(Directory.Exists(request.ArtifactDirectory));
    }

    [Fact]
    public async Task OwnershipAcquire_PostPreflightRace_AllowsOneWriterAndRejectsLoser()
    {
        using var fixture = PilotBRunnerPreflightFixture.Create();
        var artifactRoot = Path.Combine(fixture.Root, "contended-artifacts");
        var lockPath = Path.Combine(artifactRoot, ".pilot-b-write-lock");
        using var start = new Barrier(2);
        var conflictObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var outcomes = await Task.WhenAll(
            Task.Run(ContendAsync),
            Task.Run(ContendAsync));

        Assert.Single(outcomes, outcome => outcome.Acquired);
        var loser = Assert.Single(outcomes, outcome => outcome.Exception is not null).Exception!;
        Assert.Equal([PilotBPreflightReasonCodes.ArtifactOwnershipConflict], loser.ReasonCodes);
        Assert.IsAssignableFrom<IOException>(loser.InnerException);
        Assert.False(File.Exists(lockPath));

        async Task<OwnershipOutcome> ContendAsync()
        {
            start.SignalAndWait();
            FileStream? ownership = null;
            try
            {
                ownership = PilotBArtifactOwnership.Acquire(artifactRoot);
                await conflictObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
                return new OwnershipOutcome(true, null);
            }
            catch (PilotBPreflightException exception)
            {
                conflictObserved.TrySetResult(true);
                return new OwnershipOutcome(false, exception);
            }
            finally
            {
                if (ownership is not null)
                {
                    ownership.Dispose();
                    File.Delete(lockPath);
                }
            }
        }
    }

    [Fact]
    public async Task Runner_PostPreflightRace_StartsOnlyWinnerAndPreservesPublishedEvidence()
    {
        using var fixture = PilotBRunnerPreflightFixture.Create();
        using var bothPreflightComplete = new CountdownEvent(2);
        var releaseOwnership = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = fixture.CreateRequest("pilot-b.fake.delayed-valid") with
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
        var runner = new PilotBRunner(BeforeOwnershipAsync);

        var first = ObserveRunnerAsync(runner, request);
        var second = ObserveRunnerAsync(runner, request);
        Assert.True(bothPreflightComplete.Wait(TimeSpan.FromSeconds(2)));
        releaseOwnership.SetResult(true);
        var outcomes = await Task.WhenAll(first, second);

        var winner = Assert.Single(outcomes, outcome => outcome.Result is not null).Result!;
        var loser = Assert.Single(outcomes, outcome => outcome.Exception is not null).Exception!;
        Assert.Equal(PilotBEvidenceState.Sealed, winner.EvidenceState);
        Assert.Equal(PilotBRunValidity.Valid, winner.RunValidity);
        Assert.Equal([PilotBPreflightReasonCodes.ArtifactOwnershipConflict], loser.ReasonCodes);
        Assert.IsAssignableFrom<IOException>(loser.InnerException);
        var verification = new PilotBEvidenceBundleVerifier().Verify(winner.Artifacts);
        Assert.Equal(PilotBEvidenceState.Sealed, verification.EvidenceState);
        Assert.False(File.Exists(winner.Artifacts.LockPath));

        Task BeforeOwnershipAsync()
        {
            bothPreflightComplete.Signal();
            return releaseOwnership.Task;
        }
    }

    [Fact]
    public void OwnershipAcquire_LateContenderDoesNotReuseOrCleanPublishedDirectory()
    {
        using var fixture = PilotBRunnerPreflightFixture.Create();
        var artifactRoot = Path.Combine(fixture.Root, "published-artifacts");
        Directory.CreateDirectory(artifactRoot);
        var sentinelPath = Path.Combine(artifactRoot, "integrity.json");
        File.WriteAllText(sentinelPath, "sentinel");

        var exception = Assert.Throws<PilotBPreflightException>(
            () => PilotBArtifactOwnership.Acquire(artifactRoot));

        Assert.Equal([PilotBPreflightReasonCodes.ArtifactOwnershipConflict], exception.ReasonCodes);
        Assert.IsAssignableFrom<IOException>(exception.InnerException);
        Assert.Equal("sentinel", File.ReadAllText(sentinelPath));
        Assert.False(File.Exists(Path.Combine(artifactRoot, ".pilot-b-write-lock")));
    }

    private static async Task<RunnerOutcome> ObserveRunnerAsync(PilotBRunner runner, PilotBRunnerOptions request)
    {
        try
        {
            return new RunnerOutcome(await runner.RunAsync(request), null);
        }
        catch (PilotBPreflightException exception)
        {
            return new RunnerOutcome(null, exception);
        }
    }

    private sealed record OwnershipOutcome(bool Acquired, PilotBPreflightException? Exception);
    private sealed record RunnerOutcome(PilotBRunnerResult? Result, PilotBPreflightException? Exception);
}
