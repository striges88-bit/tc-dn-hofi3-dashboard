using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class CuratedRetainPolicyTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void PolicyDocsDefineCuratedRetainLifecycleGates()
    {
        var adr = ReadText("docs/decisions/0008-curated-retain-and-memory-lifecycle-policy.md");
        var policy = ReadText("docs/memory/retain-policy.md");
        var contract = ReadText("docs/memory/contract.md");
        var readme = ReadText("docs/memory/README.md");
        var openQuestions = ReadText("docs/memory/open-questions.md");

        Assert.Contains("curated retain policy", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docs/memory/retain-policy.md", adr, StringComparison.Ordinal);
        Assert.Contains("redaction before retain", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete policy", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("export policy", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Codex auto-retain remains disabled", adr, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Curated Retain Policy", policy, StringComparison.Ordinal);
        Assert.Contains("redaction before retain", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete policy", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("export policy", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("external retain", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Codex auto-retain", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must not be enabled", policy, StringComparison.OrdinalIgnoreCase);

        foreach (var allowedPath in ExpectedAllowlist)
        {
            Assert.Contains(allowedPath, policy, StringComparison.Ordinal);
            Assert.Contains(allowedPath, contract, StringComparison.Ordinal);
        }

        foreach (var deniedPath in ExpectedDenylist)
        {
            Assert.Contains(deniedPath, policy, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(deniedPath, contract, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("retain-policy.md", readme, StringComparison.Ordinal);
        Assert.Contains("redaction", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete/export", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Codex auto-retain remains disabled", openQuestions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redaction/delete/export policy", openQuestions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostCommitMarkerInstallerRemainsMarkerOnlyAfterRetainPolicy()
    {
        var scriptPath = Path.Combine(Root, "scripts", "install-memory-post-commit-marker-hook.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        var hookPath = Path.Combine(temp.Path, "post-commit");
        var reportPath = Path.Combine(temp.Path, "install-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(Root)} -HookPath {Quote(hookPath)} -OutputPath {Quote(reportPath)} -PlanOnly");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.False(File.Exists(hookPath));
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("planned", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("writes_marker").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("hook_invokes_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("rebuilds_memory").GetBoolean());
        Assert.False(root.GetProperty("imports_curated_retain").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
    }

    [Fact]
    public async Task ExportDryRunUsesCuratedReportAndAllowlistedMetadataOnly()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        temp.Write("recordings/live.jsonl", "{}\n");
        temp.Write("docs/memory/generated/project-memory-index.md", "# Generated\n");
        temp.Write(".hindsight/store.md", "# Hindsight\n");
        temp.Write("secrets/openai-token.md", "# Token\n");
        temp.Write("bin/Debug/net8.0/build-output.md", "# Build\n");
        temp.Write("obj/project.assets.md", "# Build\n");
        temp.Write("publish/desktop/report.md", "# Publish\n");

        await RunCuratedDryRunAsync(temp.Path);

        var exportReportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-export-dry-run-report.json");
        var result = await RunProjectScriptAsync(
            "curated-retain-export-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)}");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.True(File.Exists(exportReportPath), $"Missing report: {exportReportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(exportReportPath));
        var root = report.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("export-dry-run", root.GetProperty("mode").GetString());
        Assert.Equal("scripts/curated-retain-export-dry-run.ps1", root.GetProperty("generator").GetString());
        Assert.True(root.GetProperty("curated_report_present").GetBoolean());
        Assert.True(root.GetProperty("writes_report_only").GetBoolean());
        Assert.False(root.GetProperty("source_content_included").GetBoolean());
        Assert.False(root.GetProperty("external_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("calls_hindsight").GetBoolean());
        Assert.False(root.GetProperty("calls_codex_retain").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("rebuilds_memory").GetBoolean());
        Assert.False(root.GetProperty("imports_denylist").GetBoolean());

        var sources = root.GetProperty("sources").EnumerateArray().ToArray();
        var sourcePaths = sources
            .Select(source => source.GetProperty("source_path").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("AGENTS.md", sourcePaths);
        Assert.Contains("TC-DN-HOFI3.md", sourcePaths);
        Assert.Contains("docs/formulas.md", sourcePaths);
        Assert.Contains("tasks/lessons.md", sourcePaths);
        Assert.Contains("docs/decisions/0008-curated-retain-and-memory-lifecycle-policy.md", sourcePaths);
        Assert.Contains("docs/memory/contract.md", sourcePaths);
        Assert.Contains("docs/memory/retain-policy.md", sourcePaths);
        AssertNoDeniedPaths(sourcePaths);

        foreach (var source in sources)
        {
            Assert.True(source.GetProperty("source_metadata_only").GetBoolean());
            Assert.True(source.GetProperty("hash_matches_report").GetBoolean(), source.GetProperty("source_path").GetString());
            Assert.True(source.TryGetProperty("source_hash", out _));
            Assert.False(source.TryGetProperty("content", out _));
            Assert.False(source.TryGetProperty("text", out _));
        }
    }

    [Fact]
    public async Task DeleteDryRunWritesDeletionPlanOnlyAndRemovesNothing()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        temp.Write("recordings/live.jsonl", "{}\n");
        var agentPath = Path.Combine(temp.Path, "AGENTS.md");
        var deniedPath = Path.Combine(temp.Path, "recordings", "live.jsonl");

        await RunCuratedDryRunAsync(temp.Path);
        var exportResult = await RunProjectScriptAsync(
            "curated-retain-export-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)}");
        Assert.True(exportResult.ExitCode == 0, exportResult.ToString());

        var deleteReportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-delete-dry-run-report.json");
        var deleteResult = await RunProjectScriptAsync(
            "curated-retain-delete-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)}");

        Assert.True(deleteResult.ExitCode == 0, deleteResult.ToString());
        Assert.True(File.Exists(deleteReportPath), $"Missing report: {deleteReportPath}");
        Assert.True(File.Exists(agentPath), "Delete dry-run must not remove allowlisted source files.");
        Assert.True(File.Exists(deniedPath), "Delete dry-run must not remove denylisted source files.");

        using var report = JsonDocument.Parse(File.ReadAllText(deleteReportPath));
        var root = report.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("delete-dry-run", root.GetProperty("mode").GetString());
        Assert.Equal("scripts/curated-retain-delete-dry-run.ps1", root.GetProperty("generator").GetString());
        Assert.True(root.GetProperty("export_report_present").GetBoolean());
        Assert.True(root.GetProperty("writes_report_only").GetBoolean());
        Assert.False(root.GetProperty("deletes_items").GetBoolean());
        Assert.False(root.GetProperty("removes_files").GetBoolean());
        Assert.False(root.GetProperty("external_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("calls_hindsight").GetBoolean());
        Assert.False(root.GetProperty("calls_codex_retain").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("rebuilds_memory").GetBoolean());

        AssertJsonArrayContainsAll(
            root.GetProperty("planned_delete_selectors"),
            "retained_item_id",
            "source_path",
            "project_profile");

        var sourcePaths = root.GetProperty("sources")
            .EnumerateArray()
            .Select(source => source.GetProperty("source_path").GetString()!)
            .ToArray();
        Assert.Contains("AGENTS.md", sourcePaths);
        AssertNoDeniedPaths(sourcePaths);
    }

    [Fact]
    public void ControlledRetainImportScriptAndDocsStayLocalOnly()
    {
        var script = ReadText("scripts/curated-retain-import.ps1");
        var policy = ReadText("docs/memory/retain-policy.md");
        var contract = ReadText("docs/memory/contract.md");
        var scriptsReadme = ReadText("scripts/README.md");

        Assert.Contains("retain-import", script, StringComparison.Ordinal);
        Assert.Contains("curated-retain-import-report.json", script, StringComparison.Ordinal);
        Assert.Contains("curated-retain-redacted-subset-report.json", script, StringComparison.Ordinal);
        Assert.DoesNotContain("curated-retain-dry-run-report.json", script, StringComparison.Ordinal);
        Assert.Contains("exit $cliExitCode", script, StringComparison.Ordinal);
        Assert.Contains("CRYPTO_MEMORY_TOOL_DLL", script, StringComparison.Ordinal);
        Assert.Contains("external_retain_enabled = $false", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("codex_auto_retain_enabled = $false", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hindsight retain", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("codex retain", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("memory-refresh-all", script, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("controlled local import", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("commit tree", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redaction-clean", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("curated-retain-import.ps1", contract, StringComparison.Ordinal);
        Assert.Contains("curated-retain-import.ps1", scriptsReadme, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ControlledRetainImportScriptWritesBlockedResultAndPreservesCliExitCode()
    {
        using var temp = TemporaryDirectory.Create();
        var inputReportPath = Path.Combine(temp.Path, "malformed-subset.json");
        var outputReportPath = Path.Combine(temp.Path, "curated-retain-import-report.json");
        var databasePath = Path.Combine(temp.Path, "project-memory.sqlite");
        File.WriteAllText(inputReportPath, "{ not valid JSON");

        var result = await RunProjectScriptAsync(
            "curated-retain-import.ps1",
            $"-ProjectRoot {Quote(Root)} -InputReportPath {Quote(inputReportPath)} -OutputPath {Quote(outputReportPath)} -DatabasePath {Quote(databasePath)}");

        Assert.Equal(2, result.ExitCode);
        Assert.True(File.Exists(outputReportPath), result.ToString());
        using var report = JsonDocument.Parse(File.ReadAllText(outputReportPath));
        var root = report.RootElement;
        Assert.Equal("blocked", root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("cli_exit_code").GetInt32());
        Assert.Equal(Path.GetFullPath(outputReportPath), root.GetProperty("output_path").GetString());
        Assert.False(root.GetProperty("output_is_generated").GetBoolean());
        Assert.Equal(0, root.GetProperty("result").GetProperty("imported_count").GetInt32());
        Assert.Contains(
            root.GetProperty("result").GetProperty("blocking_reasons").EnumerateArray().Select(reason => reason.GetString()),
            reason => reason == "invalid_input_report_json");
    }

    [Fact]
    public async Task RedactedSubsetScriptBuildsReviewedLocalReportOnly()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        temp.Write(
            "AGENTS.md",
            "# Agents\n\nOPENAI_API_KEY=sk-testtoken1234567890 must be redacted before retain.\n\nredacted subset safe phrase should survive.\n");

        await RunCuratedDryRunAsync(temp.Path);

        var result = await RunProjectScriptAsync(
            "curated-retain-redacted-subset.ps1",
            $"-ProjectRoot {Quote(temp.Path)} -SourcePath AGENTS.md");

        Assert.True(result.ExitCode == 0, result.ToString());
        var reportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-redacted-subset-report.json");
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal(2, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("redacted-subset", root.GetProperty("mode").GetString());
        Assert.Equal("scripts/curated-retain-redacted-subset.ps1", root.GetProperty("generator").GetString());
        Assert.Equal("ready_for_import", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("writes_report_only").GetBoolean());
        Assert.False(root.TryGetProperty("source_content_included", out _));
        Assert.False(root.GetProperty("raw_source_text_included").GetBoolean());
        Assert.True(root.GetProperty("source_derived_text_included").GetBoolean());
        Assert.False(root.GetProperty("candidate_text_included").GetBoolean());
        Assert.True(root.GetProperty("redacted_text_included").GetBoolean());
        Assert.False(root.GetProperty("external_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("calls_hindsight").GetBoolean());
        Assert.False(root.GetProperty("calls_codex_retain").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("rebuilds_memory").GetBoolean());

        var file = Assert.Single(root.GetProperty("files").EnumerateArray());
        Assert.Equal("AGENTS.md", file.GetProperty("path").GetString());
        Assert.Equal("redacted", file.GetProperty("redaction_status").GetString());
        Assert.Equal("reviewed-redacted-text", file.GetProperty("content_kind").GetString());
        Assert.False(file.TryGetProperty("source_content_included", out _));
        Assert.False(file.GetProperty("raw_source_text_included").GetBoolean());
        Assert.True(file.GetProperty("source_derived_text_included").GetBoolean());
        Assert.False(file.GetProperty("candidate_text_included").GetBoolean());
        Assert.True(file.GetProperty("redacted_text_included").GetBoolean());
        Assert.Equal(0, file.GetProperty("finding_count").GetInt32());
        Assert.True(file.GetProperty("original_finding_count").GetInt32() > 0);
        Assert.True(file.TryGetProperty("hash", out _));
        Assert.True(file.TryGetProperty("redacted_hash", out _));

        var redactedText = file.GetProperty("redacted_text").GetString();
        Assert.Contains("[REDACTED:", redactedText, StringComparison.Ordinal);
        Assert.Contains("redacted subset safe phrase", redactedText, StringComparison.Ordinal);
        Assert.DoesNotContain("OPENAI_API_KEY", redactedText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-testtoken", redactedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedactedSubsetReportReferencesCleanCandidateWithoutEmbeddingSourceText()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        const string sourcePath = "docs/formulas.md";

        await RunCuratedDryRunAsync(temp.Path);

        var result = await RunProjectScriptAsync(
            "curated-retain-redacted-subset.ps1",
            $"-ProjectRoot {Quote(temp.Path)} -SourcePath {sourcePath}");

        Assert.True(result.ExitCode == 0, result.ToString());
        var reportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-redacted-subset-report.json");
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;

        Assert.Equal(2, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("ready_for_import", root.GetProperty("status").GetString());
        Assert.False(root.TryGetProperty("source_content_included", out _));
        Assert.False(root.GetProperty("raw_source_text_included").GetBoolean());
        Assert.False(root.GetProperty("source_derived_text_included").GetBoolean());
        Assert.False(root.GetProperty("candidate_text_included").GetBoolean());
        Assert.False(root.GetProperty("redacted_text_included").GetBoolean());

        var file = Assert.Single(root.GetProperty("files").EnumerateArray());
        Assert.Equal(sourcePath, file.GetProperty("path").GetString());
        Assert.Equal("candidate", file.GetProperty("redaction_status").GetString());
        Assert.Equal("commit-source-reference", file.GetProperty("content_kind").GetString());
        Assert.False(file.TryGetProperty("source_content_included", out _));
        Assert.False(file.GetProperty("raw_source_text_included").GetBoolean());
        Assert.False(file.GetProperty("source_derived_text_included").GetBoolean());
        Assert.False(file.GetProperty("candidate_text_included").GetBoolean());
        Assert.False(file.GetProperty("redacted_text_included").GetBoolean());
        Assert.False(file.TryGetProperty("redacted_text", out _));
        Assert.False(file.TryGetProperty("redacted_hash", out _));
    }

    [Fact]
    public async Task RedactedSubsetScriptRequiresExactCanonicalSourcePath()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        await RunCuratedDryRunAsync(temp.Path);

        var result = await RunProjectScriptAsync(
            "curated-retain-redacted-subset.ps1",
            $"-ProjectRoot {Quote(temp.Path)} -SourcePath {Quote(@"docs\formulas.md")}");

        Assert.True(result.ExitCode == 0, result.ToString());
        var reportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-redacted-subset-report.json");
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;

        Assert.Equal("blocked", root.GetProperty("status").GetString());
        Assert.Contains(
            root.GetProperty("blocking_reasons").EnumerateArray().Select(reason => reason.GetString()),
            reason => reason == "source_not_allowlisted");
        Assert.Empty(root.GetProperty("files").EnumerateArray());
    }

    [Fact]
    public async Task RedactedSubsetScriptRejectsDuplicateSourcePaths()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        await RunCuratedDryRunAsync(temp.Path);
        var subsetScriptPath = Path.Combine(Root, "scripts", "curated-retain-redacted-subset.ps1");
        const string invocationPath = "invoke-duplicate-source.ps1";
        temp.Write(
            invocationPath,
            $"& {PowerShellLiteral(subsetScriptPath)} -ProjectRoot {PowerShellLiteral(temp.Path)} -SourcePath @('AGENTS.md', 'AGENTS.md')\n");

        var result = await RunPowerShellAsync(Path.Combine(temp.Path, invocationPath), string.Empty);

        Assert.True(result.ExitCode == 0, result.ToString());
        var reportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-redacted-subset-report.json");
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("blocked", root.GetProperty("status").GetString());
        Assert.Contains(
            root.GetProperty("blocking_reasons").EnumerateArray().Select(reason => reason.GetString()),
            reason => reason == "duplicate_source_path");
        Assert.Empty(root.GetProperty("files").EnumerateArray());
    }

    [Fact]
    public async Task RedactedSubsetScriptRejectsSourcesChangedAfterDryRun()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        temp.Write(
            "AGENTS.md",
            "# Agents\n\nOPENAI_API_KEY=sk-testtoken1234567890 must be redacted before retain.\n\nredacted subset safe phrase should survive.\n");

        await RunCuratedDryRunAsync(temp.Path);
        temp.Write(
            "AGENTS.md",
            "# Agents\n\nOPENAI_API_KEY=sk-testtoken1234567890 must be redacted before retain.\n\nchanged after dry-run should block retain.\n");

        var result = await RunProjectScriptAsync(
            "curated-retain-redacted-subset.ps1",
            $"-ProjectRoot {Quote(temp.Path)} -SourcePath AGENTS.md");

        Assert.True(result.ExitCode == 0, result.ToString());
        var reportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-redacted-subset-report.json");
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("blocked", root.GetProperty("status").GetString());
        Assert.Contains(
            root.GetProperty("blocking_reasons").EnumerateArray().Select(reason => reason.GetString()),
            reason => reason == "stale_source_metadata");
        Assert.Empty(root.GetProperty("files").EnumerateArray());
    }

    [Fact]
    public async Task RedactedSubsetScriptRejectsDryRunFindingMetadataThatDoesNotMatchScanner()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        temp.Write(
            "AGENTS.md",
            "# Agents\n\nOPENAI_API_KEY=sk-testtoken1234567890 partialredactionleak must never reach the subset report.\n");

        await RunCuratedDryRunAsync(temp.Path);
        var dryRunPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-dry-run-report.json");
        var dryRun = JsonNode.Parse(File.ReadAllText(dryRunPath))!.AsObject();
        var findings = dryRun["findings"]!.AsArray();
        Assert.NotEmpty(findings);
        foreach (var finding in findings)
        {
            finding!["line"] = 999;
        }

        File.WriteAllText(dryRunPath, dryRun.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var result = await RunProjectScriptAsync(
            "curated-retain-redacted-subset.ps1",
            $"-ProjectRoot {Quote(temp.Path)} -SourcePath AGENTS.md");

        Assert.True(result.ExitCode == 0, result.ToString());
        var reportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-redacted-subset-report.json");
        var reportText = File.ReadAllText(reportPath);
        using var report = JsonDocument.Parse(reportText);
        var root = report.RootElement;

        Assert.Equal("blocked", root.GetProperty("status").GetString());
        Assert.Contains(
            root.GetProperty("blocking_reasons").EnumerateArray().Select(reason => reason.GetString()),
            reason => reason == "invalid_input_report_contract");
        Assert.Empty(root.GetProperty("files").EnumerateArray());
        Assert.DoesNotContain("OPENAI_API_KEY", reportText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-testtoken", reportText, StringComparison.Ordinal);
        Assert.DoesNotContain("partialredactionleak", reportText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("schema_version")]
    [InlineData("generator")]
    [InlineData("status")]
    [InlineData("blocking_reasons")]
    [InlineData("safety_metadata")]
    [InlineData("orphan_finding")]
    [InlineData("duplicate_file")]
    [InlineData("duplicate_finding")]
    [InlineData("policy_reference_type")]
    [InlineData("summary_count")]
    public async Task RedactedSubsetScriptRejectsInvalidDryRunReportContract(string mutation)
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        if (mutation == "policy_reference_type")
        {
            temp.Write("AGENTS.md", "# Agents\n\nDo not store OPENAI_API_KEY values.\n");
        }

        var dryRunPath = await RunCuratedDryRunAsync(temp.Path);
        var dryRun = JsonNode.Parse(File.ReadAllText(dryRunPath))!.AsObject();

        switch (mutation)
        {
            case "schema_version":
                dryRun["schema_version"] = 99;
                break;
            case "generator":
                dryRun["generator"] = "scripts/untrusted-report.ps1";
                break;
            case "status":
                dryRun["status"] = "blocked";
                break;
            case "blocking_reasons":
                dryRun["blocking_reasons"] = new JsonArray("review_failed");
                break;
            case "safety_metadata":
                dryRun.Remove("writes_report_only");
                break;
            case "orphan_finding":
                dryRun["status"] = "review_required";
                dryRun["findings"]!.AsArray().Add(new JsonObject
                {
                    ["source_path"] = "docs/memory/not-in-files.md",
                    ["line"] = 1,
                    ["type"] = "secret_reference",
                });
                break;
            case "duplicate_file":
                dryRun["files"]!.AsArray().Add(dryRun["files"]!.AsArray()[0]!.DeepClone());
                break;
            case "duplicate_finding":
                var reportFiles = dryRun["files"]!.AsArray()
                    .Select(node => node!.AsObject())
                    .ToArray();
                foreach (var reportFile in reportFiles)
                {
                    reportFile["finding_count"] = 0;
                    reportFile["redaction_status"] = "candidate";
                }

                var agentsFile = reportFiles.Single(file => file["path"]!.GetValue<string>() == "AGENTS.md");
                agentsFile["finding_count"] = 2;
                agentsFile["redaction_status"] = "review_required";
                dryRun["status"] = "review_required";
                dryRun["findings"] = new JsonArray();
                var duplicateFinding = new JsonObject
                {
                    ["source_path"] = "AGENTS.md",
                    ["line"] = 1,
                    ["type"] = "secret_reference",
                    ["rule"] = "secret/token/api key marker",
                };
                dryRun["findings"]!.AsArray().Add(duplicateFinding);
                dryRun["findings"]!.AsArray().Add(duplicateFinding.DeepClone());
                break;
            case "policy_reference_type":
                dryRun["findings"]!.AsArray()[0]!["policy_reference"] = "false";
                break;
            case "summary_count":
                dryRun["summary"]!["file_count"] = 999;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }

        File.WriteAllText(dryRunPath, dryRun.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var result = await RunProjectScriptAsync(
            "curated-retain-redacted-subset.ps1",
            $"-ProjectRoot {Quote(temp.Path)} -SourcePath AGENTS.md");

        Assert.True(result.ExitCode == 0, result.ToString());
        var reportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-redacted-subset-report.json");
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("blocked", root.GetProperty("status").GetString());
        Assert.Contains(
            root.GetProperty("blocking_reasons").EnumerateArray().Select(reason => reason.GetString()),
            reason => reason == "invalid_input_report_contract");
        Assert.Empty(root.GetProperty("files").EnumerateArray());
    }

    [Theory]
    [InlineData("recordings/live.jsonl", "recordings/live.jsonl")]
    [InlineData("README.md", "README.md")]
    [InlineData("docs\\memory\\contract.md", "docs/memory/contract.md")]
    public async Task RedactedSubsetScriptRejectsAnyNonCuratedFileInDryRunReport(
        string reportedPath,
        string physicalPath)
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        if (!File.Exists(Path.Combine(temp.Path, physicalPath.Replace('/', Path.DirectorySeparatorChar))))
        {
            temp.Write(physicalPath, "# Untrusted dry-run source\n");
        }

        var dryRunPath = await RunCuratedDryRunAsync(temp.Path);
        var dryRun = JsonNode.Parse(File.ReadAllText(dryRunPath))!.AsObject();
        var sourceFilePath = Path.Combine(temp.Path, physicalPath.Replace('/', Path.DirectorySeparatorChar));
        dryRun["files"]!.AsArray().Add(new JsonObject
        {
            ["path"] = reportedPath,
            ["hash"] = ComputeSha256(sourceFilePath),
            ["size_bytes"] = new FileInfo(sourceFilePath).Length,
            ["redaction_status"] = "candidate",
            ["finding_count"] = 0,
        });
        dryRun["summary"]!["file_count"] = dryRun["files"]!.AsArray().Count;
        File.WriteAllText(dryRunPath, dryRun.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var result = await RunProjectScriptAsync(
            "curated-retain-redacted-subset.ps1",
            $"-ProjectRoot {Quote(temp.Path)} -SourcePath AGENTS.md");

        Assert.True(result.ExitCode == 0, result.ToString());
        var reportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-redacted-subset-report.json");
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("blocked", root.GetProperty("status").GetString());
        Assert.Contains(
            root.GetProperty("blocking_reasons").EnumerateArray().Select(reason => reason.GetString()),
            reason => reason == "invalid_input_report_contract");
        Assert.Empty(root.GetProperty("files").EnumerateArray());
    }

    [Fact]
    public async Task RedactedSubsetScriptPreservesMissingFinalNewline()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        temp.Write("AGENTS.md", "# Agents\nOPENAI_API_KEY=sk-testtoken1234567890 must be redacted");

        await RunCuratedDryRunAsync(temp.Path);
        var result = await RunProjectScriptAsync(
            "curated-retain-redacted-subset.ps1",
            $"-ProjectRoot {Quote(temp.Path)} -SourcePath AGENTS.md");

        Assert.True(result.ExitCode == 0, result.ToString());
        var reportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-redacted-subset-report.json");
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("ready_for_import", root.GetProperty("status").GetString());
        var redactedText = Assert.Single(root.GetProperty("files").EnumerateArray())
            .GetProperty("redacted_text")
            .GetString();
        Assert.NotNull(redactedText);
        Assert.False(redactedText.EndsWith('\n'));
    }

    [Fact]
    public async Task RedactedSubsetScriptPreservesUtf8BomAndCrLf()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        const string sourceText = "# Agents\r\nOPENAI_API_KEY=sk-testtoken1234567890\r\nsafe line\r\n";
        var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        File.WriteAllBytes(
            Path.Combine(temp.Path, "AGENTS.md"),
            utf8WithBom.GetPreamble().Concat(utf8WithBom.GetBytes(sourceText)).ToArray());

        await RunCuratedDryRunAsync(temp.Path);
        var result = await RunProjectScriptAsync(
            "curated-retain-redacted-subset.ps1",
            $"-ProjectRoot {Quote(temp.Path)} -SourcePath AGENTS.md");

        Assert.True(result.ExitCode == 0, result.ToString());
        var reportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-redacted-subset-report.json");
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("ready_for_import", root.GetProperty("status").GetString());
        var redactedText = Assert.Single(root.GetProperty("files").EnumerateArray())
            .GetProperty("redacted_text")
            .GetString();
        Assert.Equal("\uFEFF# Agents\r\n[REDACTED:secret_reference]\r\nsafe line\r\n", redactedText);
    }

    [Fact]
    public async Task RedactedSubsetScriptRejectsTraversalOutsideProjectBeforeReadingSource()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        var outsideFileName = $"outside-retain-{Guid.NewGuid():N}.md";
        var outsidePath = Path.Combine(Path.GetDirectoryName(temp.Path)!, outsideFileName);
        var escapedSourcePath = $"docs/memory/../../../{outsideFileName}";
        const string leakedPhrase = "outside-scope-content-must-not-enter-report";

        try
        {
            File.WriteAllText(outsidePath, $"redact this line\n{leakedPhrase}\n");
            await RunCuratedDryRunAsync(temp.Path);

            var dryRunPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-dry-run-report.json");
            var dryRun = JsonNode.Parse(File.ReadAllText(dryRunPath))!.AsObject();
            dryRun["files"]!.AsArray().Add(new JsonObject
            {
                ["path"] = escapedSourcePath,
                ["hash"] = ComputeSha256(outsidePath),
                ["size_bytes"] = new FileInfo(outsidePath).Length,
                ["redaction_status"] = "review_required",
                ["finding_count"] = 1,
            });
            dryRun["findings"]!.AsArray().Add(new JsonObject
            {
                ["source_path"] = escapedSourcePath,
                ["line"] = 1,
                ["type"] = "secret_reference",
            });
            dryRun["status"] = "review_required";
            File.WriteAllText(dryRunPath, dryRun.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var result = await RunProjectScriptAsync(
                "curated-retain-redacted-subset.ps1",
                $"-ProjectRoot {Quote(temp.Path)} -SourcePath {Quote(escapedSourcePath)}");

            Assert.True(result.ExitCode == 0, result.ToString());
            var reportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-redacted-subset-report.json");
            var reportText = File.ReadAllText(reportPath);
            using var report = JsonDocument.Parse(reportText);
            var root = report.RootElement;
            Assert.Equal("blocked", root.GetProperty("status").GetString());
            Assert.Contains(
                root.GetProperty("blocking_reasons").EnumerateArray().Select(reason => reason.GetString()),
                reason => reason == "source_outside_project");
            Assert.Empty(root.GetProperty("files").EnumerateArray());
            Assert.DoesNotContain(leakedPhrase, reportText, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    [Fact]
    public void ControlledRetainLifecycleScriptsAndDocsStayLocalOnly()
    {
        var exportScript = ReadText("scripts/curated-retain-export.ps1");
        var deleteScript = ReadText("scripts/curated-retain-delete.ps1");
        var policy = ReadText("docs/memory/retain-policy.md");
        var scriptsReadme = ReadText("scripts/README.md");

        Assert.Contains("retain-export", exportScript, StringComparison.Ordinal);
        Assert.Contains("curated-retain-export-report.json", exportScript, StringComparison.Ordinal);
        Assert.Contains("external_retain_enabled = $false", exportScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("codex_auto_retain_enabled = $false", exportScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hindsight retain", exportScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("codex retain", exportScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("memory-refresh-all", exportScript, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("retain-delete", deleteScript, StringComparison.Ordinal);
        Assert.Contains("curated-retain-delete-report.json", deleteScript, StringComparison.Ordinal);
        Assert.Contains("removes_files = $false", deleteScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("external_retain_enabled = $false", deleteScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("codex_auto_retain_enabled = $false", deleteScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hindsight retain", deleteScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("codex retain", deleteScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("memory-refresh-all", deleteScript, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("curated-retain-export.ps1", policy, StringComparison.Ordinal);
        Assert.Contains("curated-retain-delete.ps1", policy, StringComparison.Ordinal);
        Assert.Contains("absent from retain-search", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("curated-retain-export.ps1", scriptsReadme, StringComparison.Ordinal);
        Assert.Contains("curated-retain-delete.ps1", scriptsReadme, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LifecycleDryRunsBlockRetainWhenReportsAreMissingOrStale()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);

        var missingExportResult = await RunProjectScriptAsync(
            "curated-retain-export-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)}");
        Assert.True(missingExportResult.ExitCode == 0, missingExportResult.ToString());

        using (var missingReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-export-dry-run-report.json"))))
        {
            var root = missingReport.RootElement;
            Assert.Equal("blocked", root.GetProperty("status").GetString());
            Assert.False(root.GetProperty("curated_report_present").GetBoolean());
            Assert.False(root.GetProperty("retain_enablement_candidate").GetBoolean());
            Assert.False(root.GetProperty("external_retain_enabled").GetBoolean());
            Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
            AssertJsonArrayContainsAll(root.GetProperty("blocking_reasons"), "missing_curated_retain_report");
        }

        var missingDeleteResult = await RunProjectScriptAsync(
            "curated-retain-delete-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)} -ExportReportPath {Quote(Path.Combine(temp.Path, "docs", "memory", "generated", "missing-export-report.json"))}");
        Assert.True(missingDeleteResult.ExitCode == 0, missingDeleteResult.ToString());

        using (var missingDeleteReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-delete-dry-run-report.json"))))
        {
            var root = missingDeleteReport.RootElement;
            Assert.Equal("blocked", root.GetProperty("status").GetString());
            Assert.False(root.GetProperty("export_report_present").GetBoolean());
            Assert.False(root.GetProperty("retain_enablement_candidate").GetBoolean());
            Assert.False(root.GetProperty("external_retain_enabled").GetBoolean());
            Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
            AssertJsonArrayContainsAll(root.GetProperty("blocking_reasons"), "missing_curated_retain_export_report");
        }

        await RunCuratedDryRunAsync(temp.Path);
        File.AppendAllText(Path.Combine(temp.Path, "AGENTS.md"), "\nChanged after dry-run.\n");

        var staleExportResult = await RunProjectScriptAsync(
            "curated-retain-export-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)}");
        Assert.True(staleExportResult.ExitCode == 0, staleExportResult.ToString());

        using var staleReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-export-dry-run-report.json")));
        var staleRoot = staleReport.RootElement;
        Assert.Equal("blocked", staleRoot.GetProperty("status").GetString());
        Assert.True(staleRoot.GetProperty("stale_report").GetBoolean());
        Assert.True(staleRoot.GetProperty("summary").GetProperty("source_hash_mismatch_count").GetInt32() >= 1);
        Assert.False(staleRoot.GetProperty("retain_enablement_candidate").GetBoolean());
        Assert.False(staleRoot.GetProperty("external_retain_enabled").GetBoolean());
        Assert.False(staleRoot.GetProperty("codex_auto_retain_enabled").GetBoolean());
        AssertJsonArrayContainsAll(staleRoot.GetProperty("blocking_reasons"), "stale_source_metadata");
    }

    [Fact]
    public async Task LifecycleDryRunsRejectDenylistSourcesEvenIfInputReportContainsThem()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        temp.Write("recordings/live.jsonl", "{}\n");
        temp.Write("docs/memory/generated/unsafe-export.md", "# Generated\n");
        temp.Write(".hindsight/store.md", "# Hindsight\n");
        WriteCompromisedCuratedReport(temp);

        var exportResult = await RunProjectScriptAsync(
            "curated-retain-export-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)}");
        Assert.True(exportResult.ExitCode == 0, exportResult.ToString());

        using (var exportReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-export-dry-run-report.json"))))
        {
            var root = exportReport.RootElement;
            Assert.Equal("blocked", root.GetProperty("status").GetString());
            Assert.False(root.GetProperty("imports_denylist").GetBoolean());
            Assert.True(root.GetProperty("summary").GetProperty("denied_source_count").GetInt32() >= 3);
            AssertJsonArrayContainsAll(root.GetProperty("blocking_reasons"), "denied_sources_in_input_report");

            var sourcePaths = root.GetProperty("sources")
                .EnumerateArray()
                .Select(source => source.GetProperty("source_path").GetString()!)
                .ToArray();
            AssertNoDeniedPaths(sourcePaths);

            var invalidPaths = root.GetProperty("invalid_sources")
                .EnumerateArray()
                .Select(source => source.GetProperty("source_path").GetString()!)
                .ToArray();
            Assert.Contains("recordings/live.jsonl", invalidPaths);
            Assert.Contains("docs/memory/generated/unsafe-export.md", invalidPaths);
            Assert.Contains(".hindsight/store.md", invalidPaths);
        }

        var deleteResult = await RunProjectScriptAsync(
            "curated-retain-delete-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)}");
        Assert.True(deleteResult.ExitCode == 0, deleteResult.ToString());

        using var deleteReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-delete-dry-run-report.json")));
        var deleteRoot = deleteReport.RootElement;
        Assert.False(deleteRoot.GetProperty("deletes_items").GetBoolean());
        Assert.True(deleteRoot.GetProperty("summary").GetProperty("denied_source_count").GetInt32() >= 3);
        AssertJsonArrayContainsAll(deleteRoot.GetProperty("blocking_reasons"), "denied_sources_in_export_report");
        var deleteSourcePaths = deleteRoot.GetProperty("sources")
            .EnumerateArray()
            .Select(source => source.GetProperty("source_path").GetString()!)
            .ToArray();
        AssertNoDeniedPaths(deleteSourcePaths);
    }

    private static readonly string[] ExpectedAllowlist =
    [
        "AGENTS.md",
        "docs/decisions/*.md",
        "docs/formulas.md",
        "TC-DN-HOFI3.md",
        "docs/memory/*.md",
        "tasks/lessons.md",
    ];

    private static readonly string[] ExpectedDenylist =
    [
        "recordings/*.jsonl",
        "docs/memory/generated/",
        ".hindsight/",
        "secrets",
        "bin/",
        "obj/",
        "publish/",
        "local proxy details",
        "raw experiment dumps",
    ];

    private static string ReadText(string relativePath)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(path);
    }

    private static async Task<string> RunCuratedDryRunAsync(string projectRoot)
    {
        var result = await RunProjectScriptAsync(
            "curated-retain-dry-run.ps1",
            $"-ProjectRoot {Quote(projectRoot)}");
        Assert.True(result.ExitCode == 0, result.ToString());

        var reportPath = Path.Combine(projectRoot, "docs", "memory", "generated", "curated-retain-dry-run-report.json");
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");
        return reportPath;
    }

    private static async Task<ProcessResult> RunProjectScriptAsync(string scriptName, string arguments)
    {
        var scriptPath = Path.Combine(Root, "scripts", scriptName);
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");
        return await RunPowerShellAsync(scriptPath, arguments);
    }

    private static void WriteMinimumCuratedSources(TemporaryDirectory temp)
    {
        temp.Write("CryptoIndicatorApp.sln", string.Empty);
        temp.Write("AGENTS.md", "# Agents\n\nNo secret values here.\n");
        temp.Write("TC-DN-HOFI3.md", "# Formula Source\n");
        temp.Write("docs/formulas.md", "# Formulas\n");
        temp.Write("tasks/lessons.md", "# Lessons\n");
        temp.Write("docs/decisions/0008-curated-retain-and-memory-lifecycle-policy.md", "# ADR 0008\n");
        temp.Write("docs/memory/contract.md", "# Contract\n");
        temp.Write("docs/memory/retain-policy.md", "# Retain Policy\n");
    }

    private static void WriteCompromisedCuratedReport(TemporaryDirectory temp)
    {
        var generatedPath = Path.Combine(temp.Path, "docs", "memory", "generated");
        Directory.CreateDirectory(generatedPath);

        var report = new
        {
            schema_version = 1,
            generated_at = DateTimeOffset.UtcNow.ToString("o"),
            generator = "scripts/curated-retain-dry-run.ps1",
            mode = "dry-run",
            status = "ready_for_review",
            files = new object[]
            {
                new
                {
                    path = "AGENTS.md",
                    hash = ComputeSha256(Path.Combine(temp.Path, "AGENTS.md")),
                    size_bytes = new FileInfo(Path.Combine(temp.Path, "AGENTS.md")).Length,
                    redaction_status = "candidate",
                    finding_count = 0,
                },
                new
                {
                    path = "recordings/live.jsonl",
                    hash = ComputeSha256(Path.Combine(temp.Path, "recordings", "live.jsonl")),
                    size_bytes = new FileInfo(Path.Combine(temp.Path, "recordings", "live.jsonl")).Length,
                    redaction_status = "candidate",
                    finding_count = 0,
                },
                new
                {
                    path = "docs/memory/generated/unsafe-export.md",
                    hash = ComputeSha256(Path.Combine(temp.Path, "docs", "memory", "generated", "unsafe-export.md")),
                    size_bytes = new FileInfo(Path.Combine(temp.Path, "docs", "memory", "generated", "unsafe-export.md")).Length,
                    redaction_status = "candidate",
                    finding_count = 0,
                },
                new
                {
                    path = ".hindsight/store.md",
                    hash = ComputeSha256(Path.Combine(temp.Path, ".hindsight", "store.md")),
                    size_bytes = new FileInfo(Path.Combine(temp.Path, ".hindsight", "store.md")).Length,
                    redaction_status = "candidate",
                    finding_count = 0,
                },
            },
            findings = Array.Empty<object>(),
        };

        File.WriteAllText(
            Path.Combine(generatedPath, "curated-retain-dry-run-report.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
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

    private static async Task<ProcessResult> RunPowerShellAsync(string scriptPath, string arguments)
    {
        var memoryToolDll = FindMemoryToolDll();
        Assert.True(File.Exists(memoryToolDll), $"Missing prebuilt memory tool: {memoryToolDll}");
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["CRYPTO_MEMORY_TOOL_DLL"] = memoryToolDll;
        var process = Process.Start(startInfo);

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

            Assert.Fail($"{Path.GetFileName(scriptPath)} timed out.");
        }

        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string PowerShellLiteral(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
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

    private static string FindMemoryToolDll()
    {
        var targetFrameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var configuration = targetFrameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the test build configuration.");
        return Path.Combine(
            Root,
            "tools",
            "Memory",
            "bin",
            configuration,
            "net8.0",
            "CryptoIndicatorApp.Memory.dll");
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error)
    {
        public override string ToString()
        {
            return $"Exit {ExitCode}\nSTDOUT:\n{Output}\nSTDERR:\n{Error}";
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "curated-retain-policy-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Write(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
