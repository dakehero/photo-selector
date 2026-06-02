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
