using System.Diagnostics;
using System.Text.Json;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class MemoryContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void ContractDefinesMemoryBoundariesAndSourcePriority()
    {
        var contract = ReadText("docs/memory/contract.md");

        Assert.Contains("code/tests/config", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AGENTS.md", contract, StringComparison.Ordinal);
        Assert.Contains("docs/memory/generated/", contract, StringComparison.Ordinal);
        Assert.Contains("not application runtime", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manual refresh", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("git post-commit hook", contract, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeneratedMemorySchemaRequiresFreshnessMetadataAndAllowedTypes()
    {
        using var document = JsonDocument.Parse(ReadText("docs/memory/generated-memory.schema.json"));
        var root = document.RootElement;

        var nodeSchema = root
            .GetProperty("definitions")
            .GetProperty("node");

        AssertJsonArrayContainsAll(
            nodeSchema.GetProperty("required"),
            "id",
            "type",
            "status",
            "source_path",
            "source_hash",
            "created_at",
            "updated_at",
            "confidence");

        var nodeProperties = nodeSchema.GetProperty("properties");
        AssertJsonArrayContainsAll(
            nodeProperties.GetProperty("type").GetProperty("enum"),
            "module",
            "type",
            "formula",
            "rule",
            "decision",
            "experiment",
            "open_question",
            "data_source",
            "config_option");

        AssertJsonArrayContainsAll(
            nodeProperties.GetProperty("status").GetProperty("enum"),
            "current",
            "proposed",
            "superseded",
            "failed");

        var edgeSchema = root
            .GetProperty("definitions")
            .GetProperty("edge");

        AssertJsonArrayContainsAll(
            edgeSchema.GetProperty("properties").GetProperty("relation").GetProperty("enum"),
            "depends_on",
            "owns",
            "feeds",
            "calculates",
            "records",
            "replays",
            "guards",
            "supersedes",
            "contradicts",
            "observed_in");
    }

    [Fact]
    public void KnownRetrievalFactsAreGroundedInHumanSources()
    {
        Assert.Contains(
            "REST must not be used for subsecond feature calculation",
            ReadText("docs/data-sources.md"),
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "canonical source",
            ReadText("docs/formulas.md"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TC-DN-HOFI3.md", ReadText("docs/formulas.md"), StringComparison.Ordinal);

        Assert.Contains(
            "Do not reference `Infrastructure` from `Application`",
            ReadText("docs/architecture.md"),
            StringComparison.Ordinal);

        Assert.Contains("recordings/*.jsonl", ReadText(".gitignore"), StringComparison.Ordinal);
        Assert.Contains(
            "Binance DTOs",
            ReadText("docs/architecture.md"),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContractDefinesStalenessRulesForGeneratedAndExperimentalFacts()
    {
        var contract = ReadText("docs/memory/contract.md");

        Assert.Contains("source_path", contract, StringComparison.Ordinal);
        Assert.Contains("source_hash", contract, StringComparison.Ordinal);
        Assert.Contains("superseded", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("experiment outcome", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("formula decision", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("freshness check", contract, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GBrainSpikeRecordsUpstreamCliAndLocalAvailability()
    {
        var spike = ReadText("docs/memory/gbrain-spike.md");
        var contract = ReadText("docs/memory/contract.md");
        var openQuestions = ReadText("docs/memory/open-questions.md");

        Assert.Contains("historical/secondary candidate", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hindsight replaced GBrain", spike, StringComparison.Ordinal);
        Assert.Contains("garrytan/gbrain", spike, StringComparison.Ordinal);
        Assert.Contains("gbrain init --pglite", spike, StringComparison.Ordinal);
        Assert.Contains("codex mcp add gbrain -- gbrain serve", spike, StringComparison.Ordinal);
        Assert.Contains("Bun `>=1.3.10`", spike, StringComparison.Ordinal);
        Assert.Contains("where.exe gbrain", spike, StringComparison.Ordinal);
        Assert.Contains("not found", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not currently usable as a local project tool", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docs/memory/gbrain-spike.md", contract, StringComparison.Ordinal);
        Assert.Contains("historical/secondary candidate", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local Windows install", openQuestions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HindsightSpikeSetsPreferredExternalMemoryWithoutAutoRetain()
    {
        var spike = ReadText("docs/memory/hindsight-spike.md");
        var contract = ReadText("docs/memory/contract.md");
        var readme = ReadText("docs/memory/README.md");
        var openQuestions = ReadText("docs/memory/open-questions.md");
        var gitignore = ReadText(".gitignore");

        Assert.Contains("vectorize-io/hindsight", spike, StringComparison.Ordinal);
        Assert.Contains("preferred external memory candidate", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Python `>=3.11`", spike, StringComparison.Ordinal);
        Assert.Contains("hindsight memory retain-files", spike, StringComparison.Ordinal);
        Assert.Contains("/mcp/{bank_id}/", spike, StringComparison.Ordinal);
        Assert.Contains("SessionStart", spike, StringComparison.Ordinal);
        Assert.Contains("UserPromptSubmit", spike, StringComparison.Ordinal);
        Assert.Contains("Stop", spike, StringComparison.Ordinal);
        Assert.Contains("where.exe hindsight", spike, StringComparison.Ordinal);
        Assert.Contains("python --version", spike, StringComparison.Ordinal);
        Assert.Contains("uvx hindsight-embed --help", spike, StringComparison.Ordinal);
        Assert.Contains("port `8888`", spike, StringComparison.Ordinal);
        Assert.Contains("profile named `tc-dn-hofi3`", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("port `9077`", spike, StringComparison.Ordinal);
        Assert.Contains("/mcp/", spike, StringComparison.Ordinal);
        Assert.Contains("billing_not_active", spike, StringComparison.Ordinal);
        Assert.Contains("separate Rust `hindsight` CLI", spike, StringComparison.Ordinal);
        Assert.Contains("not installed locally", spike, StringComparison.Ordinal);
        Assert.Contains("auto-installer failed", spike, StringComparison.Ordinal);
        Assert.Contains("Do not enable Codex auto-retain during MVP", spike, StringComparison.Ordinal);
        Assert.Contains("docs/memory/*.md", spike, StringComparison.Ordinal);
        Assert.Contains("docs/decisions/*.md", spike, StringComparison.Ordinal);
        Assert.Contains("docs/formulas.md", spike, StringComparison.Ordinal);
        Assert.Contains("AGENTS.md", spike, StringComparison.Ordinal);
        Assert.Contains("tasks/lessons.md", spike, StringComparison.Ordinal);
        Assert.Contains("Do not import raw JSONL recordings", spike, StringComparison.Ordinal);

        Assert.Contains("Hindsight is the preferred external semantic memory candidate", contract, StringComparison.Ordinal);
        Assert.Contains("docs/memory/hindsight-spike.md", contract, StringComparison.Ordinal);
        Assert.Contains("Codex auto-retain must stay disabled during MVP", contract, StringComparison.Ordinal);
        Assert.Contains("docs/memory/*.md", contract, StringComparison.Ordinal);
        Assert.Contains("Hindsight", readme, StringComparison.Ordinal);
        Assert.Contains("historical/secondary candidate", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uvx hindsight-embed --help", openQuestions, StringComparison.Ordinal);
        Assert.Contains("tc-dn-hofi3", openQuestions, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:9077", openQuestions, StringComparison.Ordinal);
        Assert.Contains("billing_not_active", openQuestions, StringComparison.Ordinal);
        Assert.Contains("auto-retain must remain disabled during MVP", openQuestions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".hindsight/", gitignore, StringComparison.Ordinal);
        Assert.Contains("*.hindsight", gitignore, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshScriptBuildsIgnoredGeneratedIndexWithRequiredMetadata()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-refresh.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ProjectRoot \"{Root}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        Assert.NotNull(process);
        Assert.True(process!.WaitForExit(TimeSpan.FromSeconds(30)), "memory-refresh.ps1 timed out.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}\nSTDOUT:\n{output}\nSTDERR:\n{error}");

        var indexPath = Path.Combine(Root, "docs", "memory", "generated", "project-memory-index.json");
        Assert.True(File.Exists(indexPath), $"Missing generated index: {indexPath}");
        Assert.Contains("docs/memory/generated/", ReadText(".gitignore"), StringComparison.Ordinal);

        using var index = JsonDocument.Parse(File.ReadAllText(indexPath));
        var nodes = index.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
        var edges = index.RootElement.GetProperty("edges").EnumerateArray().ToArray();

        Assert.Contains(nodes, node => node.GetProperty("id").GetString() == "data-source.hot-path");
        Assert.Contains(nodes, node => node.GetProperty("id").GetString() == "formula.canonical");
        Assert.Contains(nodes, node => node.GetProperty("id").GetString() == "architecture.application-boundary");
        Assert.Contains(nodes, node => node.GetProperty("id").GetString() == "recordings.raw-jsonl");
        Assert.Contains(edges, edge => edge.GetProperty("relation").GetString() == "guards");

        foreach (var node in nodes)
        {
            AssertRequiredString(node, "id");
            AssertRequiredString(node, "type");
            AssertRequiredString(node, "status");
            AssertRequiredString(node, "source_path");
            AssertRequiredString(node, "source_hash");
            AssertRequiredString(node, "created_at");
            AssertRequiredString(node, "updated_at");
            AssertRequiredNumber(node, "confidence");
            Assert.Equal(JsonValueKind.Null, node.GetProperty("valid_until").ValueKind);
        }

        var indexedPaths = index.RootElement
            .GetProperty("source_files")
            .EnumerateArray()
            .Select(sourceFile => sourceFile.GetProperty("path").GetString()!)
            .ToArray();

        Assert.DoesNotContain(indexedPaths, path => ContainsPathSegment(path, "bin"));
        Assert.DoesNotContain(indexedPaths, path => ContainsPathSegment(path, "obj"));
        Assert.DoesNotContain(indexedPaths, path => ContainsPathSegment(path, "publish"));
        Assert.DoesNotContain(indexedPaths, path => path.StartsWith("docs/memory/generated/", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadText(string relativePath)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(path);
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

    private static void AssertRequiredString(JsonElement node, string propertyName)
    {
        Assert.True(node.TryGetProperty(propertyName, out var value), $"Missing {propertyName}.");
        Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"Empty {propertyName}.");
    }

    private static void AssertRequiredNumber(JsonElement node, string propertyName)
    {
        Assert.True(node.TryGetProperty(propertyName, out var value), $"Missing {propertyName}.");
        Assert.Equal(JsonValueKind.Number, value.ValueKind);
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

    private static bool ContainsPathSegment(string path, string segment)
    {
        return path.Split('/').Contains(segment, StringComparer.OrdinalIgnoreCase);
    }
}
