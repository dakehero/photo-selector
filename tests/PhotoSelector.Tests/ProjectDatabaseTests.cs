using Microsoft.Data.Sqlite;
using PhotoSelector.Core.Scanning;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.Tests;

public sealed class ProjectDatabaseTests
{
    [Fact]
    public void Project_database_saves_projects_and_replaces_photos()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");

        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();
        var projectId = database.CreateProject(sourceDirectory);

        database.ReplacePhotos(
            projectId,
            new[]
            {
                new PhotoPair("IMG_0002", Path.Combine(sourceDirectory, "IMG_0002.JPG"), null),
                new PhotoPair("IMG_0001", Path.Combine(sourceDirectory, "IMG_0001.JPG"), Path.Combine(sourceDirectory, "IMG_0001.CR3")),
            });

        database.ReplacePhotos(
            projectId,
            new[]
            {
                new PhotoPair("IMG_0001", Path.Combine(sourceDirectory, "IMG_0001.JPG"), Path.Combine(sourceDirectory, "IMG_0001.CR3")),
                new PhotoPair("IMG_0003", Path.Combine(sourceDirectory, "IMG_0003.JPG"), Path.Combine(sourceDirectory, "IMG_0003.CR3")),
            });

        var projects = database.ListProjects();
        var photos = database.ListPhotos(projectId);

        Assert.Single(projects);
        Assert.Equal(sourceDirectory, projects[0].SourceDirectory);
        Assert.Equal(2, photos.Count);
        Assert.Equal("IMG_0001", photos[0].BaseName);
        Assert.NotNull(photos[0].JpegPath);
        Assert.NotNull(photos[0].RawPath);
        Assert.DoesNotContain(photos, photo => photo.BaseName == "IMG_0002");
    }

    [Fact]
    public void ReplacePhotos_fails_for_missing_project()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");

        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();

        Assert.Throws<SqliteException>(() =>
            database.ReplacePhotos(
                999,
                new[]
                {
                    new PhotoPair("IMG_0001", "IMG_0001.JPG", "IMG_0001.CR3"),
                }));
    }

    [Fact]
    public void Migrate_keeps_one_schema_version_row()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");

        using (var database = ProjectDatabase.Open(databasePath))
        {
            database.Migrate();
            database.Migrate();
        }

        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MIN(id), MAX(id), MIN(version), MAX(version) FROM schema_version;";

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.Equal(1, reader.GetInt64(1));
        Assert.Equal(1, reader.GetInt64(2));
        Assert.Equal(1, reader.GetInt64(3));
        Assert.Equal(1, reader.GetInt64(4));
    }

    [Fact]
    public void Migrate_normalizes_legacy_schema_version_table()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "project.db");

        using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE schema_version (
                    version INTEGER NOT NULL
                );

                INSERT INTO schema_version (version)
                VALUES (1);
                """;
            command.ExecuteNonQuery();
        }

        using (var database = ProjectDatabase.Open(databasePath))
        {
            database.Migrate();
        }

        using var migratedConnection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        migratedConnection.Open();
        using var migratedCommand = migratedConnection.CreateCommand();
        migratedCommand.CommandText = "SELECT COUNT(*), MIN(id), MAX(id), MIN(version), MAX(version) FROM schema_version;";

        using var reader = migratedCommand.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.Equal(1, reader.GetInt64(1));
        Assert.Equal(1, reader.GetInt64(2));
        Assert.Equal(1, reader.GetInt64(3));
        Assert.Equal(1, reader.GetInt64(4));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
