using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CryptoIndicatorApp.PilotB;

public sealed class PilotBRunner
{
    private static readonly IReadOnlyList<string> ExactInvocation =
        ["codex", "exec", "--ephemeral", "--json"];

    public async Task<PilotBRunnerResult> RunAsync(
        PilotBRunnerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var preflight = await PreflightAsync(options, cancellationToken);
        if (preflight.Result is not null)
        {
            return preflight.Result;
        }

        var manifest = preflight.Manifest!;
        var fixtureRoot = preflight.FixtureRoot!;
        var artifactRoot = preflight.ArtifactRoot!;
        var executablePath = preflight.ExecutablePath!;
        var artifactPaths = CreateArtifactPaths(artifactRoot);
        var invalidReasons = new List<string>();
        var startedAt = options.UtcNowProvider();
        var stopwatch = Stopwatch.StartNew();
        var timedOut = false;
        int? exitCode = null;
        byte[] stdoutBytes = [];
        byte[] stderrBytes = [];
        PilotBFileManifest? preManifest = null;
        PilotBFileManifest? postManifest = null;
        var processStarted = false;

        try
        {
            Directory.CreateDirectory(artifactRoot);
            preManifest = PilotBFileManifest.Capture(fixtureRoot);
            await File.WriteAllBytesAsync(artifactPaths.PromptPath, options.PromptBytes, cancellationToken);
            await File.WriteAllTextAsync(artifactPaths.ManifestPath, manifest.ToSanitizedJson(), Encoding.UTF8, cancellationToken);
            await File.WriteAllTextAsync(artifactPaths.PreManifestPath, preManifest.ToJson(), Encoding.UTF8, cancellationToken);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    WorkingDirectory = fixtureRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            foreach (var argument in ExactInvocation)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                invalidReasons.Add("process-start-failed");
            }
            else
            {
                processStarted = true;
                var outputBuffer = new MemoryStream();
                var errorBuffer = new MemoryStream();
                var outputTask = process.StandardOutput.BaseStream.CopyToAsync(outputBuffer, cancellationToken);
                var errorTask = process.StandardError.BaseStream.CopyToAsync(errorBuffer, cancellationToken);

                try
                {
                    await process.StandardInput.BaseStream.WriteAsync(options.PromptBytes, cancellationToken);
                    process.StandardInput.Close();
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    invalidReasons.Add("stdin-write-failed");
                }

                var waitTask = process.WaitForExitAsync(cancellationToken);
                var timeoutTask = Task.Delay(options.Timeout, cancellationToken);
                if (await Task.WhenAny(waitTask, timeoutTask) != waitTask)
                {
                    timedOut = true;
                    invalidReasons.Add("timeout");
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }

                try
                {
                    await waitTask;
                }
                catch (InvalidOperationException)
                {
                }

                try
                {
                    await Task.WhenAll(outputTask, errorTask);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    invalidReasons.Add("output-capture-failed");
                }

                stdoutBytes = outputBuffer.ToArray();
                stderrBytes = errorBuffer.ToArray();
                if (!timedOut)
                {
                    exitCode = process.ExitCode;
                }
            }

            postManifest = PilotBFileManifest.Capture(fixtureRoot);
            await File.WriteAllTextAsync(artifactPaths.PostManifestPath, postManifest.ToJson(), Encoding.UTF8, cancellationToken);
            await File.WriteAllBytesAsync(artifactPaths.RawOutputPath, stdoutBytes, cancellationToken);
            await File.WriteAllBytesAsync(artifactPaths.StderrPath, stderrBytes, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            invalidReasons.Add("artifact-or-boundary-capture-failed");
        }

        var transcript = PilotBTranscriptParser.Parse(stdoutBytes);
        if (processStarted && exitCode is not 0)
        {
            invalidReasons.Add("nonzero-exit");
        }

        if (!transcript.IsValid)
        {
            foreach (var reason in transcript.InvalidReasons)
            {
                invalidReasons.Add(reason == "missing-turn-completed" ? "partial-run" : reason);
            }
        }

        if (transcript.HasTurnFailed)
        {
            invalidReasons.Add("failed-event");
        }

        var endAt = options.UtcNowProvider();
        var timingValid = !timedOut && stopwatch.Elapsed <= options.Timeout;
        if (!timingValid)
        {
            invalidReasons.Add("timing-violation");
        }

        var executableSha = SafeComputeFileHash(executablePath, invalidReasons, "executable-read-failed");
        if (!string.Equals(executableSha, preflight.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
        {
            invalidReasons.Add("executable-drift");
        }

        var preSha = preManifest?.Sha256 ?? string.Empty;
        var postSha = postManifest?.Sha256 ?? string.Empty;
        var promptSha = PilotBSha256.Compute(options.PromptBytes);
        var artifactComplete = RequiredEvidenceArtifactsExist(artifactPaths);
        if (!artifactComplete)
        {
            invalidReasons.Add("missing-artifact");
        }

        var distinctReasons = invalidReasons.Distinct(StringComparer.Ordinal).ToArray();
        var status = distinctReasons.Length == 0
            ? PilotBRunnerStatus.Valid
            : PilotBRunnerStatus.Invalid;
        var integrity = new PilotBRunnerIntegrityFacts(
            executableSha,
            preflight.ArmManifestSha256 ?? string.Empty,
            promptSha,
            preSha,
            postSha,
            preflight.RepositoryBoundaryValid,
            artifactComplete,
            timingValid,
            AuthLaneExcluded: true,
            WorkspaceIntegrityCaptured: preManifest is not null && postManifest is not null);

        var fingerprint = PilotBSha256.Compute(string.Join(
            "\n",
            status.ToString(),
            string.Join("|", ExactInvocation),
            promptSha,
            preflight.ArmManifestSha256,
            preSha,
            postSha,
            PilotBSha256.Compute(stdoutBytes),
            string.Join("|", distinctReasons)));

        try
        {
            await File.WriteAllTextAsync(
                artifactPaths.IntegrityPath,
                JsonSerializer.Serialize(new
                {
                    schema_version = "pilot-b.integrity.v3",
                    executable_sha256 = integrity.ExecutableSha256,
                    arm_manifest_sha256 = integrity.ArmManifestSha256,
                    prompt_sha256 = integrity.PromptSha256,
                    pre_manifest_sha256 = integrity.PreManifestSha256,
                    post_manifest_sha256 = integrity.PostManifestSha256,
                    repository_boundary_valid = integrity.RepositoryBoundaryValid,
                    artifact_complete = integrity.ArtifactComplete,
                    timing_valid = integrity.TimingValid,
                    auth_lane_excluded = integrity.AuthLaneExcluded,
                    workspace_integrity_captured = integrity.WorkspaceIntegrityCaptured,
                    status = status.ToString().ToLowerInvariant(),
                    invalid_reasons = distinctReasons
                }),
                Encoding.UTF8,
                cancellationToken);
            await File.WriteAllTextAsync(
                artifactPaths.MetadataPath,
                JsonSerializer.Serialize(new
                {
                    schema_version = "pilot-b.runner-metadata.v3",
                    arm_id = manifest.ArmId,
                    cli_version = manifest.CliVersion,
                    model_alias = manifest.ModelAlias,
                    reasoning_effort = manifest.ReasoningEffort,
                    sandbox = manifest.Sandbox,
                    approval_policy = manifest.ApprovalPolicy,
                    started_at_utc = startedAt.ToUniversalTime().ToString("O"),
                    completed_at_utc = endAt.ToUniversalTime().ToString("O"),
                    exit_code = exitCode,
                    timed_out = timedOut,
                    invocation = ExactInvocation,
                    prompt_sha256 = promptSha,
                    executable_sha256 = integrity.ExecutableSha256,
                    qualification = options.IsQualification,
                    scored = !options.IsQualification,
                    status = status.ToString().ToLowerInvariant(),
                    invalid_reasons = distinctReasons
                }),
                Encoding.UTF8,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            distinctReasons = distinctReasons.Append("metadata-write-failed").Distinct(StringComparer.Ordinal).ToArray();
            status = PilotBRunnerStatus.Invalid;
        }

        return new PilotBRunnerResult(
            status,
            options.IsQualification,
            !options.IsQualification && status == PilotBRunnerStatus.Valid,
            exitCode,
            timedOut,
            distinctReasons,
            ExactInvocation,
            transcript,
            artifactPaths,
            integrity,
            fingerprint);
    }

    private static async Task<PreflightResult> PreflightAsync(
        PilotBRunnerOptions options,
        CancellationToken cancellationToken)
    {
        var reasons = new List<string>();
        var executablePath = AbsolutePath(options.ExecutablePath, "executable-not-absolute", reasons);
        var manifestPath = AbsolutePath(options.ArmManifestPath, "manifest-not-absolute", reasons);
        var fixtureRoot = AbsolutePath(options.FixtureRoot, "fixture-not-absolute", reasons);
        var artifactRoot = AbsolutePath(options.ArtifactDirectory, "artifact-not-absolute", reasons);

        if (!PilotBSha256.IsSha256(options.ExpectedExecutableSha256))
        {
            reasons.Add("invalid-expected-executable-sha256");
        }

        if (!PilotBSha256.IsSha256(options.ExpectedArmManifestSha256))
        {
            reasons.Add("invalid-expected-manifest-sha256");
        }

        if (options.PromptBytes is null || options.PromptBytes.Length == 0)
        {
            reasons.Add("empty-prompt");
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            reasons.Add("invalid-timeout");
        }

        if (executablePath is null || !File.Exists(executablePath))
        {
            reasons.Add("executable-missing");
        }

        if (manifestPath is null || !File.Exists(manifestPath))
        {
            reasons.Add("manifest-missing");
        }

        if (fixtureRoot is null || !Directory.Exists(fixtureRoot))
        {
            reasons.Add("fixture-missing");
        }
        else if (!PilotBGitBoundary.IsExactRepositoryRoot(fixtureRoot))
        {
            reasons.Add("repository-boundary-invalid");
        }

        if (artifactRoot is not null && Directory.Exists(artifactRoot) && Directory.EnumerateFileSystemEntries(artifactRoot).Any())
        {
            reasons.Add("artifact-directory-not-empty");
        }

        if (fixtureRoot is not null
            && ((executablePath is not null && PilotBFileManifest.IsWithin(fixtureRoot, executablePath))
                || (manifestPath is not null && PilotBFileManifest.IsWithin(fixtureRoot, manifestPath))
                || (artifactRoot is not null && PilotBFileManifest.IsWithin(fixtureRoot, artifactRoot))))
        {
            reasons.Add("boundary-contamination");
        }

        if (reasons.Count > 0)
        {
            return new PreflightResult(null, null, null, null, null, null, false, InvalidResult(options, reasons));
        }

        try
        {
            var executableSha = PilotBSha256.ComputeFile(executablePath!);
            if (!string.Equals(executableSha, options.ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("executable-hash-mismatch");
            }

            var manifestBytes = await File.ReadAllBytesAsync(manifestPath!, cancellationToken);
            var manifestSha = PilotBSha256.Compute(manifestBytes);
            if (!string.Equals(manifestSha, options.ExpectedArmManifestSha256, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("manifest-hash-mismatch");
            }

            PilotBArmManifest? manifest = null;
            try
            {
                manifest = PilotBArmManifest.Parse(manifestBytes);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                reasons.Add("malformed-manifest");
            }

            if (manifest is not null)
            {
                if (!string.Equals(Path.GetFullPath(manifest.RepositoryRoot), fixtureRoot, StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add("boundary-contamination");
                }

                if (!string.Equals(manifest.ArmId, "control", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(manifest.ArmId, "treatment", StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add("invalid-arm-id");
                }
            }

            if (reasons.Count > 0)
            {
                return new PreflightResult(null, null, null, null, null, null, false, InvalidResult(options, reasons));
            }

            return new PreflightResult(
                manifest!,
                fixtureRoot,
                artifactRoot,
                executablePath,
                executableSha,
                manifestSha,
                true,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new PreflightResult(null, null, null, null, null, null, false, InvalidResult(options, ["preflight-read-failed"]));
        }
    }

    private static PilotBRunnerResult InvalidResult(PilotBRunnerOptions options, IEnumerable<string> reasons)
    {
        var invalidReasons = reasons.Distinct(StringComparer.Ordinal).ToArray();
        var fingerprint = PilotBSha256.Compute(string.Join("|", "invalid", string.Join("|", invalidReasons)));
        return new PilotBRunnerResult(
            PilotBRunnerStatus.Invalid,
            options.IsQualification,
            false,
            null,
            false,
            invalidReasons,
            ExactInvocation,
            new PilotBTranscriptParseResult([], false, false, false, 0, ["not-executed"], []),
            PilotBArtifactPaths.Empty,
            new PilotBRunnerIntegrityFacts("", "", "", "", "", false, false, false, true, false),
            fingerprint);
    }

    private static string? AbsolutePath(string value, string reason, ICollection<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            reasons.Add(reason);
            return null;
        }

        return Path.GetFullPath(value);
    }

    private static PilotBArtifactPaths CreateArtifactPaths(string root)
        => new(
            root,
            Path.Combine(root, "output.jsonl"),
            Path.Combine(root, "metadata.json"),
            Path.Combine(root, "manifest.json"),
            Path.Combine(root, "pre-manifest.json"),
            Path.Combine(root, "post-manifest.json"),
            Path.Combine(root, "integrity.json"),
            Path.Combine(root, "prompt.bin"),
            Path.Combine(root, "stderr.txt"));

    private static string SafeComputeFileHash(string path, ICollection<string> reasons, string failureReason)
    {
        try
        {
            return PilotBSha256.ComputeFile(path);
        }
        catch
        {
            reasons.Add(failureReason);
            return string.Empty;
        }
    }

    private static bool RequiredEvidenceArtifactsExist(PilotBArtifactPaths paths)
    {
        var files = new[]
        {
            paths.RawOutputPath,
            paths.ManifestPath,
            paths.PreManifestPath,
            paths.PostManifestPath,
            paths.PromptPath,
            paths.StderrPath
        };
        return files.All(File.Exists);
    }

    private sealed record PreflightResult(
        PilotBArmManifest? Manifest,
        string? FixtureRoot,
        string? ArtifactRoot,
        string? ExecutablePath,
        string? ExecutableSha256,
        string? ArmManifestSha256,
        bool RepositoryBoundaryValid,
        PilotBRunnerResult? Result);
}
