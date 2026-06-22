using LinqToDB;
using PhotoSelector.Core.Projects;

namespace PhotoSelector.Core.Storage;

public sealed partial class ProjectDatabase
{
    public long CreateProject(string sourceDirectory)
    {
        var now = FormatTimestamp(DateTimeOffset.UtcNow);
        return database.InsertWithInt64Identity(new ProjectRow
        {
            SourceDirectory = sourceDirectory,
            CreatedAt = now,
            LastOpenedAt = now,
        });
    }

    public IReadOnlyList<PhotoProject> ListProjects()
    {
        return Projects
            .OrderBy(project => project.Id)
            .AsEnumerable()
            .Select(ToProject)
            .ToArray();
    }

    private static PhotoProject ToProject(ProjectRow row)
    {
        return new PhotoProject(row.Id, row.SourceDirectory, ParseTimestamp(row.CreatedAt), ParseTimestamp(row.LastOpenedAt));
    }
}
