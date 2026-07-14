using Microsoft.Data.Sqlite;

namespace CryptoIndicatorApp.Memory;

internal static class LegacyRetainedStoreMigration
{
    public static int MigrateIfNeeded(string canonicalDatabasePath, string retainedDatabasePath)
    {
        if (Path.GetFullPath(canonicalDatabasePath).Equals(
                Path.GetFullPath(retainedDatabasePath),
                StringComparison.OrdinalIgnoreCase)
            || !File.Exists(canonicalDatabasePath))
        {
            return 0;
        }

        using var legacyConnection = Open(canonicalDatabasePath);
        var legacyItems = ReadCurrentLegacyItems(legacyConnection);
        if (legacyItems.Count == 0)
        {
            return 0;
        }

        if (ContainsRetainedItems(retainedDatabasePath))
        {
            throw new InvalidOperationException(
                "Both project-memory.sqlite and project-retained.sqlite contain retained items. "
                + "Resolve the local stores explicitly before continuing; no automatic version choice was made.");
        }

        using (var retainedStore = new MemoryStore(retainedDatabasePath))
        {
            var import = retainedStore.RetainImport(new RetainImportBatch(
                "legacy:project-memory.sqlite",
                string.Empty,
                string.Empty,
                [],
                legacyItems));
            if (import.ImportedCount != legacyItems.Count)
            {
                throw new InvalidOperationException("Legacy retained-item migration did not import every current source.");
            }
        }

        DropLegacyTables(legacyConnection);
        return legacyItems.Count;
    }

    private static List<RetainedMemoryItem> ReadCurrentLegacyItems(SqliteConnection connection)
    {
        if (!TableExists(connection, "retained_items"))
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, source_path, source_hash, source_blob_sha, commit_sha, tree_sha, provider, redaction_status, retained_at, text
            FROM retained_items
            ORDER BY source_path, retained_at DESC, id DESC
            """;
        using var reader = command.ExecuteReader();
        var currentBySource = new Dictionary<string, RetainedMemoryItem>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var item = new RetainedMemoryItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9));
            currentBySource.TryAdd(item.SourcePath, item);
        }

        return currentBySource.Values
            .OrderBy(item => item.SourcePath, StringComparer.Ordinal)
            .ToList();
    }

    private static bool ContainsRetainedItems(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return false;
        }

        using var connection = Open(databasePath);
        if (!TableExists(connection, "retained_items"))
        {
            return false;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM retained_items LIMIT 1)";
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static void DropLegacyTables(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DROP TABLE IF EXISTS retained_items_fts;
            DROP TABLE IF EXISTS retained_items;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name)";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        return connection;
    }
}
