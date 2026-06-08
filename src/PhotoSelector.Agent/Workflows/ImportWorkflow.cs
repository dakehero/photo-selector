using PhotoSelector.Core.Scanning;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.Agent.Workflows;

public sealed class ImportWorkflow
{
    public ImportWorkflowResult ImportDirectory(ProjectDatabase database, string directory, bool forceJobs = false)
    {
        var sourceDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {sourceDirectory}");
        }

        var pairs = PhotoScanner.ScanDirectory(sourceDirectory);
        var project = database
            .ListProjects()
            .FirstOrDefault(project =>
                string.Equals(project.SourceDirectory, sourceDirectory, StringComparison.OrdinalIgnoreCase));
        var projectId = project?.Id ?? database.CreateProject(sourceDirectory);
        database.ReplacePhotos(projectId, pairs);
        database.EnqueueRatingJobs(projectId, forceJobs);
        var summary = database.GetRatingJobSummary(projectId);

        return new ImportWorkflowResult(sourceDirectory, projectId, pairs.Count, summary.Pending);
    }
}

public sealed record ImportWorkflowResult(
    string SourceDirectory,
    long ProjectId,
    int PhotoCount,
    int PendingRatingJobs);

