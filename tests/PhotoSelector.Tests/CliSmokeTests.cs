using System.Text.Json;
using PhotoSelector.Cli;
using PhotoSelector.Config;
using PhotoSelector.Config.Secrets;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.Tests;

public sealed class CliSmokeTests
{
    [Fact]
    public void Import_indexes_directory_then_catalog_commands_open_project_and_photos()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = CreateSourceDirectory(tempDirectory.Path);
        var databasePath = Path.Combine(tempDirectory.Path, "photo-selector.db");

        var importOutput = new StringWriter();
        var importError = new StringWriter();
        var importExitCode = CliApp.Run(["import", sourceDirectory], importOutput, importError);

        Assert.Equal(0, importExitCode);
        Assert.Equal(string.Empty, importError.ToString());
        Assert.True(File.Exists(databasePath));
        Assert.Contains("Imported 2 photo(s)", importOutput.ToString());
        Assert.Contains("Project: 1", importOutput.ToString());
        Assert.Contains(databasePath, importOutput.ToString());

        var projectsOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["projects", "list", "--json"], projectsOutput, TextWriter.Null));
        using var projectsDocument = JsonDocument.Parse(projectsOutput.ToString());
        var project = Assert.Single(projectsDocument.RootElement.GetProperty("projects").EnumerateArray());
        var projectId = project.GetProperty("id").GetInt64();
        Assert.Equal(sourceDirectory, project.GetProperty("sourceDirectory").GetString());
        Assert.Equal(2, project.GetProperty("photoCount").GetInt32());

        var openOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["open", projectId.ToString(), "--json"], openOutput, TextWriter.Null));
        using var openDocument = JsonDocument.Parse(openOutput.ToString());
        var openedProject = openDocument.RootElement.GetProperty("project");
        Assert.Equal(projectId, openedProject.GetProperty("id").GetInt64());
        Assert.Equal(sourceDirectory, openedProject.GetProperty("sourceDirectory").GetString());
        Assert.Equal(2, openedProject.GetProperty("photos").GetArrayLength());

        var photosOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["photos", "list", "--project", projectId.ToString(), "--json"], photosOutput, TextWriter.Null));
        using var photosDocument = JsonDocument.Parse(photosOutput.ToString());
        var photos = photosDocument.RootElement.GetProperty("photos").EnumerateArray().ToList();
        Assert.Equal(2, photos.Count);
        Assert.Equal("IMG_0001", photos[0].GetProperty("baseName").GetString());
        Assert.Equal("IMG_0002", photos[1].GetProperty("baseName").GetString());
    }

    [Fact]
    public void Import_creates_shared_catalog_database_with_jpg_and_raw_files()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment(ConfigPaths.ConfigHomeEnvironmentVariable, tempDirectory.Path);
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        var rawPath = Path.Combine(sourceDirectory, "IMG_0001.CR3");
        File.WriteAllText(jpegPath, "jpeg");
        File.WriteAllText(rawPath, "raw");

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(new[] { "import", sourceDirectory }, output, error);

        var databasePath = Path.Combine(tempDirectory.Path, "photo-selector.db");
        var sidecarDatabasePath = Path.Combine(sourceDirectory, ".photo-selector", "photo-selector.db");
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(databasePath));
        Assert.False(File.Exists(sidecarDatabasePath));
        Assert.Contains(databasePath, output.ToString());
        Assert.Contains("Imported 1 photo(s)", output.ToString());
        Assert.Equal(string.Empty, error.ToString());

        using var database = ProjectDatabase.Open(databasePath);
        var project = Assert.Single(database.ListProjects());
        Assert.Equal(sourceDirectory, project.SourceDirectory);
        var photo = Assert.Single(database.ListPhotos(project.Id));
        Assert.Equal("IMG_0001", photo.BaseName);
        Assert.Equal(jpegPath, photo.JpegPath);
        Assert.Equal(rawPath, photo.RawPath);
    }

    [Fact]
    public void Process_openai_compatible_provider_succeeds_when_catalog_has_no_pending_jobs()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);
        var databasePath = Path.Combine(tempDirectory.Path, "photo-selector.db");
        using (var database = ProjectDatabase.Open(databasePath))
        {
            database.Migrate();
        }

        var secretStore = new MemorySecretStore();
        Assert.Equal(
            0,
            CliApp.Run(
                ["auth", "login", "--profile", "default", "--api-key-stdin"],
                TextWriter.Null,
                TextWriter.Null,
                new StringReader("sk-test\n"),
                secretStore));

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(
            new[] { "process" },
            output,
            error,
            TextReader.Null,
            secretStore);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("Rated 0 photo(s)", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateSourceDirectory(string rootDirectory)
    {
        var sourceDirectory = Path.Combine(rootDirectory, Path.GetRandomFileName());
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0001.JPG"), "jpeg");
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0001.CR3"), "raw");
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0002.JPG"), "jpeg");
        return sourceDirectory;
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

    private sealed class ScopedEnvironment : IDisposable
    {
        private readonly string name;
        private readonly string? previousValue;

        public ScopedEnvironment(string name, string value)
        {
            this.name = name;
            previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(name, previousValue);
        }
    }
}
