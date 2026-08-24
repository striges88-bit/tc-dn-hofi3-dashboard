using System.Diagnostics;

namespace CryptoIndicatorApp.PilotB;

public sealed class PilotBRunner
{
    private static readonly IReadOnlyList<string> ExactInvocation =
        ["codex", "exec", "--ephemeral", "--json"];
    private readonly Func<Task> beforeOwnershipAcquisition;

    public PilotBRunner()
        : this(static () => Task.CompletedTask)
    {
    }

    internal PilotBRunner(Func<Task> beforeOwnershipAcquisition)
    {
        this.beforeOwnershipAcquisition = beforeOwnershipAcquisition
            ?? throw new ArgumentNullException(nameof(beforeOwnershipAcquisition));
    }

    public async Task<PilotBRunnerResult> RunAsync(
        PilotBRunnerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var promptBytes = options.PromptBytes?.ToArray() ?? [];
        var preflight = await PreflightAsync(options, promptBytes, cancellationToken);
        var manifest = preflight.Manifest!;
        var manifestBytes = preflight.ManifestBytes!;
        var fixtureRoot = preflight.FixtureRoot!;
        var artifactRoot = preflight.ArtifactRoot!;
        var executablePath = preflight.ExecutablePath!;
        await beforeOwnershipAcquisition();
        var artifactPaths = PilotBEvidenceBundle.CreatePaths(artifactRoot);
        var ownershipLock = PilotBArtifactOwnership.Acquire(artifactRoot);

        var captureReasons = new List<string>();
        var startedAt = options.UtcNowProvider();
        var stopwatch = Stopwatch.StartNew();
        var timedOut = false;
        int? exitCode = null;
        byte[] stdoutBytes = [];
        byte[] stderrBytes = [];
        PilotBFileManifest? preManifest = null;
        PilotBFileManifest? postManifest = null;
        var processStarted = false;
        var sealPublished = false;
        var lockReleased = false;
        PilotBRunQualificationResult? qualification = null;
        PilotBRunnerIntegrityFacts? integrity = null;

        try
        {
            try
            {
                preManifest = PilotBFileManifest.Capture(fixtureRoot);
                await PilotBEvidenceBundle.WriteNewBytesAsync(artifactPaths.PromptPath, promptBytes, cancellationToken);
                await PilotBEvidenceBundle.WriteNewBytesAsync(artifactPaths.ManifestPath, manifestBytes, cancellationToken);
                await PilotBEvidenceBundle.WriteNewBytesAsync(
                    artifactPaths.PreManifestPath,
                    System.Text.Encoding.UTF8.GetBytes(preManifest.ToJson()),
                    cancellationToken);

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
                    AddReason(captureReasons, "process-start-failed");
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
                        await process.StandardInput.BaseStream.WriteAsync(promptBytes, cancellationToken);
                        process.StandardInput.Close();
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        AddReason(captureReasons, "stdin-write-failed");
                    }

                    var waitTask = process.WaitForExitAsync(cancellationToken);
                    var timeoutTask = Task.Delay(options.Timeout, cancellationToken);
                    if (await Task.WhenAny(waitTask, timeoutTask) != waitTask)
                    {
                        timedOut = true;
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
                        AddReason(captureReasons, "output-capture-failed");
                    }

                    stdoutBytes = outputBuffer.ToArray();
                    stderrBytes = errorBuffer.ToArray();
                    if (!timedOut)
                    {
                        exitCode = process.ExitCode;
                    }
                }

                postManifest = PilotBFileManifest.Capture(fixtureRoot);
                await PilotBEvidenceBundle.WriteNewBytesAsync(
                    artifactPaths.PostManifestPath,
                    System.Text.Encoding.UTF8.GetBytes(postManifest.ToJson()),
                    cancellationToken);
                await PilotBEvidenceBundle.WriteNewBytesAsync(artifactPaths.RawOutputPath, stdoutBytes, cancellationToken);
                await PilotBEvidenceBundle.WriteNewBytesAsync(artifactPaths.StderrPath, stderrBytes, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                AddReason(captureReasons, "artifact-or-boundary-capture-failed");
            }

            var transcript = PilotBTranscriptParser.Parse(stdoutBytes);
            var completedAt = options.UtcNowProvider();
            var elapsedTicks = stopwatch.Elapsed.Ticks;
            var timingValid = !timedOut && elapsedTicks <= options.Timeout.Ticks;
            var executableSha = SafeComputeFileHash(executablePath, captureReasons, "executable-read-failed");
            var executableHashValid = string.Equals(executableSha, preflight.ExecutableSha256, StringComparison.OrdinalIgnoreCase);
            var promptSha = SafeComputeFileHash(artifactPaths.PromptPath, captureReasons, "prompt-read-failed");
            var promptBytesVerified = string.Equals(promptSha, PilotBSha256.Compute(promptBytes), StringComparison.OrdinalIgnoreCase);
            var repositoryBoundaryValid = preflight.RepositoryBoundaryValid
                && PilotBGitBoundary.IsExactRepositoryRoot(fixtureRoot)
                && string.Equals(Path.GetFullPath(manifest.RepositoryRoot), fixtureRoot, StringComparison.OrdinalIgnoreCase)
                && !PilotBFileManifest.IsWithin(fixtureRoot, artifactRoot)
                && !PilotBFileManifest.IsWithin(fixtureRoot, executablePath);
            var workspaceIntegrityCaptured = preManifest is not null && postManifest is not null;
            var payloadCaptured = PilotBEvidenceBundle.PayloadArtifactsExist(artifactPaths, includeMetadata: false);
            qualification = PilotBRunQualification.Evaluate(new PilotBRunQualificationFacts(
                processStarted,
                exitCode,
                timedOut,
                transcript,
                timingValid,
                executableHashValid,
                repositoryBoundaryValid,
                promptBytesVerified,
                workspaceIntegrityCaptured,
                payloadCaptured,
                captureReasons));
            integrity = new PilotBRunnerIntegrityFacts(
                executableSha,
                preflight.ArmManifestSha256 ?? string.Empty,
                promptSha,
                preManifest?.Sha256 ?? string.Empty,
                postManifest?.Sha256 ?? string.Empty,
                repositoryBoundaryValid,
                ArtifactComplete: false,
                timingValid,
                AuthLaneExcluded: true,
                WorkspaceIntegrityCaptured: workspaceIntegrityCaptured);

            if (payloadCaptured
                && preManifest is not null
                && postManifest is not null
                && PilotBSha256.IsSha256(executableSha)
                && PilotBSha256.IsSha256(promptSha))
            {
                try
                {
                    var preFixtureSemanticSha = PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(preManifest);
                    var postFixtureSemanticSha = PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(postManifest);
                    var fingerprint = PilotBRunFingerprintWriter.Compute(new PilotBRunFingerprintInput(
                        executableSha,
                        promptSha,
                        manifest,
                        transcript,
                        preFixtureSemanticSha,
                        postFixtureSemanticSha,
                        options.IsQualification,
                        qualification,
                        exitCode,
                        timedOut));
                    var metadata = new PilotBEvidenceMetadata(
                        executablePath,
                        options.ExpectedExecutableSha256,
                        options.ExpectedArmManifestSha256,
                        fixtureRoot,
                        artifactRoot,
                        startedAt,
                        completedAt,
                        ExactInvocation,
                        manifest.ArmId,
                        manifest.CliVersion,
                        manifest.ModelAlias,
                        manifest.ReasoningEffort,
                        manifest.Sandbox,
                        manifest.ApprovalPolicy,
                        processStarted,
                        exitCode,
                        timedOut,
                        options.Timeout.Ticks,
                        elapsedTicks,
                        timingValid,
                        repositoryBoundaryValid,
                        promptBytesVerified,
                        executableHashValid,
                        workspaceIntegrityCaptured,
                        payloadCaptured,
                        options.IsQualification,
                        executableSha,
                        preflight.ArmManifestSha256!,
                        promptSha,
                        preManifest.Sha256,
                        postManifest.Sha256,
                        preFixtureSemanticSha,
                        postFixtureSemanticSha,
                        captureReasons,
                        qualification);
                    await PilotBEvidenceBundle.WriteNewBytesAsync(
                        artifactPaths.MetadataPath,
                        PilotBEvidenceBundle.CreateMetadataBytes(metadata),
                        cancellationToken);
                    await PilotBEvidenceBundle.PublishSealAsync(artifactPaths, metadata, fingerprint, cancellationToken);
                    sealPublished = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    AddReason(captureReasons, "seal-publication-failed");
                }
            }
        }
        finally
        {
            ownershipLock.Dispose();
            try
            {
                File.Delete(artifactPaths.LockPath);
                lockReleased = !File.Exists(artifactPaths.LockPath);
            }
            catch
            {
                AddReason(captureReasons, "ownership-release-failed");
            }
        }

        var finalTranscript = PilotBTranscriptParser.Parse(stdoutBytes);
        var finalIntegrity = integrity ?? new PilotBRunnerIntegrityFacts("", "", "", "", "", false, false, false, true, false);
        if (!sealPublished || !lockReleased)
        {
            return UnsealedResult(
                options,
                exitCode,
                timedOut,
                finalTranscript,
                artifactPaths,
                finalIntegrity,
                qualification,
                captureReasons);
        }

        var verification = new PilotBEvidenceBundleVerifier().Verify(artifactPaths);
        if (verification.EvidenceState != PilotBEvidenceState.Sealed || verification.Qualification is null)
        {
            return UnsealedResult(
                options,
                exitCode,
                timedOut,
                finalTranscript,
                artifactPaths,
                finalIntegrity,
                qualification,
                captureReasons.Concat(verification.InvalidReasons));
        }

        var verifiedIntegrity = finalIntegrity with { ArtifactComplete = true };
        return new PilotBRunnerResult(
            verification.Qualification.Validity == PilotBRunValidity.Valid
                ? PilotBRunnerStatus.Valid
                : PilotBRunnerStatus.Invalid,
            options.IsQualification,
            !options.IsQualification && verification.Qualification.Validity == PilotBRunValidity.Valid,
            exitCode,
            timedOut,
            verification.Qualification.InvalidReasons,
            ExactInvocation,
            finalTranscript,
            artifactPaths,
            verifiedIntegrity,
            verification.SemanticFingerprint)
        {
            EvidenceState = PilotBEvidenceState.Sealed,
            RunValidity = verification.Qualification.Validity,
            Qualification = verification.Qualification
        };
    }

    private static async Task<PreflightResult> PreflightAsync(
        PilotBRunnerOptions options,
        byte[] promptBytes,
        CancellationToken cancellationToken)
    {
        var reasons = new List<string>();
        var executablePath = AbsolutePath(options.ExecutablePath, PilotBPreflightReasonCodes.ExecutableNotAbsolute, reasons);
        var manifestPath = AbsolutePath(options.ArmManifestPath, PilotBPreflightReasonCodes.ManifestNotAbsolute, reasons);
        var fixtureRoot = AbsolutePath(options.FixtureRoot, PilotBPreflightReasonCodes.FixtureNotAbsolute, reasons);
        var artifactRoot = AbsolutePath(options.ArtifactDirectory, PilotBPreflightReasonCodes.ArtifactNotAbsolute, reasons);

        if (!PilotBSha256.IsSha256(options.ExpectedExecutableSha256))
        {
            reasons.Add(PilotBPreflightReasonCodes.InvalidExpectedExecutableSha256);
        }

        if (!PilotBSha256.IsSha256(options.ExpectedArmManifestSha256))
        {
            reasons.Add(PilotBPreflightReasonCodes.InvalidExpectedManifestSha256);
        }

        if (promptBytes.Length == 0)
        {
            reasons.Add(PilotBPreflightReasonCodes.EmptyPrompt);
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            reasons.Add(PilotBPreflightReasonCodes.InvalidTimeout);
        }

        if (executablePath is not null && !File.Exists(executablePath))
        {
            reasons.Add(PilotBPreflightReasonCodes.ExecutableMissing);
        }

        if (manifestPath is not null && !File.Exists(manifestPath))
        {
            reasons.Add(PilotBPreflightReasonCodes.ManifestMissing);
        }

        if (fixtureRoot is not null && !Directory.Exists(fixtureRoot))
        {
            reasons.Add(PilotBPreflightReasonCodes.FixtureMissing);
        }
        else if (fixtureRoot is not null && !PilotBGitBoundary.IsExactRepositoryRoot(fixtureRoot))
        {
            reasons.Add(PilotBPreflightReasonCodes.RepositoryBoundaryInvalid);
        }

        if (artifactRoot is not null && FileSystemEntryExists(artifactRoot))
        {
            reasons.Add(PilotBPreflightReasonCodes.ArtifactPathAlreadyExists);
        }

        if (fixtureRoot is not null
            && ((executablePath is not null && PilotBFileManifest.IsWithin(fixtureRoot, executablePath))
                || (manifestPath is not null && PilotBFileManifest.IsWithin(fixtureRoot, manifestPath))
                || (artifactRoot is not null && PilotBFileManifest.IsWithin(fixtureRoot, artifactRoot))))
        {
            reasons.Add(PilotBPreflightReasonCodes.BoundaryContamination);
        }

        if (reasons.Count > 0)
        {
            throw new PilotBPreflightException(reasons);
        }

        try
        {
            var executableSha = PilotBSha256.ComputeFile(executablePath!);
            if (!string.Equals(executableSha, options.ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add(PilotBPreflightReasonCodes.ExecutableHashMismatch);
            }

            var manifestBytes = await File.ReadAllBytesAsync(manifestPath!, cancellationToken);
            var manifestSha = PilotBSha256.Compute(manifestBytes);
            if (!string.Equals(manifestSha, options.ExpectedArmManifestSha256, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add(PilotBPreflightReasonCodes.ManifestHashMismatch);
            }

            PilotBArmManifest? manifest = null;
            try
            {
                manifest = PilotBArmManifest.Parse(manifestBytes);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                reasons.Add(PilotBPreflightReasonCodes.MalformedManifest);
            }

            if (manifest is not null)
            {
                if (!string.Equals(Path.GetFullPath(manifest.RepositoryRoot), fixtureRoot, StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add(PilotBPreflightReasonCodes.BoundaryContamination);
                }

                if (!string.Equals(manifest.ArmId, "control", StringComparison.Ordinal)
                    && !string.Equals(manifest.ArmId, "treatment", StringComparison.Ordinal))
                {
                    reasons.Add(PilotBPreflightReasonCodes.InvalidArmId);
                }
            }

            if (reasons.Count > 0)
            {
                throw new PilotBPreflightException(reasons);
            }

            return new PreflightResult(
                manifest!,
                manifestBytes,
                fixtureRoot,
                artifactRoot,
                executablePath,
                executableSha,
                manifestSha,
                true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PilotBPreflightException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PilotBPreflightException([PilotBPreflightReasonCodes.PreflightReadFailed], exception);
        }
    }

    private static PilotBRunnerResult UnsealedResult(
        PilotBRunnerOptions options,
        int? exitCode,
        bool timedOut,
        PilotBTranscriptParseResult transcript,
        PilotBArtifactPaths artifacts,
        PilotBRunnerIntegrityFacts integrity,
        PilotBRunQualificationResult? qualification,
        IEnumerable<string> additionalReasons)
    {
        var invalidReasons = (qualification?.InvalidReasons ?? [])
            .Concat(additionalReasons)
            .Append("evidence-unsealed")
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new PilotBRunnerResult(
            PilotBRunnerStatus.Invalid,
            options.IsQualification,
            false,
            exitCode,
            timedOut,
            invalidReasons,
            ExactInvocation,
            transcript,
            artifacts,
            integrity with { ArtifactComplete = false },
            null)
        {
            EvidenceState = PilotBEvidenceState.Unsealed,
            RunValidity = null,
            Qualification = null
        };
    }

    private static string? AbsolutePath(string value, string reason, ICollection<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            reasons.Add(reason);
            return null;
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reasons.Add(reason);
            return null;
        }
    }

    private static bool FileSystemEntryExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception exception)
        {
            throw new PilotBPreflightException([PilotBPreflightReasonCodes.PreflightReadFailed], exception);
        }
    }

    private static string SafeComputeFileHash(string path, ICollection<string> reasons, string failureReason)
    {
        try
        {
            return PilotBSha256.ComputeFile(path);
        }
        catch
        {
            AddReason(reasons, failureReason);
            return string.Empty;
        }
    }

    private static void AddReason(ICollection<string> reasons, string reason)
    {
        if (!reasons.Contains(reason, StringComparer.Ordinal))
        {
            reasons.Add(reason);
        }
    }

    private sealed record PreflightResult(
        PilotBArmManifest? Manifest,
        byte[]? ManifestBytes,
        string? FixtureRoot,
        string? ArtifactRoot,
        string? ExecutablePath,
        string? ExecutableSha256,
        string? ArmManifestSha256,
        bool RepositoryBoundaryValid);
}
