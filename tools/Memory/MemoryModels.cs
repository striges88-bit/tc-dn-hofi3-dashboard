namespace CryptoIndicatorApp.Memory;

public sealed record ProjectMemorySnapshot(
    IReadOnlyList<IndexedFile> Files,
    IReadOnlyList<SearchDocument> SearchDocuments,
    IReadOnlyList<RuleRecord> Rules,
    IReadOnlyList<AdrRecord> Adrs,
    IReadOnlyList<FormulaVersionRecord> FormulaVersions,
    IReadOnlyList<SymbolRecord> Symbols,
    IReadOnlyList<EventRecord> Events,
    IReadOnlyList<RelationRecord> Relations,
    MemorySnapshotMetadata Metadata);

public sealed record MemorySnapshotMetadata(
    string RefreshSource,
    string? CommitSha,
    string? TreeSha,
    IReadOnlyDictionary<string, string> SourceBlobShas,
    string IndexedAt)
{
    public static MemorySnapshotMetadata ForWorkingTree(string indexedAt)
    {
        return new MemorySnapshotMetadata(
            "working-tree",
            null,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            indexedAt);
    }
}

public sealed record IndexedFile(string Path, string Hash, long SizeBytes);

public sealed record SearchDocument(
    string Id,
    string Type,
    string Status,
    string Title,
    string Body,
    string SourcePath,
    string SourceHash,
    double Confidence,
    string? ValidFrom,
    string? ValidUntil);

public sealed record RuleRecord(
    string Id,
    string Status,
    string? ActiveScope,
    string Text,
    string SourcePath,
    string SourceHash);

public sealed record AdrRecord(
    string Id,
    string Status,
    string Title,
    string Text,
    string SourcePath,
    string SourceHash);

public sealed record FormulaVersionRecord(
    string Id,
    string Status,
    string? Owner,
    string Text,
    string SourcePath,
    string SourceHash);

public sealed record SymbolRecord(string Symbol, string SourcePath, string SourceHash);

public sealed record EventRecord(
    string Id,
    string EventType,
    string? Symbol,
    string Text,
    string SourcePath,
    string SourceHash);

public sealed record RelationRecord(
    string Id,
    string FromId,
    string Relation,
    string ToId,
    string Text,
    string SourcePath,
    string SourceHash);

public sealed record RefreshResult(
    int SchemaVersion,
    string CanonicalStore,
    string SemanticSidecar,
    string HindsightStatus,
    string RefreshSource,
    string? CommitSha,
    string? TreeSha,
    string IndexedAt,
    int SourceBlobShaCount,
    int IndexedFiles,
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> IndexedPaths);

public sealed record SearchResult(string Query, IReadOnlyList<SearchHit> Results);

public sealed record SearchHit(
    string Id,
    string Type,
    string Status,
    string Title,
    string SourcePath,
    double Rank);

public sealed record ExplainResult(
    string Diagnostic,
    string Query,
    IReadOnlyList<string> ExplainPlan,
    IReadOnlyList<SearchHit> Results,
    decimal DurationMs,
    int QueryLogRows);

public sealed record StaleCheckResult(IReadOnlyList<StaleIssue> Issues);

public sealed record StaleIssue(string Code, string Id, string SourcePath, string Message);

public sealed record MemoryStatusResult(
    string? Head,
    string? IndexedCommit,
    string? IndexedTree,
    string? IndexedAt,
    bool MarkerExists,
    bool NeedsRefresh,
    bool WorkingTreeDirty,
    string MarkerPath);

public sealed record RetainImportBatch(
    string InputReportPath,
    string CommitSha,
    string TreeSha,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<RetainedMemoryItem> Items);

public sealed record RetainedMemoryItem(
    string Id,
    string SourcePath,
    string SourceHash,
    string SourceBlobSha,
    string CommitSha,
    string TreeSha,
    string Provider,
    string RedactionStatus,
    string RetainedAt,
    string Text);

public sealed record RetainImportResult(
    string Mode,
    string Status,
    string InputReportPath,
    string CommitSha,
    string TreeSha,
    int CandidateCount,
    int ImportedCount,
    IReadOnlyList<string> BlockingReasons,
    bool ExternalRetainEnabled,
    bool CodexAutoRetainEnabled,
    bool CloudEnabled,
    bool CallsHindsight,
    bool CallsCodexRetain,
    bool InstallsHooks,
    bool RunsRefreshAll,
    bool RebuildsMemory,
    IReadOnlyList<RetainImportItemResult> Items);

public sealed record RetainImportItemResult(
    string Id,
    string SourcePath,
    string SourceHash,
    string SourceBlobSha,
    string CommitSha,
    string Provider,
    string RedactionStatus);

public sealed record RetainSearchResult(string Query, IReadOnlyList<RetainSearchHit> Results);

public sealed record RetainSearchHit(
    string Id,
    string SourcePath,
    string CommitSha,
    string Provider,
    string RedactionStatus,
    double Rank);
