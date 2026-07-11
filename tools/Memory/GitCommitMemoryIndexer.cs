using System.Diagnostics;
using System.Text;

namespace CryptoIndicatorApp.Memory;

internal sealed class GitCommitMemoryIndexer
{
    private readonly string _projectRoot;

    public GitCommitMemoryIndexer(string projectRoot)
    {
        _projectRoot = projectRoot;
    }

    public async Task<ProjectMemorySnapshot> BuildSnapshotAsync(string commitSpec)
    {
        var commitSha = (await RunGitTextAsync("rev-parse", "--verify", $"{commitSpec}^{{commit}}")).Trim();
        var treeSha = (await RunGitTextAsync("rev-parse", $"{commitSha}^{{tree}}")).Trim();
        var entries = await ListTreeEntriesAsync(commitSha);

        var tempRoot = Path.Combine(Path.GetTempPath(), "tc-dn-hofi3-memory-commit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceBlobShas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!ProjectMemoryIndexer.ShouldIndexRelativePath(entry.Path))
                {
                    continue;
                }

                var content = await RunGitBytesAsync("cat-file", "-p", entry.BlobSha);
                var targetPath = Path.Combine(tempRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                await File.WriteAllBytesAsync(targetPath, content);
                sourceBlobShas[entry.Path] = entry.BlobSha;
            }

            var snapshot = await new ProjectMemoryIndexer(tempRoot).BuildSnapshotAsync();
            var indexedAt = DateTimeOffset.UtcNow.ToString("O");
            return snapshot with
            {
                Metadata = new MemorySnapshotMetadata(
                    "git-commit",
                    commitSha,
                    treeSha,
                    sourceBlobShas,
                    indexedAt)
            };
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    public static string? ReadHead(string projectRoot)
    {
        return TryRunGitText(projectRoot, "rev-parse", "--verify", "HEAD")?.Trim();
    }

    public static string? ReadTree(string projectRoot, string commitSha)
    {
        return TryRunGitText(projectRoot, "rev-parse", $"{commitSha}^{{tree}}")?.Trim();
    }

    public static async Task<string> ResolveCommitAsync(string projectRoot, string commitSpec)
    {
        return (await RunGitTextCoreAsync(projectRoot, "rev-parse", "--verify", $"{commitSpec}^{{commit}}")).Trim();
    }

    public static async Task<string> ReadTreeAsync(string projectRoot, string commitSha)
    {
        return (await RunGitTextCoreAsync(projectRoot, "rev-parse", $"{commitSha}^{{tree}}")).Trim();
    }

    public static string? ReadBlobSha(string projectRoot, string commitSha, string sourcePath)
    {
        return TryRunGitText(projectRoot, "rev-parse", "--verify", $"{commitSha}:{sourcePath}")?.Trim();
    }

    public static async Task<byte[]> ReadBlobBytesAsync(string projectRoot, string blobSha)
    {
        return await RunGitBytesCoreAsync(projectRoot, "cat-file", "-p", blobSha);
    }

    public static bool IsWorkingTreeDirty(string projectRoot)
    {
        var status = TryRunGitText(projectRoot, "status", "--porcelain");
        return !string.IsNullOrWhiteSpace(status);
    }

    private async Task<IReadOnlyList<TreeEntry>> ListTreeEntriesAsync(string commitSha)
    {
        var output = await RunGitTextAsync("ls-tree", "-r", "-l", "-z", commitSha);
        var entries = new List<TreeEntry>();
        foreach (var rawRecord in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tabIndex = rawRecord.IndexOf('\t', StringComparison.Ordinal);
            if (tabIndex < 0)
            {
                continue;
            }

            var metadata = rawRecord[..tabIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length < 4 || !metadata[1].Equals("blob", StringComparison.Ordinal))
            {
                continue;
            }

            var path = rawRecord[(tabIndex + 1)..].Replace('\\', '/');
            entries.Add(new TreeEntry(metadata[2], path));
        }

        return entries;
    }

    private Task<string> RunGitTextAsync(params string[] arguments)
    {
        return RunGitTextCoreAsync(_projectRoot, arguments);
    }

    private Task<byte[]> RunGitBytesAsync(params string[] arguments)
    {
        return RunGitBytesCoreAsync(_projectRoot, arguments);
    }

    private static async Task<string> RunGitTextCoreAsync(string projectRoot, params string[] arguments)
    {
        var bytes = await RunGitBytesCoreAsync(projectRoot, arguments);
        return Encoding.UTF8.GetString(bytes);
    }

    private static async Task<byte[]> RunGitBytesCoreAsync(string projectRoot, params string[] arguments)
    {
        var startInfo = CreateGitStartInfo(projectRoot, arguments);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
        await using var memory = new MemoryStream();
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(memory);
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {error.Trim()}");
        }

        return memory.ToArray();
    }

    private static string? TryRunGitText(string projectRoot, params string[] arguments)
    {
        try
        {
            var startInfo = CreateGitStartInfo(projectRoot, arguments);
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30000);
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static ProcessStartInfo CreateGitStartInfo(string projectRoot, string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FindGitPath(),
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(projectRoot);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static string FindGitPath()
    {
        const string commonGitPath = @"C:\Program Files\Git\cmd\git.exe";
        return File.Exists(commonGitPath) ? commonGitPath : "git";
    }

    private sealed record TreeEntry(string BlobSha, string Path);
}
