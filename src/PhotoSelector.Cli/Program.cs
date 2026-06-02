using System.Text.Json;
using PhotoSelector.Core.Exporting;
using PhotoSelector.Core.Projects;
using PhotoSelector.Core.Scanning;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        return CliApp.Run(args, Console.Out, Console.Error);
    }
}

public static class CliApp
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0)
        {
            return WriteUsage(error);
        }

        try
        {
            return args[0] switch
            {
                "scan" => RunScan(args, output, error),
                "list" => RunList(args, output, error),
                "export" => RunExport(args, output, error),
                "rate" => RunRate(args, output, error),
                _ => WriteUsage(error),
            };
        }
        catch (Exception ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunScan(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 2)
        {
            return WriteUsage(error);
        }

        var sourceDirectory = Path.GetFullPath(args[1]);
        if (!Directory.Exists(sourceDirectory))
        {
            error.WriteLine($"Directory not found: {sourceDirectory}");
            return 1;
        }

        var pairs = PhotoScanner.ScanDirectory(sourceDirectory);
        var databasePath = GetProjectDatabasePath(sourceDirectory);
        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();

        var project = database
            .ListProjects()
            .FirstOrDefault(project =>
                string.Equals(project.SourceDirectory, sourceDirectory, StringComparison.OrdinalIgnoreCase));
        var projectId = project?.Id ?? database.CreateProject(sourceDirectory);
        database.ReplacePhotos(projectId, pairs);

        output.WriteLine($"Scanned {pairs.Count} photo(s). Database: {databasePath}");
        return 0;
    }

    private static int RunList(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 3 || args[2] != "--json")
        {
            return WriteUsage(error);
        }

        using var database = OpenExistingDatabase(args[1]);
        var projects = database
            .ListProjects()
            .Select(project => new ProjectJson(
                project.Id,
                project.SourceDirectory,
                project.CreatedAt,
                project.LastOpenedAt,
                database.ListPhotos(project.Id).Select(ToPhotoJson).ToArray()))
            .ToArray();

        output.WriteLine(JsonSerializer.Serialize(new ListJson(projects), JsonOptions));
        return 0;
    }

    private static int RunExport(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 6 ||
            args[2] != "--category" ||
            !string.Equals(args[3], "keep", StringComparison.OrdinalIgnoreCase) ||
            args[4] != "--out")
        {
            return WriteUsage(error);
        }

        using var database = OpenExistingDatabase(args[1]);
        var photos = database
            .ListProjects()
            .SelectMany(project => database.ListPhotos(project.Id))
            .ToArray();

        var result = new ExportService().Export(photos, Path.GetFullPath(args[5]), DateTimeOffset.UtcNow);
        output.WriteLine(
            $"Exported all photos for MVP category '{args[3]}' to {result.ExportDirectory}. Files copied: {result.ExportedFiles.Count}");
        return 0;
    }

    private static int RunRate(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 4 ||
            args[2] != "--provider" ||
            !string.Equals(args[3], "openai-compatible", StringComparison.OrdinalIgnoreCase))
        {
            return WriteUsage(error);
        }

        output.WriteLine("AI rating via openai-compatible provider is not wired yet; this MVP command is a placeholder.");
        return 0;
    }

    private static ProjectDatabase OpenExistingDatabase(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Database not found: {fullPath}", fullPath);
        }

        var database = ProjectDatabase.Open(fullPath);
        database.Migrate();
        return database;
    }

    private static string GetProjectDatabasePath(string sourceDirectory)
    {
        return Path.Combine(sourceDirectory, ".photo-selector", "photo-selector.db");
    }

    private static PhotoJson ToPhotoJson(PhotoItem photo)
    {
        return new PhotoJson(
            photo.Id,
            photo.ProjectId,
            photo.BaseName,
            photo.JpegPath,
            photo.RawPath,
            photo.CaptureTime,
            photo.ImportStatus);
    }

    private static int WriteUsage(TextWriter error)
    {
        error.WriteLine("Usage:");
        error.WriteLine("  photo-selector scan <directory>");
        error.WriteLine("  photo-selector list <project-db> --json");
        error.WriteLine("  photo-selector export <project-db> --category keep --out <directory>");
        error.WriteLine("  photo-selector rate <project-db> --provider openai-compatible");
        return 1;
    }

    private sealed record ListJson(ProjectJson[] Projects);

    private sealed record ProjectJson(
        long Id,
        string SourceDirectory,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastOpenedAt,
        PhotoJson[] Photos);

    private sealed record PhotoJson(
        long Id,
        long ProjectId,
        string BaseName,
        string? JpegPath,
        string? RawPath,
        DateTimeOffset? CaptureTime,
        string ImportStatus);
}
