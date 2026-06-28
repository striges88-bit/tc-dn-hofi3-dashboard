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
