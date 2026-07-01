using System.Diagnostics;
using System.Text.Json;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class CuratedRetainDryRunTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public async Task DryRunReportUsesAllowlistAndDoesNotCallExternalRetainOrAutomation()
    {
        using var fixture = TemporaryProjectFixture.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("AGENTS.md", "# Agents\n\n");
        fixture.Write("TC-DN-HOFI3.md", "# Formula Source\n");
        fixture.Write("docs/formulas.md", "# Formulas\n");
        fixture.Write("tasks/lessons.md", "# Lessons\n");
        fixture.Write("docs/decisions/0008-curated-retain-and-memory-lifecycle-policy.md", "# ADR\n");
        fixture.Write("docs/memory/contract.md", "# Contract\n");
        fixture.Write("docs/memory/retain-policy.md", "# Retain\n");

        fixture.Write("recordings/live.jsonl", "{}\n");
        fixture.Write("docs/memory/generated/project-memory-index.md", "# Generated\n");
        fixture.Write("docs/memory/local-proxy-details.md", "# Proxy\n");
        fixture.Write("docs/memory/raw-experiment-dump.md", "# Raw dump\n");
        fixture.Write(".hindsight/store.md", "# Hindsight\n");
        fixture.Write("secrets/openai-token.md", "# Token\n");
        fixture.Write("bin/Debug/net8.0/build-output.md", "# Build\n");
        fixture.Write("obj/project.assets.md", "# Build\n");
        fixture.Write("publish/desktop/report.md", "# Publish\n");

        var reportPath = await RunDryRunScriptAsync(fixture.Root);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;

        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("dry-run", root.GetProperty("mode").GetString());
        Assert.Equal("scripts/curated-retain-dry-run.ps1", root.GetProperty("generator").GetString());
        Assert.False(root.GetProperty("external_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("calls_hindsight").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("rebuilds_memory").GetBoolean());

        AssertJsonArrayContainsAll(
            root.GetProperty("allowed_patterns"),
            "AGENTS.md",
            "docs/decisions/*.md",
            "docs/formulas.md",
            "TC-DN-HOFI3.md",
            "docs/memory/*.md",
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

        var files = root.GetProperty("files")
            .EnumerateArray()
            .Select(file => file.GetProperty("path").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("AGENTS.md", files);
        Assert.Contains("TC-DN-HOFI3.md", files);
        Assert.Contains("docs/formulas.md", files);
        Assert.Contains("tasks/lessons.md", files);
        Assert.Contains("docs/decisions/0008-curated-retain-and-memory-lifecycle-policy.md", files);
        Assert.Contains("docs/memory/contract.md", files);
        Assert.Contains("docs/memory/retain-policy.md", files);

        AssertNoDeniedPaths(files);
    }

    [Fact]
    public async Task DryRunReportFlagsRedactionRisksInAllowedSources()
    {
        using var fixture = TemporaryProjectFixture.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("AGENTS.md", "Do not store OPENAI_API_KEY values or .env contents.\n");
        fixture.Write("TC-DN-HOFI3.md", "# Formula Source\n");
        fixture.Write("docs/formulas.md", "A raw JSONL dump example must not be retained: {\"stream\":\"x\"}\n");
        fixture.Write("tasks/lessons.md", "Replace C:\\Users\\MECHREVO\\Desktop\\PRJCT-INDIC with repo-relative paths.\n");
        fixture.Write("docs/decisions/0008-curated-retain-and-memory-lifecycle-policy.md", "# ADR\n");
        fixture.Write("docs/memory/contract.md", "Local proxy details such as Shadowsocks ss-local endpoints are not retained.\n");
        fixture.Write("docs/memory/retain-policy.md", "Generated export docs/memory/generated/lancedb-eval-report.md is not retained.\n");

        var reportPath = await RunDryRunScriptAsync(fixture.Root);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var findingTypes = report.RootElement.GetProperty("findings")
            .EnumerateArray()
            .Select(finding => finding.GetProperty("type").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("secret_reference", findingTypes);
        Assert.Contains("env_reference", findingTypes);
        Assert.Contains("absolute_local_path", findingTypes);
        Assert.Contains("local_proxy_detail", findingTypes);
        Assert.Contains("raw_jsonl_or_dump", findingTypes);
        Assert.Contains("generated_export_reference", findingTypes);
    }

    [Fact]
    public async Task DryRunReportAddsSeverityBreakdownsMarkdownAndPolicyReferenceSignals()
    {
        using var fixture = TemporaryProjectFixture.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("AGENTS.md", "Do not store OPENAI_API_KEY values or .env contents.\n");
        fixture.Write("TC-DN-HOFI3.md", "# Formula Source\n");
        fixture.Write("docs/formulas.md", "Example leaked key: sk-testtoken1234567890\n");
        fixture.Write("tasks/lessons.md", "Replace C:\\Users\\MECHREVO\\Desktop\\PRJCT-INDIC with repo-relative paths.\n");
        fixture.Write("docs/decisions/0008-curated-retain-and-memory-lifecycle-policy.md", "# ADR\n");
        fixture.Write("docs/memory/contract.md", "Generated export docs/memory/generated/lancedb-eval-report.md is not retained.\n");
        fixture.Write("docs/memory/lancedb-spike.md", "The old deterministic token-hash vectors remain fallback-only.\n");
        fixture.Write("docs/memory/retain-policy.md", "Raw JSONL and local proxy details must not be retained.\n");

        var reportPath = await RunDryRunScriptAsync(fixture.Root);
        var markdownReportPath = Path.Combine(fixture.Root, "docs", "memory", "generated", "curated-retain-dry-run-report.md");
        Assert.True(File.Exists(markdownReportPath), $"Missing markdown report: {markdownReportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("docs/memory/generated/curated-retain-dry-run-report.md", root.GetProperty("markdown_report_path").GetString());

        var summary = root.GetProperty("summary");
        var severityCounts = summary.GetProperty("findings_by_severity");
        Assert.True(severityCounts.GetProperty("critical").GetInt32() >= 1);
        Assert.True(severityCounts.GetProperty("review").GetInt32() >= 1);
        Assert.True(severityCounts.GetProperty("info").GetInt32() >= 1);

        var typeCounts = summary.GetProperty("findings_by_type");
        Assert.True(typeCounts.GetProperty("secret_reference").GetInt32() >= 2);
        Assert.True(typeCounts.GetProperty("absolute_local_path").GetInt32() >= 1);

        var findings = root.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Contains(findings, finding =>
            finding.GetProperty("type").GetString() == "secret_reference" &&
            finding.GetProperty("severity").GetString() == "critical" &&
            !finding.GetProperty("policy_reference").GetBoolean());

        Assert.Contains(findings, finding =>
            finding.GetProperty("type").GetString() == "secret_reference" &&
            finding.GetProperty("severity").GetString() == "info" &&
            finding.GetProperty("policy_reference").GetBoolean());

        Assert.DoesNotContain(findings, finding =>
            finding.GetProperty("type").GetString() == "secret_reference" &&
            finding.GetProperty("source_path").GetString() == "docs/memory/lancedb-spike.md");

        Assert.Equal(
            findings.Length,
            findings.Select(finding =>
                string.Join(
                    "|",
                    finding.GetProperty("type").GetString(),
                    finding.GetProperty("source_path").GetString(),
                    finding.GetProperty("line").GetInt32(),
                    finding.GetProperty("rule").GetString()))
                .Distinct(StringComparer.Ordinal)
                .Count());

        var markdown = File.ReadAllText(markdownReportPath);
        Assert.Contains("# Curated Retain Dry-Run Report", markdown);
        Assert.Contains("## Findings By Severity", markdown);
        Assert.Contains("## Review Findings", markdown);
    }

    private static async Task<string> RunDryRunScriptAsync(string projectRoot)
    {
        var scriptPath = Path.Combine(Root, "scripts", "curated-retain-dry-run.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ProjectRoot \"{projectRoot}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        Assert.NotNull(process);
        var outputTask = process!.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // The assertion below is the useful failure; cleanup is best effort.
            }

            Assert.Fail("curated-retain-dry-run.ps1 timed out.");
        }

        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}\nSTDOUT:\n{output}\nSTDERR:\n{error}");

        var reportPath = Path.Combine(projectRoot, "docs", "memory", "generated", "curated-retain-dry-run-report.json");
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");
        return reportPath;
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
            Assert.False(path.Contains("raw-experiment", StringComparison.OrdinalIgnoreCase), path);
            Assert.DoesNotContain(path.Split('/'), segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(path.Split('/'), segment => segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(path.Split('/'), segment => segment.Equals("publish", StringComparison.OrdinalIgnoreCase));
        }
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
            var root = Path.Combine(Path.GetTempPath(), "tc-dn-hofi3-curated-retain-dry-run-tests", Guid.NewGuid().ToString("N"));
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
