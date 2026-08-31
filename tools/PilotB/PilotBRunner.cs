using System.Diagnostics;

namespace CryptoIndicatorApp.PilotB;

public sealed class PilotBRunner
{
    private static readonly IReadOnlyList<string> ExactInvocation =
        ["codex", "exec", "--ephemeral", "--json"];
    private const string ProcessTreeTerminationFailureDataKey = "PilotB.ProcessTreeTerminationFailure";
    private static readonly TimeSpan ProcessShutdownTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CaptureAbortTimeout = TimeSpan.FromSeconds(1);
    private readonly IEvidenceBundlePublisher evidencePublisher;
    private readonly Func<Task> beforeOwnershipAcquisition;
    private readonly IPilotBProcessTreeTerminator processTreeTerminator;

    public PilotBRunner()
        : this(
            new PilotBEvidenceBundlePublisher(),
            static () => Task.CompletedTask,
            new PilotBProcessTreeTerminator())
    {
    }

    internal PilotBRunner(Func<Task> beforeOwnershipAcquisition)
        : this(
            new PilotBEvidenceBundlePublisher(),
            beforeOwnershipAcquisition,
            new PilotBProcessTreeTerminator())
    {
    }

    internal PilotBRunner(IEvidenceBundlePublisher evidencePublisher)
        : this(
            evidencePublisher,
            static () => Task.CompletedTask,
            new PilotBProcessTreeTerminator())
    {
    }

    internal PilotBRunner(IPilotBProcessTreeTerminator processTreeTerminator)
        : this(
            new PilotBEvidenceBundlePublisher(),
            static () => Task.CompletedTask,
            processTreeTerminator)
    {
    }

    private PilotBRunner(
        IEvidenceBundlePublisher evidencePublisher,
        Func<Task> beforeOwnershipAcquisition,
        IPilotBProcessTreeTerminator processTreeTerminator)
    {
        this.evidencePublisher = evidencePublisher
            ?? throw new ArgumentNullException(nameof(evidencePublisher));
        this.beforeOwnershipAcquisition = beforeOwnershipAcquisition
            ?? throw new ArgumentNullException(nameof(beforeOwnershipAcquisition));
        this.processTreeTerminator = processTreeTerminator
            ?? throw new ArgumentNullException(nameof(processTreeTerminator));
    }

    public async Task<PilotBRunnerResult> RunAsync(
        PilotBRunnerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var promptBytes = options.PromptBytes?.ToArray() ?? [];
        var expectedPromptSha = PilotBSha256.Compute(promptBytes);
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
        var processTerminationCompleted = true;
        var sealPublished = false;
        var lockReleased = false;
        PilotBRunQualificationResult? qualification = null;
        PilotBRunnerIntegrityFacts? integrity = null;

        try
        {
            try
            {
                preManifest = PilotBFileManifest.Capture(fixtureRoot);
                await evidencePublisher.WriteNewBytesAsync(artifactPaths.PromptPath, promptBytes, cancellationToken);
                await evidencePublisher.WriteNewBytesAsync(artifactPaths.ManifestPath, manifestBytes, cancellationToken);
                await evidencePublisher.WriteNewBytesAsync(
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
                    using var outputBuffer = new MemoryStream();
                    using var errorBuffer = new MemoryStream();
                    using var captureCancellation = new CancellationTokenSource();
                    var outputTask = process.StandardOutput.BaseStream.CopyToAsync(
                        outputBuffer,
                        captureCancellation.Token);
                    var errorTask = process.StandardError.BaseStream.CopyToAsync(
                        errorBuffer,
                        captureCancellation.Token);

                    try
                    {
                        try
                        {
                            await process.StandardInput.BaseStream.WriteAsync(promptBytes, cancellationToken);
                            process.StandardInput.Close();
                        }
                        catch (Exception) when (!cancellationToken.IsCancellationRequested)
                        {
                            AddReason(captureReasons, "stdin-write-failed");
                        }

                        var waitTask = process.WaitForExitAsync();
                        var timeoutTask = Task.Delay(options.Timeout);
                        var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                        var completedTask = await Task.WhenAny(waitTask, timeoutTask, cancellationTask);
                        if (cancellationToken.IsCancellationRequested)
                        {
                            await cancellationTask;
                        }

                        if (completedTask == timeoutTask)
                        {
                            timedOut = true;
                            processTerminationCompleted = await TerminateAfterRunnerTimeoutAsync(
                                process,
                                outputTask,
                                errorTask,
                                captureCancellation,
                                captureReasons);
                            if (cancellationToken.IsCancellationRequested)
                            {
                                await cancellationTask;
                            }
                        }

                        if (processTerminationCompleted)
                        {
                            await waitTask;
                            if (cancellationToken.IsCancellationRequested)
                            {
                                await cancellationTask;
                            }

                            var captureTask = Task.WhenAll(outputTask, errorTask);
                            completedTask = await Task.WhenAny(captureTask, cancellationTask);
                            if (cancellationToken.IsCancellationRequested)
                            {
                                await cancellationTask;
                            }

                            await captureTask;
                        }
                    }
                    catch (OperationCanceledException cancellationException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        await TerminateAfterCallerCancellationAsync(
                            process,
                            outputTask,
                            errorTask,
                            captureCancellation,
                            cancellationException);
                        throw;
                    }
                    catch (Exception exception) when (cancellationToken.IsCancellationRequested)
                    {
                        var cancellationException = new OperationCanceledException(
                            "The Pilot B run was canceled.",
                            exception,
                            cancellationToken);
                        await TerminateAfterCallerCancellationAsync(
                            process,
                            outputTask,
                            errorTask,
                            captureCancellation,
                            cancellationException);
                        throw cancellationException;
                    }
                    catch (Exception)
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
                await evidencePublisher.WriteNewBytesAsync(
                    artifactPaths.PostManifestPath,
                    System.Text.Encoding.UTF8.GetBytes(postManifest.ToJson()),
                    cancellationToken);
                await evidencePublisher.WriteNewBytesAsync(artifactPaths.RawOutputPath, stdoutBytes, cancellationToken);
                await evidencePublisher.WriteNewBytesAsync(artifactPaths.StderrPath, stderrBytes, cancellationToken);
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
            var promptBytesVerified = string.Equals(promptSha, expectedPromptSha, StringComparison.OrdinalIgnoreCase);
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
                expectedPromptSha,
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
                && processTerminationCompleted
                && PilotBSha256.IsSha256(executableSha)
                && PilotBSha256.IsSha256(promptSha))
            {
                try
                {
                    var preFixtureSemanticSha = PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(preManifest);
                    var postFixtureSemanticSha = PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(postManifest);
                    var fingerprint = PilotBRunFingerprintWriter.Compute(new PilotBRunFingerprintInput(
                        executableSha,
                        expectedPromptSha,
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
                        expectedPromptSha,
                        preManifest.Sha256,
                        postManifest.Sha256,
                        preFixtureSemanticSha,
                        postFixtureSemanticSha,
                        captureReasons,
                        qualification);
                    await evidencePublisher.WriteNewBytesAsync(
                        artifactPaths.MetadataPath,
                        PilotBEvidenceBundle.CreateMetadataBytes(metadata),
                        cancellationToken);
                    await evidencePublisher.PublishSealAsync(artifactPaths, metadata, fingerprint, cancellationToken);
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

    private async Task TerminateAfterCallerCancellationAsync(
        Process process,
        Task outputTask,
        Task errorTask,
        CancellationTokenSource captureCancellation,
        OperationCanceledException cancellationException)
    {
        var captureTask = Task.WhenAll(outputTask, errorTask);
        var terminationFailure = await CaptureAfterProcessTreeTerminationAsync(process, captureTask);
        if (terminationFailure is null)
        {
            return;
        }

        try
        {
            cancellationException.Data[ProcessTreeTerminationFailureDataKey] = terminationFailure;
        }
        catch
        {
        }

        await AbortCaptureAsync(process, captureTask, captureCancellation);
    }

    private async Task<bool> TerminateAfterRunnerTimeoutAsync(
        Process process,
        Task outputTask,
        Task errorTask,
        CancellationTokenSource captureCancellation,
        ICollection<string> captureReasons)
    {
        var captureTask = Task.WhenAll(outputTask, errorTask);
        var terminationFailure = await CaptureAfterProcessTreeTerminationAsync(process, captureTask);
        if (terminationFailure is null)
        {
            return true;
        }

        AddReason(
            captureReasons,
            terminationFailure is TimeoutException
                ? "timeout-termination-incomplete"
                : "timeout-termination-failed");
        await AbortCaptureAsync(process, captureTask, captureCancellation);
        return false;
    }

    private async Task<Exception?> CaptureAfterProcessTreeTerminationAsync(
        Process process,
        Task captureTask)
    {
        try
        {
            processTreeTerminator.Terminate(process);
            await process.WaitForExitAsync().WaitAsync(ProcessShutdownTimeout);
            await captureTask.WaitAsync(ProcessShutdownTimeout);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task AbortCaptureAsync(
        Process process,
        Task captureTask,
        CancellationTokenSource captureCancellation)
    {
        captureCancellation.Cancel();
        TryDispose(process.StandardInput);
        TryDispose(process.StandardOutput);
        TryDispose(process.StandardError);
        try
        {
            await captureTask.WaitAsync(CaptureAbortTimeout);
        }
        catch
        {
        }
    }

    private static void TryDispose(IDisposable disposable)
    {
        try
        {
            disposable.Dispose();
        }
        catch
        {
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
