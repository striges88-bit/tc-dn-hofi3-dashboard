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

        foreach (var file in snapshot.Files)
        {
            Execute(
                "INSERT INTO files(path, hash, size_bytes, indexed_at) VALUES ($path, $hash, $size, $indexed)",
                transaction,
                ("$path", file.Path),
                ("$hash", file.Hash),
                ("$size", file.SizeBytes),
                ("$indexed", Now()));
            Execute(
                "INSERT INTO sources(id, source_path, source_hash, updated_at) VALUES ($id, $path, $hash, $updated)",
                transaction,
                ("$id", $"source.{file.Path}"),
                ("$path", file.Path),
                ("$hash", file.Hash),
                ("$updated", Now()));
        }

        foreach (var document in snapshot.SearchDocuments)
        {
            InsertSearchDocument(document, transaction);
        }

        foreach (var rule in snapshot.Rules)
        {
            Execute(
                """
                INSERT INTO rules(id, status, active_scope, text, source_path, source_hash, updated_at)
                VALUES ($id, $status, $scope, $text, $source, $hash, $updated)
                """,
                transaction,
                ("$id", rule.Id),
                ("$status", rule.Status),
                ("$scope", rule.ActiveScope),
                ("$text", rule.Text),
                ("$source", rule.SourcePath),
                ("$hash", rule.SourceHash),
                ("$updated", Now()));
        }

        foreach (var adr in snapshot.Adrs)
        {
            Execute(
                """
                INSERT INTO adr(id, status, title, text, source_path, source_hash, updated_at)
                VALUES ($id, $status, $title, $text, $source, $hash, $updated)
                """,
                transaction,
                ("$id", adr.Id),
                ("$status", adr.Status),
                ("$title", adr.Title),
                ("$text", adr.Text),
                ("$source", adr.SourcePath),
                ("$hash", adr.SourceHash),
                ("$updated", Now()));
        }

        foreach (var formula in snapshot.FormulaVersions)
        {
            Execute(
                """
                INSERT INTO formula_versions(id, status, owner, text, source_path, source_hash, updated_at)
                VALUES ($id, $status, $owner, $text, $source, $hash, $updated)
                """,
                transaction,
                ("$id", formula.Id),
                ("$status", formula.Status),
                ("$owner", formula.Owner),
                ("$text", formula.Text),
                ("$source", formula.SourcePath),
                ("$hash", formula.SourceHash),
                ("$updated", Now()));
        }

        foreach (var symbol in snapshot.Symbols)
        {
            Execute(
                "INSERT INTO symbols(symbol, source_path, source_hash, updated_at) VALUES ($symbol, $source, $hash, $updated)",
                transaction,
                ("$symbol", symbol.Symbol),
                ("$source", symbol.SourcePath),
                ("$hash", symbol.SourceHash),
                ("$updated", Now()));
        }

        foreach (var memoryEvent in snapshot.Events)
        {
            Execute(
                """
                INSERT INTO events(id, event_type, symbol, text, source_path, source_hash, updated_at)
                VALUES ($id, $type, $symbol, $text, $source, $hash, $updated)
                """,
                transaction,
                ("$id", memoryEvent.Id),
                ("$type", memoryEvent.EventType),
                ("$symbol", memoryEvent.Symbol),
                ("$text", memoryEvent.Text),
                ("$source", memoryEvent.SourcePath),
                ("$hash", memoryEvent.SourceHash),
                ("$updated", Now()));
        }

        foreach (var relation in snapshot.Relations)
        {
            Execute(
                """
                INSERT INTO relations(id, from_id, relation, to_id, text, source_path, source_hash, updated_at)
                VALUES ($id, $from, $relation, $to, $text, $source, $hash, $updated)
                """,
                transaction,
                ("$id", relation.Id),
                ("$from", relation.FromId),
                ("$relation", relation.Relation),
                ("$to", relation.ToId),
                ("$text", relation.Text),
                ("$source", relation.SourcePath),
                ("$hash", relation.SourceHash),
                ("$updated", Now()));
        }

        transaction.Commit();

        return new RefreshResult(
            SchemaVersion,
            "sqlite-fts5",
            "lancedb-active-local-spike",
            "historical-failed",
            snapshot.Files.Count,
            GetTableNames(),
            snapshot.Files.Select(file => file.Path).Order(StringComparer.Ordinal).ToArray());
    }

    public SearchResult Search(string query)
    {
        return new SearchResult(query, SearchInternal(query, explainPlan: null, logQuery: true, out _));
    }

    public ExplainResult Explain(string query)
    {
        var plan = ExplainPlan(query);
        var stopwatch = Stopwatch.StartNew();
        var results = SearchInternal(query, plan, logQuery: true, out var logRows);
        stopwatch.Stop();
        return new ExplainResult("EXPLAIN QUERY PLAN", query, plan, results, (decimal)stopwatch.Elapsed.TotalMilliseconds, logRows);
    }

    public StaleCheckResult StaleCheck(string projectRoot)
    {
        var issues = new List<StaleIssue>();

        using (var command = CreateCommand("SELECT id, source_path, source_hash FROM search_documents"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var sourcePath = reader.GetString(1);
                var sourceHash = reader.GetString(2);
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

    private void InsertSearchDocument(SearchDocument document, SqliteTransaction transaction)
    {
        Execute(
            """
            INSERT INTO search_documents(id, type, status, title, body, source_path, source_hash, confidence, valid_from, valid_until, updated_at)
            VALUES ($id, $type, $status, $title, $body, $source, $hash, $confidence, $validFrom, $validUntil, $updated)
            """,
            transaction,
            ("$id", document.Id),
            ("$type", document.Type),
            ("$status", document.Status),
            ("$title", document.Title),
            ("$body", document.Body),
            ("$source", document.SourcePath),
            ("$hash", document.SourceHash),
            ("$confidence", document.Confidence),
            ("$validFrom", document.ValidFrom),
            ("$validUntil", document.ValidUntil),
            ("$updated", Now()));
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
                INSERT INTO chunks(id, file_path, ordinal, text, source_path, source_hash, status)
                VALUES ($id, $file, $ordinal, $text, $source, $hash, $status)
                """,
                transaction,
                ("$id", document.Id),
                ("$file", document.SourcePath),
                ("$ordinal", ExtractChunkOrdinal(document.Id)),
                ("$text", document.Body),
                ("$source", document.SourcePath),
                ("$hash", document.SourceHash),
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

    private static int ExtractChunkOrdinal(string id)
    {
        var lastDot = id.LastIndexOf('.');
        return lastDot >= 0 && int.TryParse(id[(lastDot + 1)..], out var ordinal) ? ordinal : 0;
    }
}
