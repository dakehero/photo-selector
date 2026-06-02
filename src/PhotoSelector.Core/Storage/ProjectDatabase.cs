using Microsoft.Data.Sqlite;
using PhotoSelector.Core.Projects;
using PhotoSelector.Core.Scanning;

namespace PhotoSelector.Core.Storage;

public sealed class ProjectDatabase : IDisposable
{
    private readonly SqliteConnection connection;

    private ProjectDatabase(SqliteConnection connection)
    {
        this.connection = connection;
    }

    public static ProjectDatabase Open(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false,
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return new ProjectDatabase(connection);
    }

    public void Migrate()
    {
        NormalizeSchemaVersionTable();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_version (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                version INTEGER NOT NULL
            );

            INSERT INTO schema_version (id, version)
            VALUES (1, 1)
            ON CONFLICT(id) DO NOTHING;

            CREATE TABLE IF NOT EXISTS projects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_directory TEXT NOT NULL,
                created_at TEXT NOT NULL,
                last_opened_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS photos (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id INTEGER NOT NULL,
                base_name TEXT NOT NULL,
                jpeg_path TEXT NULL,
                raw_path TEXT NULL,
                capture_time TEXT NULL,
                import_status TEXT NOT NULL,
                FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE CASCADE
            );
            """;
        command.ExecuteNonQuery();
    }

    private void NormalizeSchemaVersionTable()
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM pragma_table_info('schema_version')
            WHERE name = 'id';
            """;

        if ((long)command.ExecuteScalar()! > 0)
        {
            return;
        }

        using var rebuildCommand = connection.CreateCommand();
        rebuildCommand.CommandText = """
            DROP TABLE IF EXISTS schema_version;

            CREATE TABLE schema_version (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                version INTEGER NOT NULL
            );
            """;
        rebuildCommand.ExecuteNonQuery();
    }

    public long CreateProject(string sourceDirectory)
    {
        var now = DateTimeOffset.UtcNow;

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO projects (source_directory, created_at, last_opened_at)
            VALUES ($source_directory, $created_at, $last_opened_at);

            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$source_directory", sourceDirectory);
        command.Parameters.AddWithValue("$created_at", FormatTimestamp(now));
        command.Parameters.AddWithValue("$last_opened_at", FormatTimestamp(now));

        return (long)command.ExecuteScalar()!;
    }

    public void ReplacePhotos(long projectId, IEnumerable<PhotoPair> pairs)
    {
        using var transaction = connection.BeginTransaction();

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM photos WHERE project_id = $project_id;";
            deleteCommand.Parameters.AddWithValue("$project_id", projectId);
            deleteCommand.ExecuteNonQuery();
        }

        foreach (var pair in pairs)
        {
            using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO photos (
                    project_id,
                    base_name,
                    jpeg_path,
                    raw_path,
                    capture_time,
                    import_status
                )
                VALUES (
                    $project_id,
                    $base_name,
                    $jpeg_path,
                    $raw_path,
                    $capture_time,
                    $import_status
                );
                """;
            insertCommand.Parameters.AddWithValue("$project_id", projectId);
            insertCommand.Parameters.AddWithValue("$base_name", pair.BaseName);
            insertCommand.Parameters.AddWithValue("$jpeg_path", (object?)pair.JpegPath ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("$raw_path", (object?)pair.RawPath ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("$capture_time", DBNull.Value);
            insertCommand.Parameters.AddWithValue("$import_status", "imported");
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<PhotoProject> ListProjects()
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, source_directory, created_at, last_opened_at
            FROM projects
            ORDER BY id;
            """;

        var projects = new List<PhotoProject>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            projects.Add(new PhotoProject(
                reader.GetInt64(0),
                reader.GetString(1),
                ParseTimestamp(reader.GetString(2)),
                ParseTimestamp(reader.GetString(3))));
        }

        return projects;
    }

    public IReadOnlyList<PhotoItem> ListPhotos(long projectId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, project_id, base_name, jpeg_path, raw_path, capture_time, import_status
            FROM photos
            WHERE project_id = $project_id
            ORDER BY base_name COLLATE NOCASE, base_name, id;
            """;
        command.Parameters.AddWithValue("$project_id", projectId);

        var photos = new List<PhotoItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            photos.Add(new PhotoItem(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : ParseTimestamp(reader.GetString(5)),
                reader.GetString(6)));
        }

        return photos;
    }

    public void Dispose()
    {
        connection.Dispose();
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O");
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.Parse(value);
    }
}
