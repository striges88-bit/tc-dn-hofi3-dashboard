using System.Text;
using System.Text.Json.Nodes;
using CryptoIndicatorApp.PilotB;

namespace CryptoIndicatorApp.PilotB.Tests;

[Collection(PilotBProcessBackedRunnerCollection.Name)]
public sealed class PilotBFixtureRootConsistencyTests
{
    [Theory]
    [InlineData("pre-manifest.json")]
    [InlineData("post-manifest.json")]
    public async Task Verifier_RejectsFixtureRootMismatchEvenWithRehashedInventory(string payloadName)
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest());
        Assert.Equal(PilotBEvidenceState.Sealed, result.EvidenceState);
        Assert.Equal(PilotBRunValidity.Valid, result.RunValidity);

        var path = Path.Combine(result.Artifacts.Root, payloadName);
        var original = PilotBFileManifest.Parse(await File.ReadAllBytesAsync(path));
        await RewriteRootAndInventoryAsync(result.Artifacts, payloadName, fixture.FixtureRoot + "-other");
        var changed = PilotBFileManifest.Parse(await File.ReadAllBytesAsync(path));
        Assert.Equal(PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(original),
            PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(changed));

        var verification = new PilotBEvidenceBundleVerifier().Verify(result.Artifacts);
        Assert.Equal(PilotBEvidenceState.Unsealed, verification.EvidenceState);
        Assert.Contains("fixture-root-mismatch", verification.InvalidReasons);
        Assert.Null(verification.Qualification);
        Assert.Null(verification.SemanticFingerprint);
    }

    [Theory]
    [InlineData("pre-manifest.json")]
    [InlineData("post-manifest.json")]
    public async Task Verifier_AcceptsNormalizedFixtureRootWithoutChangingFingerprint(string payloadName)
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest());
        Assert.Equal(PilotBEvidenceState.Sealed, result.EvidenceState);
        var equivalentRoot = Path.Combine(fixture.FixtureRoot, ".") + Path.DirectorySeparatorChar;
        await RewriteRootAndInventoryAsync(result.Artifacts, payloadName, equivalentRoot);

        var verification = new PilotBEvidenceBundleVerifier().Verify(result.Artifacts);
        Assert.Equal(PilotBEvidenceState.Sealed, verification.EvidenceState);
        Assert.Equal(PilotBRunValidity.Valid, verification.Qualification!.Validity);
        Assert.Equal(result.DeterministicFingerprint, verification.SemanticFingerprint);
    }

    private static async Task RewriteRootAndInventoryAsync(
        PilotBArtifactPaths paths, string payloadName, string root)
    {
        var path = Path.Combine(paths.Root, payloadName);
        var manifest = JsonNode.Parse(await File.ReadAllBytesAsync(path))!;
        manifest["root"] = root;
        var bytes = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        await File.WriteAllBytesAsync(path, bytes);
        var seal = JsonNode.Parse(await File.ReadAllBytesAsync(paths.SealPath))!;
        var entry = seal["payload_inventory"]!.AsArray().Single(
            item => item!["path"]!.GetValue<string>() == payloadName)!;
        entry["length"] = bytes.Length;
        entry["sha256"] = PilotBSha256.Compute(bytes);
        await File.WriteAllTextAsync(paths.SealPath, seal.ToJsonString());
    }
}
