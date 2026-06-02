using PhotoSelector.App.ViewModels;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void LoadDirectory_scans_photos_updates_summary_and_persists_project_database()
    {
        using var tempDirectory = new TempDirectory();
        File.WriteAllText(Path.Combine(tempDirectory.Path, "IMG_0001.JPG"), "");
        File.WriteAllText(Path.Combine(tempDirectory.Path, "IMG_0001.CR3"), "");
        File.WriteAllText(Path.Combine(tempDirectory.Path, "IMG_0002.JPG"), "");
        File.WriteAllText(Path.Combine(tempDirectory.Path, "IMG_0003.NEF"), "");
        File.WriteAllText(Path.Combine(tempDirectory.Path, "notes.txt"), "");

        var viewModel = new MainWindowViewModel();

        viewModel.LoadDirectory(tempDirectory.Path);

        Assert.Equal(tempDirectory.Path, viewModel.ProjectDirectory);
        Assert.Equal(3, viewModel.TotalPhotos);
        Assert.Equal(1, viewModel.PairedPhotos);
        Assert.Equal(1, viewModel.JpgOnlyPhotos);
        Assert.Equal(1, viewModel.RawOnlyPhotos);
        Assert.Equal(3, viewModel.Photos.Count);
        Assert.Equal(viewModel.Photos[0], viewModel.SelectedPhoto);

        Assert.Collection(
            viewModel.Photos,
            photo =>
            {
                Assert.Equal("IMG_0001", photo.Name);
                Assert.Equal("IMG_0001.JPG", photo.JpgFileName);
                Assert.Equal("IMG_0001.CR3", photo.RawFileName);
                Assert.Equal("JPG+RAW", photo.PairStatus);
            },
            photo =>
            {
                Assert.Equal("IMG_0002", photo.Name);
                Assert.Equal("IMG_0002.JPG", photo.JpgFileName);
                Assert.Null(photo.RawFileName);
                Assert.Equal("JPG only", photo.PairStatus);
            },
            photo =>
            {
                Assert.Equal("IMG_0003", photo.Name);
                Assert.Null(photo.JpgFileName);
                Assert.Equal("IMG_0003.NEF", photo.RawFileName);
                Assert.Equal("RAW only", photo.PairStatus);
            });

        var databasePath = Path.Combine(tempDirectory.Path, ".photo-selector", "photo-selector.db");
        Assert.True(File.Exists(databasePath));

        using var database = ProjectDatabase.Open(databasePath);
        var project = Assert.Single(database.ListProjects());
        Assert.Equal(tempDirectory.Path, project.SourceDirectory);
        Assert.Equal(3, database.ListPhotos(project.Id).Count);
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
