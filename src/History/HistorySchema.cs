using System;
using System.Collections.Generic;
using MySqlConnector;
using Serilog;

namespace SheetMan.History
{
    /// <summary>
    /// The history's tables, and the migration that brings a database up to them.
    ///
    /// Applied on connect rather than by a separate step. A build machine that has just
    /// been pointed at a fresh database should record its first snapshot rather than fail
    /// with an instruction to run something else, and the alternative - a migration tool
    /// somebody has to remember - is how a schema and the code that reads it drift.
    ///
    /// Migrations are additive and run once, guarded by the versions recorded in
    /// `schema_version` rather than by each statement being safe to repeat. Two build
    /// machines connecting at once is normal, so the whole thing runs inside a named lock:
    /// without one, both would read the same version and both would run the same
    /// statements, and one would fail on a race rather than on anything real.
    ///
    /// An applied migration is never edited. It is tempting while a schema is still new -
    /// nothing has shipped, so why not - but every database created during that development
    /// is already at that version and will never see the change. The column below is
    /// migration 2 rather than a line added to migration 1 for exactly that reason.
    /// </summary>
    internal static class HistorySchema
    {
        /// <summary>
        /// What this build expects. A database at a higher version was written by a newer
        /// SheetMan and is left alone rather than downgraded.
        /// </summary>
        public const int Version = 2;

        private const string LockName = "sheetman_history_migrate";

        private const int LockTimeoutSeconds = 60;

        /// <summary>
        /// Brings the database up to <see cref="Version"/>, or throws saying why it cannot.
        /// </summary>
        public static void Migrate(MySqlConnection connection)
        {
            if (!TryLock(connection))
            {
                throw new SheetManException(
                    $"Another process has been migrating the history database for more than " +
                    $"{LockTimeoutSeconds} seconds. If nothing else is running, the lock is stale " +
                    $"and will clear when its connection closes.");
            }

            try
            {
                Execute(connection, @"
                    CREATE TABLE IF NOT EXISTS schema_version (
                        version    INT          NOT NULL,
                        applied_at DATETIME(3)  NOT NULL,
                        PRIMARY KEY (version)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

                int current = CurrentVersion(connection);

                if (current > Version)
                {
                    throw new SheetManException(
                        $"The history database is at schema version {current}, and this build of " +
                        $"SheetMan understands version {Version}. Upgrade SheetMan rather than " +
                        $"letting an older one write to it.");
                }

                if (current == Version)
                    return;

                Log.Information($"Migrating the history database from version {current} to {Version}.");

                for (int version = current + 1; version <= Version; version++)
                {
                    foreach (var statement in Migrations[version])
                        Execute(connection, statement);

                    Execute(connection,
                        "INSERT INTO schema_version (version, applied_at) VALUES (@v, UTC_TIMESTAMP(3))",
                        ("@v", version));
                }
            }
            finally
            {
                Execute(connection, "SELECT RELEASE_LOCK(@name)", ("@name", LockName));
            }
        }

        private static bool TryLock(MySqlConnection connection)
        {
            using var command = new MySqlCommand("SELECT GET_LOCK(@name, @timeout)", connection);

            command.Parameters.AddWithValue("@name", LockName);
            command.Parameters.AddWithValue("@timeout", LockTimeoutSeconds);

            return Convert.ToInt32(command.ExecuteScalar() ?? 0) == 1;
        }

        private static int CurrentVersion(MySqlConnection connection)
        {
            using var command = new MySqlCommand(
                "SELECT COALESCE(MAX(version), 0) FROM schema_version", connection);

            return Convert.ToInt32(command.ExecuteScalar() ?? 0);
        }

        private static void Execute(MySqlConnection connection, string sql, params (string Name, object Value)[] args)
        {
            using var command = new MySqlCommand(sql, connection);

            foreach (var (name, value) in args)
                command.Parameters.AddWithValue(name, value);

            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Statements per version.
        ///
        /// Two decisions run through all of it.
        ///
        /// A row key is stored twice: as text for reading, and as a hash for indexing. A
        /// primary index can be a long string, and an index on a column long enough to hold
        /// one exceeds what InnoDB will key on - so keying on the hash removes the length
        /// limit rather than imposing one on the data.
        ///
        /// Values live in a pool addressed by their content. Planning data repeats itself
        /// enormously; storing each cell's text inline would store the string `0` some
        /// millions of times.
        /// </summary>
        private static readonly Dictionary<int, string[]> Migrations = new Dictionary<int, string[]>
        {
            [1] = new[]
            {
                @"CREATE TABLE IF NOT EXISTS project (
                    id          INT          NOT NULL AUTO_INCREMENT,
                    project_key VARCHAR(128) NOT NULL,
                    PRIMARY KEY (id),
                    UNIQUE KEY uq_project (project_key)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS value (
                    id   BIGINT     NOT NULL AUTO_INCREMENT,
                    hash BINARY(32) NOT NULL,
                    text LONGTEXT   NOT NULL,
                    PRIMARY KEY (id),
                    UNIQUE KEY uq_value (hash)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS snapshot (
                    id            BIGINT       NOT NULL AUTO_INCREMENT,
                    project_id    INT          NOT NULL,
                    branch        VARCHAR(100) NOT NULL,
                    commit_hash   VARCHAR(128) NOT NULL,
                    seq           BIGINT       NOT NULL,
                    parent_id     BIGINT       NULL,
                    model_hash    CHAR(64)     NOT NULL,
                    author_name   VARCHAR(190) NULL,
                    author_email  VARCHAR(190) NULL,
                    committed_at  DATETIME(3)  NULL,
                    subject       TEXT         NULL,
                    dirty         TINYINT(1)   NOT NULL,
                    attributable  TINYINT(1)   NOT NULL,
                    converted_at  DATETIME(3)  NOT NULL,
                    converted_by  VARCHAR(190) NULL,
                    tool_version  VARCHAR(64)  NULL,
                    recipe        VARCHAR(255) NULL,
                    summary       LONGBLOB     NOT NULL,
                    PRIMARY KEY (id),
                    UNIQUE KEY uq_snapshot (project_id, branch, commit_hash),
                    KEY ix_chain (project_id, branch, seq)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS snapshot_stat (
                    snapshot_id      BIGINT NOT NULL,
                    tables           INT    NOT NULL,
                    rows_count       INT    NOT NULL,
                    fields           INT    NOT NULL,
                    cells            BIGINT NOT NULL,
                    empty_cells      BIGINT NOT NULL,
                    content_bytes    BIGINT NOT NULL,
                    enums            INT    NOT NULL,
                    enum_labels      INT    NOT NULL,
                    constant_sets    INT    NOT NULL,
                    constants        INT    NOT NULL,
                    reference_fields INT    NOT NULL,
                    array_fields     INT    NOT NULL,
                    PRIMARY KEY (snapshot_id)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS table_stat (
                    snapshot_id      BIGINT       NOT NULL,
                    table_name       VARCHAR(128) NOT NULL,
                    row_count        INT          NOT NULL,
                    field_count      INT          NOT NULL,
                    cell_count       BIGINT       NOT NULL,
                    empty_cell_count BIGINT       NOT NULL,
                    content_bytes    BIGINT       NOT NULL,
                    table_hash       CHAR(64)     NOT NULL,
                    schema_hash      CHAR(64)     NOT NULL,
                    PRIMARY KEY (snapshot_id, table_name)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS table_current (
                    project_id  INT          NOT NULL,
                    branch      VARCHAR(100) NOT NULL,
                    table_name  VARCHAR(128) NOT NULL,
                    table_hash  CHAR(64)     NOT NULL,
                    schema_hash CHAR(64)     NOT NULL,
                    snapshot_id BIGINT       NOT NULL,
                    PRIMARY KEY (project_id, branch, table_name)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS field_current (
                    project_id  INT          NOT NULL,
                    branch      VARCHAR(100) NOT NULL,
                    table_name  VARCHAR(128) NOT NULL,
                    field_name  VARCHAR(128) NOT NULL,
                    field_hash  CHAR(64)     NOT NULL,
                    descriptor  TEXT         NOT NULL,
                    ordinal     INT          NOT NULL,
                    snapshot_id BIGINT       NOT NULL,
                    PRIMARY KEY (project_id, branch, table_name, field_name)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS row_current (
                    project_id   INT          NOT NULL,
                    branch       VARCHAR(100) NOT NULL,
                    table_name   VARCHAR(128) NOT NULL,
                    row_key_hash BINARY(32)   NOT NULL,
                    row_key      TEXT         NOT NULL,
                    row_hash     CHAR(64)     NOT NULL,
                    snapshot_id  BIGINT       NOT NULL,
                    PRIMARY KEY (project_id, branch, table_name, row_key_hash)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS cell_current (
                    project_id   INT          NOT NULL,
                    branch       VARCHAR(100) NOT NULL,
                    table_name   VARCHAR(128) NOT NULL,
                    row_key_hash BINARY(32)   NOT NULL,
                    field_name   VARCHAR(128) NOT NULL,
                    row_key      TEXT         NOT NULL,
                    value_id     BIGINT       NULL,
                    snapshot_id  BIGINT       NOT NULL,
                    PRIMARY KEY (project_id, branch, table_name, row_key_hash, field_name)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS entity_current (
                    project_id  INT          NOT NULL,
                    branch      VARCHAR(100) NOT NULL,
                    entity_kind VARCHAR(16)  NOT NULL,
                    entity_name VARCHAR(128) NOT NULL,
                    entity_hash CHAR(64)     NOT NULL,
                    snapshot_id BIGINT       NOT NULL,
                    PRIMARY KEY (project_id, branch, entity_kind, entity_name)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS member_current (
                    project_id  INT          NOT NULL,
                    branch      VARCHAR(100) NOT NULL,
                    entity_kind VARCHAR(16)  NOT NULL,
                    entity_name VARCHAR(128) NOT NULL,
                    member_name VARCHAR(128) NOT NULL,
                    member_value TEXT        NOT NULL,
                    snapshot_id BIGINT       NOT NULL,
                    PRIMARY KEY (project_id, branch, entity_kind, entity_name, member_name)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS schema_change (
                    id           BIGINT       NOT NULL AUTO_INCREMENT,
                    snapshot_id  BIGINT       NOT NULL,
                    entity_kind  VARCHAR(16)  NOT NULL,
                    entity_name  VARCHAR(128) NOT NULL,
                    member_name  VARCHAR(128) NULL,
                    change_kind  VARCHAR(16)  NOT NULL,
                    before_value TEXT         NULL,
                    after_value  TEXT         NULL,
                    file         VARCHAR(255) NULL,
                    sheet        VARCHAR(128) NULL,
                    cell         VARCHAR(16)  NULL,
                    url          TEXT         NULL,
                    PRIMARY KEY (id),
                    KEY ix_snapshot (snapshot_id)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS row_change (
                    id           BIGINT       NOT NULL AUTO_INCREMENT,
                    snapshot_id  BIGINT       NOT NULL,
                    table_name   VARCHAR(128) NOT NULL,
                    row_key_hash BINARY(32)   NOT NULL,
                    row_key      TEXT         NOT NULL,
                    change_kind  VARCHAR(16)  NOT NULL,
                    PRIMARY KEY (id),
                    KEY ix_snapshot (snapshot_id, table_name)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS cell_change (
                    id           BIGINT       NOT NULL AUTO_INCREMENT,
                    snapshot_id  BIGINT       NOT NULL,
                    table_name   VARCHAR(128) NOT NULL,
                    row_key_hash BINARY(32)   NOT NULL,
                    row_key      TEXT         NOT NULL,
                    field_name   VARCHAR(128) NOT NULL,
                    change_kind  VARCHAR(16)  NOT NULL,
                    old_value_id BIGINT       NULL,
                    new_value_id BIGINT       NULL,
                    file         VARCHAR(255) NULL,
                    sheet        VARCHAR(128) NULL,
                    cell         VARCHAR(16)  NULL,
                    url          TEXT         NULL,
                    PRIMARY KEY (id),
                    KEY ix_snapshot (snapshot_id, table_name),
                    KEY ix_cell (table_name, row_key_hash, field_name)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            },

            [2] = new[]
            {
                // Whether a snapshot's commit directly follows its parent snapshot's.
                // Recorded when the snapshot is written, because only a conversion has the
                // repository to ask - and false means the changes cover more than one
                // commit's work, which a report has to say rather than let a reader assume.
                //
                // Existing rows default to following: claiming a gap that cannot be checked
                // would put a warning on every snapshot recorded before this column existed.
                @"ALTER TABLE snapshot
                    ADD COLUMN follows_parent TINYINT(1) NOT NULL DEFAULT 1 AFTER parent_id",
            },
        };
    }
}
