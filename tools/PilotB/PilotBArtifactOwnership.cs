using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CryptoIndicatorApp.PilotB.Tests")]

namespace CryptoIndicatorApp.PilotB;

internal static class PilotBArtifactOwnership
{
    private const int ErrorFileExists = 80;
    private const int ErrorAlreadyExists = 183;

    public static FileStream Acquire(string artifactRoot)
    {
        FileStream? ownership = null;
        try
        {
            try
            {
                Directory.CreateDirectory(artifactRoot);
            }
            catch (IOException exception) when (IsAlreadyExists(exception))
            {
                throw new ArtifactOwnershipConflictException("Artifact path appeared after preflight.", exception);
            }

            var attributes = File.GetAttributes(artifactRoot);
            if ((attributes & FileAttributes.Directory) == 0
                || (attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArtifactOwnershipConflictException("Artifact ownership requires a regular directory.");
            }

            var lockPath = Path.Combine(artifactRoot, ".pilot-b-write-lock");
            try
            {
                ownership = new FileStream(
                    lockPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
            }
            catch (IOException exception) when (IsAlreadyExists(exception))
            {
                throw new ArtifactOwnershipConflictException("Artifact write lock is already owned.", exception);
            }

            if (Directory.EnumerateFileSystemEntries(artifactRoot)
                .Any(path => !string.Equals(path, lockPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArtifactOwnershipConflictException("Artifact directory cannot be reused.");
            }

            return ownership;
        }
        catch (Exception exception)
        {
            var cleanupFailures = new List<Exception>();
            if (ownership is not null)
            {
                try
                {
                    ownership.Dispose();
                }
                catch (Exception cleanupException)
                {
                    cleanupFailures.Add(cleanupException);
                }

                try
                {
                    File.Delete(Path.Combine(artifactRoot, ".pilot-b-write-lock"));
                }
                catch (Exception cleanupException)
                {
                    cleanupFailures.Add(cleanupException);
                }
            }

            var reasons = new List<string>
            {
                exception is ArtifactOwnershipConflictException
                    ? PilotBPreflightReasonCodes.ArtifactOwnershipConflict
                    : PilotBPreflightReasonCodes.ArtifactOwnershipUnavailable
            };
            if (cleanupFailures.Count > 0)
            {
                reasons.Add(PilotBPreflightReasonCodes.ArtifactOwnershipCleanupFailed);
            }

            var diagnostic = cleanupFailures.Count == 0
                ? exception
                : new AggregateException([exception, .. cleanupFailures]);
            throw new PilotBPreflightException(reasons, diagnostic);
        }
    }

    private static bool IsAlreadyExists(IOException exception)
        => (exception.HResult & 0xffff) is ErrorFileExists or ErrorAlreadyExists;

    private sealed class ArtifactOwnershipConflictException(string message, Exception? innerException = null)
        : IOException(message, innerException);
}
