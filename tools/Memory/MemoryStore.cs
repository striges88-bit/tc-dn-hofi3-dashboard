using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace CryptoIndicatorApp.Memory;

public sealed class MemoryStore : IDisposable
{
    private const int SchemaVersion = 1;
    private readonly SqliteConnection _connection;

    public MemoryStore(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection($"Data Source={databasePath}");
        _connection.Open();
    }

    public RefreshResult Refresh(ProjectMemorySnapshot snapshot)
    {
        RecreateSchema();
        using var transaction = _connection.BeginTransaction();
        var metadata = snapshot.Metadata;

        SetMetadata("refresh_source", metadata.RefreshSource, transaction);
        SetMetadata("commit_sha", metadata.CommitSha, transaction);
        SetMetadata("tree_sha", metadata.TreeSha, transaction);
        SetMetadata("indexed_at", metadata.IndexedAt, transaction);

        foreach (var file in snapshot.Files)
        {
            Execute(
                """
                INSERT INTO files(path, hash, size_bytes, commit_sha, tree_sha, source_blob_sha, indexed_at)
                VALUES ($path, $hash, $size, $commit, $tree, $blob, $indexed)
                """,
                transaction,
                ("$path", file.Path),
                ("$hash", file.Hash),
                ("$size", file.SizeBytes),
                ("$commit", metadata.CommitSha),
                ("$tree", metadata.TreeSha),
                ("$blob", SourceBlobSha(metadata, file.Path)),
                ("$indexed", metadata.IndexedAt));
            Execute(
                """
                INSERT INTO sources(id, source_path, source_hash, source_blob_sha, commit_sha, tree_sha, indexed_at, updated_at)
                VALUES ($id, $path, $hash, $blob, $commit, $tree, $indexed, $updated)
                """,
                transaction,
                ("$id", $"source.{file.Path}"),
                ("$path", file.Path),
                ("$hash", file.Hash),
                ("$blob", SourceBlobSha(metadata, file.Path)),
                ("$commit", metadata.CommitSha),
                ("$tree", metadata.TreeSha),
                ("$indexed", metadata.IndexedAt),
                ("$updated", metadata.IndexedAt));
        }

        foreach (var document in snapshot.SearchDocuments)
        {
            InsertSearchDocument(document, metadata, transaction);
        }

        foreach (var rule in snapshot.Rules)
        {
            Execute(
                """
                INSERT INTO rules(id, status, active_scope, text, source_path, source_hash, source_blob_sha, commit_sha, tree_sha, indexed_at, updated_at)
                VALUES ($id, $status, $scope, $text, $source, $hash, $blob, $commit, $tree, $indexed, $updated)
                """,
                transaction,
                ("$id", rule.Id),
                ("$status", rule.Status),
                ("$scope", rule.ActiveScope),
                ("$text", rule.Text),
                ("$source", rule.SourcePath),
                ("$hash", rule.SourceHash),
                ("$blob", SourceBlobSha(metadata, rule.SourcePath)),
                ("$commit", metadata.CommitSha),
                ("$tree", metadata.TreeSha),
                ("$indexed", metadata.IndexedAt),
                ("$updated", metadata.IndexedAt));
        }

        foreach (var adr in snapshot.Adrs)
        {
            Execute(
                """
                INSERT INTO adr(id, status, title, text, source_path, source_hash, source_blob_sha, commit_sha, tree_sha, indexed_at, updated_at)
                VALUES ($id, $status, $title, $text, $source, $hash, $blob, $commit, $tree, $indexed, $updated)
                """,
                transaction,
                ("$id", adr.Id),
                ("$status", adr.Status),
                ("$title", adr.Title),
                ("$text", adr.Text),
                ("$source", adr.SourcePath),
                ("$hash", adr.SourceHash),
                ("$blob", SourceBlobSha(metadata, adr.SourcePath)),
                ("$commit", metadata.CommitSha),
                ("$tree", metadata.TreeSha),
                ("$indexed", metadata.IndexedAt),
                ("$updated", metadata.IndexedAt));
        }

        foreach (var formula in snapshot.FormulaVersions)
        {
            Execute(
                """
                INSERT INTO formula_versions(id, status, owner, text, source_path, source_hash, source_blob_sha, commit_sha, tree_sha, indexed_at, updated_at)
                VALUES ($id, $status, $owner, $text, $source, $hash, $blob, $commit, $tree, $indexed, $updated)
                """,
                transaction,
                ("$id", formula.Id),
                ("$status", formula.Status),
                ("$owner", formula.Owner),
                ("$text", formula.Text),
                ("$source", formula.SourcePath),
                ("$hash", formula.SourceHash),
                ("$blob", SourceBlobSha(metadata, formula.SourcePath)),
                ("$commit", metadata.CommitSha),
                ("$tree", metadata.TreeSha),
                ("$indexed", metadata.IndexedAt),
                ("$updated", metadata.IndexedAt));
        }

        foreach (var symbol in snapshot.Symbols)
        {
            Execute(
                """
                INSERT INTO symbols(symbol, kind, display_name, parent_symbol, source_path, source_hash, commit_sha, tree_sha, source_blob_sha, indexed_at, updated_at)
                VALUES ($symbol, $kind, $display, $parent, $source, $hash, $commit, $tree, $blob, $indexed, $updated)
                """,
                transaction,
                ("$symbol", symbol.Symbol),
                ("$kind", symbol.Kind),
                ("$display", symbol.DisplayName),
                ("$parent", symbol.ParentSymbol),
                ("$source", symbol.SourcePath),
                ("$hash", symbol.SourceHash),
                ("$commit", metadata.CommitSha),
                ("$tree", metadata.TreeSha),
                ("$blob", SourceBlobSha(metadata, symbol.SourcePath)),
                ("$indexed", metadata.IndexedAt),
                ("$updated", metadata.IndexedAt));
        }

        foreach (var experiment in snapshot.Experiments)
        {
            Execute(
                """
                INSERT INTO experiments(id, status, outcome, source_path, source_hash, source_blob_sha, commit_sha, tree_sha, indexed_at, updated_at)
                VALUES ($id, $status, $outcome, $source, $hash, $blob, $commit, $tree, $indexed, $updated)
                """,
                transaction,
                ("$id", experiment.Id),
                ("$status", experiment.Status),
                ("$outcome", experiment.Outcome),
                ("$source", experiment.SourcePath),
                ("$hash", experiment.SourceHash),
                ("$blob", SourceBlobSha(metadata, experiment.SourcePath)),
                ("$commit", metadata.CommitSha),
                ("$tree", metadata.TreeSha),
                ("$indexed", metadata.IndexedAt),
                ("$updated", metadata.IndexedAt));
        }

        foreach (var todo in snapshot.Todos)
        {
            Execute(
                """
                INSERT INTO todos(id, status, text, source_path, source_hash, source_blob_sha, commit_sha, tree_sha, indexed_at, updated_at)
                VALUES ($id, $status, $text, $source, $hash, $blob, $commit, $tree, $indexed, $updated)
                """,
                transaction,
                ("$id", todo.Id),
                ("$status", todo.Status),
                ("$text", todo.Text),
                ("$source", todo.SourcePath),
                ("$hash", todo.SourceHash),
                ("$blob", SourceBlobSha(metadata, todo.SourcePath)),
                ("$commit", metadata.CommitSha),
                ("$tree", metadata.TreeSha),
                ("$indexed", metadata.IndexedAt),
                ("$updated", metadata.IndexedAt));
        }

        foreach (var memoryEvent in snapshot.Events)
        {
            Execute(
                """
                INSERT INTO events(id, event_type, symbol, text, source_path, source_hash, source_blob_sha, commit_sha, tree_sha, indexed_at, updated_at)
                VALUES ($id, $type, $symbol, $text, $source, $hash, $blob, $commit, $tree, $indexed, $updated)
                """,
                transaction,
                ("$id", memoryEvent.Id),
                ("$type", memoryEvent.EventType),
                ("$symbol", memoryEvent.Symbol),
                ("$text", memoryEvent.Text),
                ("$source", memoryEvent.SourcePath),
                ("$hash", memoryEvent.SourceHash),
                ("$blob", SourceBlobSha(metadata, memoryEvent.SourcePath)),
                ("$commit", metadata.CommitSha),
                ("$tree", metadata.TreeSha),
                ("$indexed", metadata.IndexedAt),
                ("$updated", metadata.IndexedAt));
        }

        foreach (var relation in snapshot.Relations)
        {
            Execute(
                """
                INSERT INTO relations(id, from_id, relation, to_id, text, source_path, source_hash, source_blob_sha, commit_sha, tree_sha, indexed_at, updated_at)
                VALUES ($id, $from, $relation, $to, $text, $source, $hash, $blob, $commit, $tree, $indexed, $updated)
                """,
                transaction,
                ("$id", relation.Id),
                ("$from", relation.FromId),
                ("$relation", relation.Relation),
                ("$to", relation.ToId),
                ("$text", relation.Text),
                ("$source", relation.SourcePath),
                ("$hash", relation.SourceHash),
                ("$blob", SourceBlobSha(metadata, relation.SourcePath)),
                ("$commit", metadata.CommitSha),
                ("$tree", metadata.TreeSha),
                ("$indexed", metadata.IndexedAt),
                ("$updated", metadata.IndexedAt));
        }

        transaction.Commit();

        return new RefreshResult(
            SchemaVersion,
            "sqlite-fts5",
            "lancedb-fastembed-local-candidate",
            "historical-failed",
            metadata.RefreshSource,
            metadata.CommitSha,
            metadata.TreeSha,
            metadata.IndexedAt,
            metadata.SourceBlobShas.Count,
            snapshot.Files.Count,
            GetTableNames(),
            snapshot.Files.Select(file => file.Path).Order(StringComparer.Ordinal).ToArray());
    }

    public SearchResult Search(string query)
    {
        return new SearchResult(query, SearchInternal(query, explainPlan: null, logQuery: true, out _));
    }

    public RetainImportResult RetainImport(RetainImportBatch import)
    {
        EnsureRetainSchema();

        if (import.BlockingReasons.Count > 0)
        {
            return NewRetainImportResult("blocked", import, []);
        }

        using var transaction = _connection.BeginTransaction();
        foreach (var item in import.Items)
        {
            Execute(
                "DELETE FROM retained_items_fts WHERE id IN (SELECT id FROM retained_items WHERE source_path = $source)",
                transaction,
                ("$source", item.SourcePath));
            Execute(
                "DELETE FROM retained_items WHERE source_path = $source",
                transaction,
                ("$source", item.SourcePath));
            Execute(
                """
                INSERT INTO retained_items(id, source_path, source_hash, source_blob_sha, commit_sha, tree_sha, provider, redaction_status, retained_at, text)
                VALUES ($id, $source, $hash, $blob, $commit, $tree, $provider, $redaction, $retained, $text)
                """,
                transaction,
                ("$id", item.Id),
                ("$source", item.SourcePath),
                ("$hash", item.SourceHash),
                ("$blob", item.SourceBlobSha),
                ("$commit", item.CommitSha),
                ("$tree", item.TreeSha),
                ("$provider", item.Provider),
                ("$redaction", item.RedactionStatus),
                ("$retained", item.RetainedAt),
                ("$text", item.Text));
            Execute(
                "INSERT INTO retained_items_fts(id, text, source_path) VALUES ($id, $text, $source)",
                transaction,
                ("$id", item.Id),
                ("$text", item.Text),
                ("$source", item.SourcePath));
        }

        transaction.Commit();
        return NewRetainImportResult("imported", import, import.Items);
    }

    public RetainSearchResult RetainSearch(string query)
    {
        EnsureRetainSchema();
        var ftsQuery = BuildFtsQuery(query);
        var results = new List<RetainSearchHit>();
        using var command = CreateCommand(
            """
            SELECT r.id, r.source_path, r.commit_sha, r.provider, r.redaction_status, bm25(retained_items_fts) AS rank
            FROM retained_items_fts
            JOIN retained_items r ON r.id = retained_items_fts.id
            WHERE retained_items_fts MATCH $query
            ORDER BY rank, r.source_path, r.id
            LIMIT 10
            """);
        command.Parameters.AddWithValue("$query", ftsQuery);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new RetainSearchHit(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDouble(5)));
        }

        return new RetainSearchResult(query, results);
    }

    public RetainExportResult RetainExport(string outputPath)
    {
        EnsureRetainSchema();
        var items = new List<RetainExportItem>();
        using var command = CreateCommand(
            """
            SELECT id, source_path, source_hash, source_blob_sha, commit_sha, tree_sha, provider, redaction_status, retained_at, text
            FROM retained_items
            ORDER BY source_path, id
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new RetainExportItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9)));
        }

        return new RetainExportResult(
            "retain-export",
            "exported",
            outputPath,
            items.Count,
            SourceContentIncluded: true,
            ExternalRetainEnabled: false,
            CodexAutoRetainEnabled: false,
            CloudEnabled: false,
            CallsHindsight: false,
            CallsCodexRetain: false,
            InstallsHooks: false,
            RunsRefreshAll: false,
            RebuildsMemory: false,
            items);
    }

    public RetainDeleteResult RetainDelete(string sourcePath)
    {
        EnsureRetainSchema();
        sourcePath = sourcePath.Replace('\\', '/');
        var ids = new List<string>();
        using (var select = CreateCommand("SELECT id FROM retained_items WHERE source_path = $source"))
        {
            select.Parameters.AddWithValue("$source", sourcePath);
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
        }

        using var transaction = _connection.BeginTransaction();
        foreach (var id in ids)
        {
            Execute("DELETE FROM retained_items_fts WHERE id = $id", transaction, ("$id", id));
        }

        Execute("DELETE FROM retained_items WHERE source_path = $source", transaction, ("$source", sourcePath));
        transaction.Commit();

        return new RetainDeleteResult(
            "retain-delete",
            "deleted",
            sourcePath,
            ids.Count,
            DeletesItems: ids.Count > 0,
            RemovesFiles: false,
            ExternalRetainEnabled: false,
            CodexAutoRetainEnabled: false,
            CloudEnabled: false,
            CallsHindsight: false,
            CallsCodexRetain: false,
            InstallsHooks: false,
            RunsRefreshAll: false,
            RebuildsMemory: false);
    }

    public ExplainResult Explain(string query)
    {
        var plan = ExplainPlan(query);
        var stopwatch = Stopwatch.StartNew();
        var results = SearchInternal(query, plan, logQuery: true, out var logRows);
        stopwatch.Stop();
        return new ExplainResult("EXPLAIN QUERY PLAN", query, plan, results, (decimal)stopwatch.Elapsed.TotalMilliseconds, logRows);
    }

    public MemoryStatusResult Status(string projectRoot)
    {
        var head = GitCommitMemoryIndexer.ReadHead(projectRoot);
        var indexedCommit = GetMetadata("commit_sha");
        var indexedTree = GetMetadata("tree_sha");
        var indexedAt = GetMetadata("indexed_at");
        var markerPath = MemoryRefreshMarker.GetMarkerPath(projectRoot);
        var markerExists = File.Exists(markerPath);
        var needsRefresh = markerExists
            || string.IsNullOrWhiteSpace(indexedCommit)
            || (!string.IsNullOrWhiteSpace(head) && !indexedCommit.Equals(head, StringComparison.OrdinalIgnoreCase));

        return new MemoryStatusResult(
            head,
            indexedCommit,
            indexedTree,
            indexedAt,
            markerExists,
            needsRefresh,
            GitCommitMemoryIndexer.IsWorkingTreeDirty(projectRoot),
            ToRepoPath(projectRoot, markerPath));
    }

    public StaleCheckResult StaleCheck(string projectRoot)
    {
        var issues = new List<StaleIssue>();

        using (var command = CreateCommand("SELECT id, source_path, source_hash, source_blob_sha, commit_sha FROM search_documents"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var sourcePath = reader.GetString(1);
                var sourceHash = reader.GetString(2);
                var sourceBlobSha = reader.IsDBNull(3) ? null : reader.GetString(3);
                var commitSha = reader.IsDBNull(4) ? null : reader.GetString(4);

                if (!string.IsNullOrWhiteSpace(commitSha))
                {
                    if (string.IsNullOrWhiteSpace(sourceBlobSha))
                    {
                        issues.Add(new StaleIssue("missing_source_blob_sha", id, sourcePath, "Commit-addressed source has no source_blob_sha."));
                        continue;
                    }

                    var currentBlobSha = GitCommitMemoryIndexer.ReadBlobSha(projectRoot, commitSha, sourcePath);
                    if (string.IsNullOrWhiteSpace(currentBlobSha))
                    {
                        issues.Add(new StaleIssue("missing_source", id, sourcePath, "Source file does not exist in indexed commit."));
                    }
                    else if (!currentBlobSha.Equals(sourceBlobSha, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new StaleIssue("source_blob_mismatch", id, sourcePath, "Source blob changed for indexed commit."));
                    }

                    continue;
                }

                var fullPath = Path.Combine(projectRoot, sourcePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    issues.Add(new StaleIssue("missing_source", id, sourcePath, "Source file no longer exists."));
                    continue;
                }

                var currentHash = HashFile(fullPath);
                if (!currentHash.Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new StaleIssue("source_hash_mismatch", id, sourcePath, "Source file hash changed after indexing."));
                }
            }
        }

        AddSimpleIssues(
            issues,
            "SELECT id, source_path FROM formula_versions WHERE status = 'current' AND (owner IS NULL OR trim(owner) = '')",
            "formula_missing_owner",
            "Current formula_version has no owner.");
        AddSimpleIssues(
            issues,
            "SELECT id, source_path FROM rules WHERE status IN ('current', 'proposed') AND (active_scope IS NULL OR trim(active_scope) = '')",
            "rule_missing_active_scope",
            "Rule has no active scope.");
        AddSimpleIssues(
            issues,
            """
            SELECT e.id, e.source_path
            FROM events e
            LEFT JOIN symbols s ON s.symbol = e.symbol
            WHERE e.event_type = 'test_symbol_reference'
              AND e.symbol IS NOT NULL
              AND s.symbol IS NULL
            """,
            "unknown_symbol_reference",
            "Test references a symbol that is not present in symbols.");

        return new StaleCheckResult(issues);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private void RecreateSchema()
    {
        foreach (var statement in MemorySchema.RecreateStatements)
        {
            Execute(statement);
        }
    }

    private static RetainImportResult NewRetainImportResult(
        string status,
        RetainImportBatch import,
        IReadOnlyList<RetainedMemoryItem> importedItems)
    {
        return new RetainImportResult(
            "retain-import",
            status,
            import.InputReportPath,
            import.CommitSha,
            import.TreeSha,
            import.Items.Count,
            importedItems.Count,
            import.BlockingReasons,
            ExternalRetainEnabled: false,
            CodexAutoRetainEnabled: false,
            CloudEnabled: false,
            CallsHindsight: false,
            CallsCodexRetain: false,
            InstallsHooks: false,
            RunsRefreshAll: false,
            RebuildsMemory: false,
            importedItems
                .Select(item => new RetainImportItemResult(
                    item.Id,
                    item.SourcePath,
                    item.SourceHash,
                    item.SourceBlobSha,
                    item.CommitSha,
                    item.Provider,
                    item.RedactionStatus))
                .ToArray());
    }

    private void EnsureRetainSchema()
    {
        Execute(
            """
            CREATE TABLE IF NOT EXISTS retained_items(
                id TEXT PRIMARY KEY,
                source_path TEXT NOT NULL,
                source_hash TEXT NOT NULL,
                source_blob_sha TEXT NOT NULL,
                commit_sha TEXT NOT NULL,
                tree_sha TEXT NOT NULL,
                provider TEXT NOT NULL,
                redaction_status TEXT NOT NULL,
                retained_at TEXT NOT NULL,
                text TEXT NOT NULL
            )
            """);
        Execute(
            """
            CREATE VIRTUAL TABLE IF NOT EXISTS retained_items_fts USING fts5(
                id UNINDEXED,
                text,
                source_path UNINDEXED
            )
            """);
    }

    private void InsertSearchDocument(SearchDocument document, MemorySnapshotMetadata metadata, SqliteTransaction transaction)
    {
        try
        {
            Execute(
                """
                INSERT INTO search_documents(id, type, status, title, body, source_path, source_hash, source_blob_sha, commit_sha, tree_sha, confidence, valid_from, valid_until, indexed_at, updated_at)
                VALUES ($id, $type, $status, $title, $body, $source, $hash, $blob, $commit, $tree, $confidence, $validFrom, $validUntil, $indexed, $updated)
                """,
                transaction,
                ("$id", document.Id),
                ("$type", document.Type),
                ("$status", document.Status),
                ("$title", document.Title),
                ("$body", document.Body),
                ("$source", document.SourcePath),
                ("$hash", document.SourceHash),
                ("$blob", SourceBlobSha(metadata, document.SourcePath)),
                ("$commit", metadata.CommitSha),
                ("$tree", metadata.TreeSha),
                ("$confidence", document.Confidence),
                ("$validFrom", document.ValidFrom),
                ("$validUntil", document.ValidUntil),
                ("$indexed", metadata.IndexedAt),
                ("$updated", metadata.IndexedAt));
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                $"Duplicate search document id '{document.Id}' from '{document.SourcePath}'.",
                exception);
        }

        Execute(
            """
            INSERT INTO search_documents_fts(id, title, body, type, status, source_path)
            VALUES ($id, $title, $body, $type, $status, $source)
            """,
            transaction,
            ("$id", document.Id),
            ("$title", document.Title),
            ("$body", document.Body),
            ("$type", document.Type),
            ("$status", document.Status),
            ("$source", document.SourcePath));
        if (document.Type.Equals("chunk", StringComparison.Ordinal))
        {
            Execute(
                """
                INSERT INTO chunks(id, file_path, ordinal, text, source_path, source_hash, source_blob_sha, commit_sha, tree_sha, indexed_at, status)
                VALUES ($id, $file, $ordinal, $text, $source, $hash, $blob, $commit, $tree, $indexed, $status)
                """,
                transaction,
                ("$id", document.Id),
                ("$file", document.SourcePath),
                ("$ordinal", ExtractChunkOrdinal(document.Id)),
                ("$text", document.Body),
                ("$source", document.SourcePath),
                ("$hash", document.SourceHash),
                ("$blob", SourceBlobSha(metadata, document.SourcePath)),
                ("$commit", metadata.CommitSha),
                ("$tree", metadata.TreeSha),
                ("$indexed", metadata.IndexedAt),
                ("$status", document.Status));
        }
    }

    private IReadOnlyList<SearchHit> SearchInternal(string query, IReadOnlyList<string>? explainPlan, bool logQuery, out int logRows)
    {
        var ftsQuery = BuildFtsQuery(query);
        var stopwatch = Stopwatch.StartNew();
        var results = new List<SearchHit>();
        try
        {
            using var command = CreateSearchCommand(ftsQuery);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new SearchHit(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetDouble(5)));
            }

            stopwatch.Stop();
            logRows = logQuery ? LogQuery("search", query, stopwatch.Elapsed.TotalMilliseconds, results.Count, explainPlan, "ok", null) : CountQueryLogRows();
            return results;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logRows = logQuery ? LogQuery("search", query, stopwatch.Elapsed.TotalMilliseconds, 0, explainPlan, "error", exception.Message) : CountQueryLogRows();
            throw;
        }
    }

    private List<string> ExplainPlan(string query)
    {
        var ftsQuery = BuildFtsQuery(query);
        var plan = new List<string>();
        using var command = CreateCommand(
            """
            EXPLAIN QUERY PLAN
            SELECT d.id, d.type, d.status, d.title, d.source_path, bm25(search_documents_fts) AS rank
            FROM search_documents_fts
            JOIN search_documents d ON d.id = search_documents_fts.id
            WHERE search_documents_fts MATCH $query
              AND d.status IN ('current', 'proposed')
            ORDER BY CASE d.type WHEN 'chunk' THEN 1 ELSE 0 END, rank, d.type, d.id
            LIMIT 10
            """);
        command.Parameters.AddWithValue("$query", ftsQuery);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            plan.Add(reader.GetString(3));
        }

        return plan;
    }

    private SqliteCommand CreateSearchCommand(string ftsQuery)
    {
        var command = CreateCommand(
            """
            SELECT d.id, d.type, d.status, d.title, d.source_path, bm25(search_documents_fts) AS rank
            FROM search_documents_fts
            JOIN search_documents d ON d.id = search_documents_fts.id
            WHERE search_documents_fts MATCH $query
              AND d.status IN ('current', 'proposed')
            ORDER BY CASE d.type WHEN 'chunk' THEN 1 ELSE 0 END, rank, d.type, d.id
            LIMIT 10
            """);
        command.Parameters.AddWithValue("$query", ftsQuery);
        return command;
    }

    private static string BuildFtsQuery(string query)
    {
        var tokens = Regex.Matches(query, "[\\p{L}\\p{N}_]+")
            .Select(match => match.Value)
            .Where(token => token.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(token => $"\"{token.Replace("\"", "\"\"")}\"")
            .ToArray();

        if (tokens.Length == 0)
        {
            throw new InvalidOperationException("Query must contain at least one searchable token.");
        }

        return string.Join(" OR ", tokens);
    }

    private int LogQuery(
        string commandName,
        string query,
        double durationMs,
        int rowCount,
        IReadOnlyList<string>? explainPlan,
        string status,
        string? errorMessage)
    {
        Execute(
            """
            INSERT INTO query_log(executed_at, command, query_text, parameters_hash, duration_ms, row_count, explain_plan, status, error_message)
            VALUES ($executed, $command, $query, $hash, $duration, $rows, $plan, $status, $error)
            """,
            transaction: null,
            ("$executed", Now()),
            ("$command", commandName),
            ("$query", query),
            ("$hash", Sha256(query)),
            ("$duration", durationMs),
            ("$rows", rowCount),
            ("$plan", explainPlan is null ? null : JsonSerializer.Serialize(explainPlan)),
            ("$status", status),
            ("$error", errorMessage));
        return CountQueryLogRows();
    }

    private void AddSimpleIssues(List<StaleIssue> issues, string sql, string code, string message)
    {
        using var command = CreateCommand(sql);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            issues.Add(new StaleIssue(code, reader.GetString(0), reader.GetString(1), message));
        }
    }

    private List<string> GetTableNames()
    {
        using var command = CreateCommand("SELECT name FROM sqlite_master WHERE type IN ('table', 'virtual') ORDER BY name");
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            if (!name.StartsWith("search_documents_fts_", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("retained_items_fts_", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private int CountQueryLogRows()
    {
        using var command = CreateCommand("SELECT COUNT(*) FROM query_log");
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private string? GetMetadata(string key)
    {
        if (!TableExists("memory_metadata"))
        {
            return null;
        }

        using var command = CreateCommand("SELECT value FROM memory_metadata WHERE key = $key");
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private void SetMetadata(string key, string? value, SqliteTransaction transaction)
    {
        Execute(
            "INSERT INTO memory_metadata(key, value) VALUES ($key, $value)",
            transaction,
            ("$key", key),
            ("$value", value));
    }

    private bool TableExists(string tableName)
    {
        using var command = CreateCommand("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name");
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private SqliteCommand CreateCommand(string sql)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private void Execute(string sql, SqliteTransaction? transaction = null, params (string Name, object? Value)[] parameters)
    {
        using var command = CreateCommand(sql);
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private static string Now()
    {
        return DateTimeOffset.UtcNow.ToString("O");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Sha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? SourceBlobSha(MemorySnapshotMetadata metadata, string sourcePath)
    {
        return metadata.SourceBlobShas.TryGetValue(sourcePath, out var blobSha) ? blobSha : null;
    }

    private static string ToRepoPath(string projectRoot, string path)
    {
        var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return fullPath[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
    }

    private static int ExtractChunkOrdinal(string id)
    {
        var lastDot = id.LastIndexOf('.');
        return lastDot >= 0 && int.TryParse(id[(lastDot + 1)..], out var ordinal) ? ordinal : 0;
    }
}
