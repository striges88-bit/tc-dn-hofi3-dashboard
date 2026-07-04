namespace CryptoIndicatorApp.Memory;

internal static class MemorySchema
{
    public static IReadOnlyList<string> RecreateStatements { get; } =
    [
        "PRAGMA journal_mode = WAL",
        "DROP TABLE IF EXISTS files",
        "DROP TABLE IF EXISTS symbols",
        "DROP TABLE IF EXISTS chunks",
        "DROP TABLE IF EXISTS rules",
        "DROP TABLE IF EXISTS adr",
        "DROP TABLE IF EXISTS formula_versions",
        "DROP TABLE IF EXISTS metrics",
        "DROP TABLE IF EXISTS experiments",
        "DROP TABLE IF EXISTS events",
        "DROP TABLE IF EXISTS relations",
        "DROP TABLE IF EXISTS sources",
        "DROP TABLE IF EXISTS todos",
        "DROP TABLE IF EXISTS retained_items",
        "DROP TABLE IF EXISTS search_documents",
        "DROP TABLE IF EXISTS query_log",
        "DROP TABLE IF EXISTS memory_metadata",
        "DROP TABLE IF EXISTS retained_items_fts",
        "DROP TABLE IF EXISTS search_documents_fts",
        """
        CREATE TABLE files(
            path TEXT PRIMARY KEY,
            hash TEXT NOT NULL,
            size_bytes INTEGER NOT NULL,
            commit_sha TEXT NULL,
            tree_sha TEXT NULL,
            source_blob_sha TEXT NULL,
            indexed_at TEXT NOT NULL
        )
        """,
        "CREATE TABLE symbols(symbol TEXT PRIMARY KEY, kind TEXT NOT NULL, display_name TEXT NOT NULL, parent_symbol TEXT NULL, source_path TEXT NOT NULL, source_hash TEXT NOT NULL, commit_sha TEXT NULL, tree_sha TEXT NULL, source_blob_sha TEXT NULL, indexed_at TEXT NOT NULL, updated_at TEXT NOT NULL)",
        "CREATE TABLE chunks(id TEXT PRIMARY KEY, file_path TEXT NOT NULL, ordinal INTEGER NOT NULL, text TEXT NOT NULL, source_path TEXT NOT NULL, source_hash TEXT NOT NULL, source_blob_sha TEXT NULL, commit_sha TEXT NULL, tree_sha TEXT NULL, indexed_at TEXT NOT NULL, status TEXT NOT NULL)",
        "CREATE TABLE rules(id TEXT PRIMARY KEY, status TEXT NOT NULL, active_scope TEXT NULL, text TEXT NOT NULL, source_path TEXT NOT NULL, source_hash TEXT NOT NULL, source_blob_sha TEXT NULL, commit_sha TEXT NULL, tree_sha TEXT NULL, indexed_at TEXT NOT NULL, updated_at TEXT NOT NULL)",
        "CREATE TABLE adr(id TEXT PRIMARY KEY, status TEXT NOT NULL, title TEXT NOT NULL, text TEXT NOT NULL, source_path TEXT NOT NULL, source_hash TEXT NOT NULL, source_blob_sha TEXT NULL, commit_sha TEXT NULL, tree_sha TEXT NULL, indexed_at TEXT NOT NULL, updated_at TEXT NOT NULL)",
        "CREATE TABLE formula_versions(id TEXT PRIMARY KEY, status TEXT NOT NULL, owner TEXT NULL, text TEXT NOT NULL, source_path TEXT NOT NULL, source_hash TEXT NOT NULL, source_blob_sha TEXT NULL, commit_sha TEXT NULL, tree_sha TEXT NULL, indexed_at TEXT NOT NULL, updated_at TEXT NOT NULL)",
        "CREATE TABLE metrics(id TEXT PRIMARY KEY, name TEXT NOT NULL, value TEXT NULL, source_path TEXT NULL, source_hash TEXT NULL, source_blob_sha TEXT NULL, commit_sha TEXT NULL, tree_sha TEXT NULL, indexed_at TEXT NOT NULL, updated_at TEXT NOT NULL)",
        "CREATE TABLE experiments(id TEXT PRIMARY KEY, status TEXT NOT NULL, outcome TEXT NULL, source_path TEXT NULL, source_hash TEXT NULL, source_blob_sha TEXT NULL, commit_sha TEXT NULL, tree_sha TEXT NULL, indexed_at TEXT NOT NULL, updated_at TEXT NOT NULL)",
        "CREATE TABLE events(id TEXT PRIMARY KEY, event_type TEXT NOT NULL, symbol TEXT NULL, text TEXT NOT NULL, source_path TEXT NOT NULL, source_hash TEXT NOT NULL, source_blob_sha TEXT NULL, commit_sha TEXT NULL, tree_sha TEXT NULL, indexed_at TEXT NOT NULL, updated_at TEXT NOT NULL)",
        "CREATE TABLE relations(id TEXT PRIMARY KEY, from_id TEXT NOT NULL, relation TEXT NOT NULL, to_id TEXT NOT NULL, text TEXT NOT NULL, source_path TEXT NOT NULL, source_hash TEXT NOT NULL, source_blob_sha TEXT NULL, commit_sha TEXT NULL, tree_sha TEXT NULL, indexed_at TEXT NOT NULL, updated_at TEXT NOT NULL)",
        "CREATE TABLE sources(id TEXT PRIMARY KEY, source_path TEXT NOT NULL, source_hash TEXT NOT NULL, source_blob_sha TEXT NULL, commit_sha TEXT NULL, tree_sha TEXT NULL, indexed_at TEXT NOT NULL, updated_at TEXT NOT NULL)",
        "CREATE TABLE todos(id TEXT PRIMARY KEY, status TEXT NOT NULL, text TEXT NOT NULL, source_path TEXT NOT NULL, source_hash TEXT NOT NULL, source_blob_sha TEXT NULL, commit_sha TEXT NULL, tree_sha TEXT NULL, indexed_at TEXT NOT NULL, updated_at TEXT NOT NULL)",
        """
        CREATE TABLE retained_items(
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
        """,
        """
        CREATE TABLE search_documents(
            id TEXT PRIMARY KEY,
            type TEXT NOT NULL,
            status TEXT NOT NULL,
            title TEXT NOT NULL,
            body TEXT NOT NULL,
            source_path TEXT NOT NULL,
            source_hash TEXT NOT NULL,
            source_blob_sha TEXT NULL,
            commit_sha TEXT NULL,
            tree_sha TEXT NULL,
            confidence REAL NOT NULL,
            valid_from TEXT NULL,
            valid_until TEXT NULL,
            indexed_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        )
        """,
        """
        CREATE VIRTUAL TABLE search_documents_fts USING fts5(
            id UNINDEXED,
            title,
            body,
            type UNINDEXED,
            status UNINDEXED,
            source_path UNINDEXED
        )
        """,
        """
        CREATE VIRTUAL TABLE retained_items_fts USING fts5(
            id UNINDEXED,
            text,
            source_path UNINDEXED
        )
        """,
        """
        CREATE TABLE query_log(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            executed_at TEXT NOT NULL,
            command TEXT NOT NULL,
            query_text TEXT NOT NULL,
            parameters_hash TEXT NOT NULL,
            duration_ms REAL NOT NULL,
            row_count INTEGER NOT NULL,
            explain_plan TEXT NULL,
            status TEXT NOT NULL,
            error_message TEXT NULL
        )
        """,
        """
        CREATE TABLE memory_metadata(
            key TEXT PRIMARY KEY,
            value TEXT NULL
        )
        """
    ];
}
