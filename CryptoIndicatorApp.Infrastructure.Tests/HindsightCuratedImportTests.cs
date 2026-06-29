using System.Diagnostics;
using System.Text.Json;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class HindsightCuratedImportTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void CuratedImportScriptListsOnlyReviewedProjectMemorySources()
    {
        var manifestPath = RunCuratedImportScript(Root);
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;

        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.True(root.GetProperty("install_required").ValueKind == JsonValueKind.False);

        AssertJsonArrayContainsAll(
            root.GetProperty("allowed_patterns"),
            "docs/memory/*.md",
            "docs/decisions/*.md",
            "docs/formulas.md",
            "TC-DN-HOFI3.md",
            "AGENTS.md",
            "tasks/lessons.md");

        AssertJsonArrayContainsAll(
            root.GetProperty("denied_patterns"),
            "recordings/*.jsonl",
            "docs/memory/generated/",
            ".hindsight/",
            "secrets/",
            "bin/",
            "obj/",
            "publish/",
            "local proxy details",
            "raw experiment dumps");

        var files = GetManifestPaths(root).ToArray();
        Assert.Contains("AGENTS.md", files);
        Assert.Contains("TC-DN-HOFI3.md", files);
        Assert.Contains("docs/formulas.md", files);
        Assert.Contains("tasks/lessons.md", files);
        Assert.Contains("docs/memory/contract.md", files);
        Assert.Contains("docs/decisions/0002-agent-memory-contract.md", files);

        Assert.All(files, AssertAllowedImportPath);
        AssertNoDeniedPaths(files);
    }

    [Fact]
    public void CuratedImportScriptRejectsDenylistedFilesEvenWhenTheyMatchMemoryGlobs()
    {
        using var fixture = TemporaryProjectFixture.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("AGENTS.md", "# Agents\n");
        fixture.Write("TC-DN-HOFI3.md", "# Formula Source\n");
        fixture.Write("docs/formulas.md", "# Formulas\n");
        fixture.Write("tasks/lessons.md", "# Lessons\n");
        fixture.Write("docs/decisions/0002-agent-memory-contract.md", "# ADR\n");
        fixture.Write("docs/memory/contract.md", "# Contract\n");
        fixture.Write("docs/memory/hindsight-spike.md", "# Hindsight\n");

        fixture.Write("recordings/live.jsonl", "{}\n");
        fixture.Write("docs/memory/generated/project-memory-index.md", "# Generated\n");
        fixture.Write("docs/memory/secret-import-note.md", "# Secret\n");
        fixture.Write("docs/memory/raw-experiment-dump.md", "# Raw experiment dump\n");
        fixture.Write("docs/memory/local-proxy-details.md", "# Proxy\n");
        fixture.Write(".hindsight/bank.md", "# Hindsight store\n");
        fixture.Write("secrets/openai-token.md", "# Token\n");
        fixture.Write("bin/Debug/net8.0/build-output.md", "# Build\n");
        fixture.Write("obj/project.assets.md", "# Build\n");
        fixture.Write("publish/desktop/report.md", "# Publish\n");

        var manifestPath = RunCuratedImportScript(fixture.Root);
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var files = GetManifestPaths(manifest.RootElement).ToArray();

        Assert.Contains("AGENTS.md", files);
        Assert.Contains("TC-DN-HOFI3.md", files);
        Assert.Contains("docs/formulas.md", files);
        Assert.Contains("tasks/lessons.md", files);
        Assert.Contains("docs/decisions/0002-agent-memory-contract.md", files);
        Assert.Contains("docs/memory/contract.md", files);
        Assert.Contains("docs/memory/hindsight-spike.md", files);

        Assert.DoesNotContain("recordings/live.jsonl", files);
        Assert.DoesNotContain("docs/memory/generated/project-memory-index.md", files);
        Assert.DoesNotContain("docs/memory/secret-import-note.md", files);
        Assert.DoesNotContain("docs/memory/raw-experiment-dump.md", files);
        Assert.DoesNotContain("docs/memory/local-proxy-details.md", files);
        Assert.DoesNotContain(".hindsight/bank.md", files);
        Assert.DoesNotContain("secrets/openai-token.md", files);
        Assert.DoesNotContain("bin/Debug/net8.0/build-output.md", files);
        Assert.DoesNotContain("obj/project.assets.md", files);
        Assert.DoesNotContain("publish/desktop/report.md", files);
        AssertNoDeniedPaths(files);
    }

    private static string RunCuratedImportScript(string projectRoot)
    {
        var scriptPath = Path.Combine(Root, "scripts", "hindsight-curated-import.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var manifestPath = Path.Combine(projectRoot, "docs", "memory", "generated", "hindsight-curated-import-manifest.json");
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ProjectRoot \"{projectRoot}\" -OutputPath \"{manifestPath}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        Assert.NotNull(process);
        Assert.True(process!.WaitForExit(TimeSpan.FromSeconds(30)), "hindsight-curated-import.ps1 timed out.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}\nSTDOUT:\n{output}\nSTDERR:\n{error}");
        Assert.True(File.Exists(manifestPath), $"Missing manifest: {manifestPath}");
        return manifestPath;
    }

    private static IEnumerable<string> GetManifestPaths(JsonElement root)
    {
        return root.GetProperty("files")
            .EnumerateArray()
            .Select(file => file.GetProperty("path").GetString()!)
            .Order(StringComparer.Ordinal);
    }

    private static void AssertAllowedImportPath(string path)
    {
        var allowed = path is "AGENTS.md" or "TC-DN-HOFI3.md" or "docs/formulas.md" or "tasks/lessons.md"
            || IsDirectChildMarkdown(path, "docs/memory/")
            || IsDirectChildMarkdown(path, "docs/decisions/");

        Assert.True(allowed, $"Unexpected import path: {path}");
    }

    private static void AssertNoDeniedPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            Assert.False(path.StartsWith("recordings/", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.StartsWith("docs/memory/generated/", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.StartsWith(".hindsight/", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.StartsWith("secrets/", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.Contains("secret", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.Contains("local-proxy", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.Contains("shadowsocks", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.Contains("raw-experiment", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.Contains("raw_experiment", StringComparison.OrdinalIgnoreCase), path);
            Assert.DoesNotContain(path.Split('/'), segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(path.Split('/'), segment => segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(path.Split('/'), segment => segment.Equals("publish", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static bool IsDirectChildMarkdown(string path, string prefix)
    {
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            && path[prefix.Length..].IndexOf('/') < 0;
    }

    private static void AssertJsonArrayContainsAll(JsonElement array, params string[] expectedValues)
    {
        var actualValues = array.EnumerateArray()
            .Select(value => value.GetString())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expectedValue in expectedValues)
        {
            Assert.Contains(expectedValue, actualValues);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CryptoIndicatorApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class TemporaryProjectFixture : IDisposable
    {
        private TemporaryProjectFixture(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TemporaryProjectFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "tc-dn-hofi3-memory-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TemporaryProjectFixture(root);
        }

        public void Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
