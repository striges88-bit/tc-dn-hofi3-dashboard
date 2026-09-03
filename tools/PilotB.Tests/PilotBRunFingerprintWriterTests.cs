using System.Text;
using CryptoIndicatorApp.PilotB;

namespace CryptoIndicatorApp.PilotB.Tests;

public sealed class PilotBRunFingerprintWriterTests
{
    [Fact]
    public void Write_EmitsVersionedGoldenBytesIdempotently()
    {
        var manifest = CreateSemanticManifest("manifest-a", @"C:\storage\a");
        var equivalentManifest = CreateSemanticManifest("manifest-b", @"D:\storage\b");
        var transcript = PilotBTranscriptParser.Parse("""
            {"type":"thread.started","thread_id":"thread-a"}
            {"type":"turn.started"}
            {"type":"item.completed","item":{"type":"agent_message","phase":"final","text":"Final exact."}}
            {"type":"item.completed","item":{"type":"agent_message","phase":"commentary","text":"Commentary exact."}}
            {"type":"turn.completed"}
            """);
        var equivalentTranscript = PilotBTranscriptParser.Parse("""
            { "thread_id" : "thread-b", "type" : "thread.started" }
            { "type" : "turn.started" }
            { "item" : { "phase" : "final", "text" : "Final exact.", "type" : "agent_message" }, "type" : "item.completed" }
            { "item" : { "phase" : "commentary", "text" : "Commentary exact.", "type" : "agent_message" }, "type" : "item.completed" }
            { "type" : "turn.completed" }
            """);
        var input = new PilotBRunFingerprintInput(
            new string('a', 64),
            new string('b', 64),
            manifest,
            transcript,
            new string('1', 64),
            new string('2', 64),
            IsQualification: false,
            new PilotBRunQualificationResult(PilotBRunValidity.Valid, []),
            ExitCode: 0,
            TimedOut: false);
        const string expected = "{\"schema_version\":\"pilot-b.run-fingerprint.v3\",\"executable_sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"prompt_sha256\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"semantic_arm_manifest\":{\"projection_version\":\"pilot-b.semantic-arm-manifest.v3\",\"arm_id\":\"treatment\",\"cli_version\":\"codex-1.2.3\",\"protocol_sha256\":\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\",\"model_alias\":\"gpt-5.6-sol\",\"reasoning_effort\":\"max\",\"sandbox\":\"native-windows\",\"approval_policy\":\"never\",\"global_instructions_sha256\":\"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd\",\"project_instructions_sha256\":\"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\",\"skills_manifest_sha256\":\"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff\"},\"semantic_transcript\":{\"projection_version\":\"pilot-b.semantic-transcript.v3\",\"messages\":[{\"sequence\":1,\"text\":\"Final exact.\",\"phase\":\"final\"},{\"sequence\":2,\"text\":\"Commentary exact.\",\"phase\":\"commentary\"}],\"terminal_outcome\":\"success\",\"valid\":true,\"invalid_reasons\":[]},\"pre_fixture_semantic_sha256\":\"1111111111111111111111111111111111111111111111111111111111111111\",\"post_fixture_semantic_sha256\":\"2222222222222222222222222222222222222222222222222222222222222222\",\"qualification_marker\":false,\"run_validity\":\"valid\",\"invalid_reasons\":[],\"exit_code\":0,\"timed_out\":false}";

        var first = PilotBRunFingerprintWriter.Write(input);
        var second = PilotBRunFingerprintWriter.Write(input);
        var equivalent = PilotBRunFingerprintWriter.Write(input with
        {
            Manifest = equivalentManifest,
            Transcript = equivalentTranscript
        });
        var semanticOutputChanged = PilotBRunFingerprintWriter.Write(input with
        {
            Transcript = PilotBTranscriptParser.Parse("""
                {"type":"thread.started","thread_id":"thread-c"}
                {"type":"turn.started"}
                {"type":"item.completed","item":{"type":"agent_message","phase":"final","text":"Different final."}}
                {"type":"item.completed","item":{"type":"agent_message","phase":"commentary","text":"Commentary exact."}}
                {"type":"turn.completed"}
                """)
        });

        Assert.Equal(expected, Encoding.UTF8.GetString(first));
        Assert.True(first.AsSpan().SequenceEqual(second));
        Assert.True(first.AsSpan().SequenceEqual(equivalent));
        Assert.False(first.AsSpan().SequenceEqual(semanticOutputChanged));
    }

    private static PilotBArmManifest CreateSemanticManifest(string manifestId, string repositoryRoot)
        => new(
            manifestId,
            "treatment",
            "codex-1.2.3",
            new string('c', 64),
            "gpt-5.6-sol",
            "max",
            "native-windows",
            "never",
            repositoryRoot,
            new string('d', 64),
            new string('e', 64),
            new string('f', 64),
            MutableAuthenticationLanePresent: false);
}
