using System.Text;
using System.Text.Json;
using CryptoIndicatorApp.PilotB;

namespace CryptoIndicatorApp.PilotB.Tests;

public sealed partial class PilotBRunFingerprintWriterTests
{
    [Fact]
    public void FixtureProjection_NormalizesSetAndIsIdempotentAtCanonicalInput()
    {
        var a = new string('a', 64);
        var b = new string('b', 64);
        PilotBFileManifestEntry[] canonical = [new("a/item.txt", 2, a), new("z.txt", 1, b)];
        PilotBFileManifestEntry[] equivalent = [new("z.txt", 1, b.ToUpperInvariant()), new(@"a\item.txt", 2, a.ToUpperInvariant())];
        var expectedBytes = "{\"schema_version\":\"pilot-b.fixture-semantic.v3\",\"files\":[{\"path\":\"a/item.txt\",\"length\":2,\"sha256\":\"" + a
            + "\"},{\"path\":\"z.txt\",\"length\":1,\"sha256\":\"" + b + "\"}]}";
        var expected = PilotBSha256.Compute(expectedBytes);
        Assert.Equal(expected, PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(equivalent));
        Assert.Equal(expected, PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(canonical));

        var manifest = new PilotBFileManifest(@"C:\storage\one", canonical,
            PilotBSha256.Compute(JsonSerializer.Serialize(canonical)));
        var first = PilotBFileManifest.Parse(Encoding.UTF8.GetBytes(manifest.ToJson()));
        var second = PilotBFileManifest.Parse(Encoding.UTF8.GetBytes(first.ToJson()));
        Assert.Equal(first.ToJson(), second.ToJson());
        Assert.Equal(expected, PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(second));
        Assert.Equal(expected, PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(second with { Root = @"D:\storage\two" }));
        Assert.NotEqual(expected, PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256([canonical[0] with { Length = 3 }, canonical[1]]));
        Assert.NotEqual(expected, PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256([canonical[0] with { RelativePath = "a/other.txt" }, canonical[1]]));
        Assert.NotEqual(expected, PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256([canonical[0] with { Sha256 = b }, canonical[1]]));
    }

    [Theory]
    [InlineData("executable")]
    [InlineData("prompt")]
    [InlineData("arm")]
    [InlineData("cli")]
    [InlineData("protocol")]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("sandbox")]
    [InlineData("approval")]
    [InlineData("global-instructions")]
    [InlineData("project-instructions")]
    [InlineData("skills")]
    [InlineData("pre-fixture")]
    [InlineData("post-fixture")]
    [InlineData("qualification-marker")]
    [InlineData("qualification-result")]
    [InlineData("exit")]
    [InlineData("timeout")]
    public void Fingerprint_ChangesForEachProtocolInputAndQualification(string field)
    {
        var original = CreateProofInput();
        var changedHash = new string('9', 64);
        var changed = field switch
        {
            "executable" => original with { ExecutableSha256 = changedHash },
            "prompt" => original with { PromptSha256 = changedHash },
            "arm" => original with { Manifest = original.Manifest with { ArmId = "control" } },
            "cli" => original with { Manifest = original.Manifest with { CliVersion = "codex-2.0.0" } },
            "protocol" => original with { Manifest = original.Manifest with { ProtocolSha256 = changedHash } },
            "model" => original with { Manifest = original.Manifest with { ModelAlias = "model-b" } },
            "reasoning" => original with { Manifest = original.Manifest with { ReasoningEffort = "high" } },
            "sandbox" => original with { Manifest = original.Manifest with { Sandbox = "read-only" } },
            "approval" => original with { Manifest = original.Manifest with { ApprovalPolicy = "on-request" } },
            "global-instructions" => original with { Manifest = original.Manifest with { GlobalInstructionsSha256 = changedHash } },
            "project-instructions" => original with { Manifest = original.Manifest with { ProjectInstructionsSha256 = changedHash } },
            "skills" => original with { Manifest = original.Manifest with { SkillsManifestSha256 = changedHash } },
            "pre-fixture" => original with { PreFixtureSemanticSha256 = changedHash },
            "post-fixture" => original with { PostFixtureSemanticSha256 = changedHash },
            "qualification-marker" => original with { IsQualification = true },
            "qualification-result" => original with { Qualification = ProofQualification(original.Transcript, executableHashValid: false) },
            "exit" => original with { ExitCode = 23, Qualification = ProofQualification(original.Transcript, exitCode: 23) },
            "timeout" => original with { TimedOut = true, Qualification = ProofQualification(original.Transcript, timedOut: true) },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        Assert.NotEqual(PilotBRunFingerprintWriter.Compute(original), PilotBRunFingerprintWriter.Compute(changed));
        Assert.Equal(PilotBRunFingerprintWriter.Compute(original), PilotBRunFingerprintWriter.Compute(CreateProofInput()));
    }

    [Fact]
    public void Fingerprint_NormalizesUppercaseHashesWithoutChangingGoldenShape()
    {
        var input = CreateProofInput();
        var uppercase = input with
        {
            ExecutableSha256 = input.ExecutableSha256.ToUpperInvariant(),
            PromptSha256 = input.PromptSha256.ToUpperInvariant(),
            Manifest = input.Manifest with
            {
                ProtocolSha256 = input.Manifest.ProtocolSha256.ToUpperInvariant(),
                GlobalInstructionsSha256 = input.Manifest.GlobalInstructionsSha256.ToUpperInvariant(),
                ProjectInstructionsSha256 = input.Manifest.ProjectInstructionsSha256.ToUpperInvariant(),
                SkillsManifestSha256 = input.Manifest.SkillsManifestSha256.ToUpperInvariant()
            }
        };
        Assert.Equal(PilotBRunFingerprintWriter.Write(input), PilotBRunFingerprintWriter.Write(uppercase));
    }

    private static PilotBRunFingerprintInput CreateProofInput()
    {
        var transcript = PilotBTranscriptParser.Parse("""
            {"type":"thread.started","thread_id":"proof"}
            {"type":"turn.started"}
            {"type":"item.completed","item":{"type":"agent_message","phase":"final","text":"Exact result."}}
            {"type":"turn.completed"}
            """);
        Assert.True(transcript.IsValid);
        return new(new string('a', 64), new string('b', 64), CreateSemanticManifest("proof", @"C:\storage"),
            transcript, new string('1', 64), new string('2', 64), false,
            ProofQualification(transcript), 0, false);
    }

    private static PilotBRunQualificationResult ProofQualification(
        PilotBTranscriptParseResult transcript, bool executableHashValid = true, int exitCode = 0, bool timedOut = false)
        => PilotBRunQualification.Evaluate(new(true, exitCode, timedOut, transcript,
            !timedOut, executableHashValid, true, true, true, true, []));
}
