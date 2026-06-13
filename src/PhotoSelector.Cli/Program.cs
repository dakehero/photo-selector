using System.Globalization;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoSelector.Agent.Workers;
using PhotoSelector.Agent.Workflows;
using PhotoSelector.Ai.Ratings;
using PhotoSelector.Config;
using PhotoSelector.Config.Secrets;
using PhotoSelector.Core.Exporting;
using PhotoSelector.Core.Projects;
using PhotoSelector.Core.Storage;
using Spectre.Console;

namespace PhotoSelector.Cli;

public static partial class Program
{
    public static int Main(string[] args)
    {
        return CliApp.Run(args, Console.Out, Console.Error, Console.In);
    }
}

public static partial class CliApp
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        return Run(args, output, error, Console.In, SecretStoreFactory.CreateDefault());
    }

    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        TextReader input,
        ISecretStore? secretStore = null,
        IPhotoRatingClient? ratingClient = null)
    {
        if (args.Length == 0)
        {
            return WriteUsage(error);
        }

        secretStore ??= SecretStoreFactory.CreateDefault();

        try
        {
            if (!IsKnownTopLevelCommand(args[0]))
            {
                return WriteUsage(error);
            }

            var root = BuildCommandLine(args, output, error, input, secretStore, ratingClient);
            var parseResult = root.Parse(args);
            return parseResult.Invoke(new InvocationConfiguration
            {
                Output = output,
                Error = error,
            });
        }
        catch (Exception ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static RootCommand BuildCommandLine(
        string[] args,
        TextWriter output,
        TextWriter error,
        TextReader input,
        ISecretStore secretStore,
        IPhotoRatingClient? ratingClient)
    {
        var root = new RootCommand("Photo Selector");
        root.Action = new RelayAction(_ => WriteUsage(error));
        root.Subcommands.Add(CreateRelayCommand("help", _ => RunHelp(args, output, error), allowUnmatched: true));
        root.Subcommands.Add(CreateRelayCommand("auth", _ => RunAuth(args, output, error, input, secretStore), allowUnmatched: true));
        root.Subcommands.Add(CreateRelayCommand("config", _ => RunConfig(args, output, error), allowUnmatched: true));
        root.Subcommands.Add(CreateRelayCommand("pick", _ => RunPick(args, output, error, secretStore, ratingClient), allowUnmatched: true));
        root.Subcommands.Add(CreateRelayCommand("rate", _ => RunSinglePhotoProductCommand(args, output, error, secretStore, ratingClient, ProductCommandKind.Rate), allowUnmatched: true));
        root.Subcommands.Add(CreateRelayCommand("coach", _ => RunSinglePhotoProductCommand(args, output, error, secretStore, ratingClient, ProductCommandKind.Coach), allowUnmatched: true));
        root.Subcommands.Add(CreateRelayCommand("arena", _ => RunArena(args, output, error, secretStore, ratingClient), allowUnmatched: true));
        root.Subcommands.Add(CreateRelayCommand("scan", _ => RunScan(args, output, error, secretStore, ratingClient), allowUnmatched: true));
        root.Subcommands.Add(CreateRelayCommand("status", _ => RunStatus(args, output, error), allowUnmatched: true));
        root.Subcommands.Add(CreateRelayCommand("reset", _ => RunReset(args, output, error), allowUnmatched: true));
        root.Subcommands.Add(CreateRelayCommand("results", _ => RunResults(args, output, error), allowUnmatched: true));
        root.Subcommands.Add(CreateRelayCommand("export", _ => RunExport(args, output, error), allowUnmatched: true));
        root.Subcommands.Add(CreateRelayCommand("projects", _ => RunProjects(args, output, error), allowUnmatched: true));
        root.Subcommands.Add(CreateRelayCommand("open", _ => RunOpen(args, output, error), allowUnmatched: true));
        root.Subcommands.Add(CreateRelayCommand("photos", _ => RunPhotos(args, output, error), allowUnmatched: true));
        return root;
    }

    private static bool IsKnownTopLevelCommand(string command)
    {
        return command is
            "help" or "--help" or "-h" or
            "auth" or
            "config" or
            "pick" or
            "rate" or
            "coach" or
            "arena" or
            "scan" or
            "status" or
            "reset" or
            "results" or
            "export" or
            "projects" or
            "open" or
            "photos";
    }

    private static Command CreateRelayCommand(
        string name,
        Func<ParseResult, int> action,
        bool allowUnmatched = false)
    {
        var command = new Command(name);
        command.TreatUnmatchedTokensAsErrors = !allowUnmatched;
        command.Action = new RelayAction(action);
        return command;
    }

    private sealed class RelayAction(Func<ParseResult, int> action) : SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult parseResult)
        {
            return action(parseResult);
        }
    }

    private static int RunHelp(string[] args, TextWriter output, TextWriter error)
    {
        var json = false;
        var selectorParts = new List<string>();
        for (var index = 1; index < args.Length; index++)
        {
            if (args[index] == "--json")
            {
                json = true;
                continue;
            }

            selectorParts.Add(args[index]);
        }

        if (selectorParts.Count == 0)
        {
            if (json)
            {
                output.WriteLine(JsonSerializer.Serialize(
                    new HelpCatalogJson("photo-selector", "0.1", HelpCommands),
                    CliJsonContext.Default.HelpCatalogJson));
            }
            else
            {
                WriteHelpOverview(output);
            }

            return 0;
        }

        var selector = string.Join(' ', selectorParts);
        var command = FindHelpCommand(selector);
        if (command is null)
        {
            error.WriteLine($"Unknown command: {selector}");
            return WriteUsage(error);
        }

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(command, CliJsonContext.Default.HelpCommandJson));
        }
        else
        {
            WriteCommandHelp(command, output);
        }

        return 0;
    }

    private static int RunConfig(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length < 2)
        {
            return WriteUsage(error);
        }

        return args[1] switch
        {
            "set" => RunConfigSet(args, output, error),
            "list" => RunConfigList(args, output),
            _ => WriteUsage(error),
        };
    }

    private static int RunConfigSet(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 4)
        {
            return WriteUsage(error);
        }

        var store = new ConfigStore();
        var config = store.Load();
        var profile = config.GetOrCreateProfile(config.ActiveProfile);

        switch (args[2])
        {
            case "provider":
                profile.Provider = args[3];
                break;
            case "base_url":
                profile.BaseUrl = args[3];
                break;
            case "model":
                profile.Model = args[3];
                break;
            case "api_key_env":
                profile.ApiKeyEnv = args[3];
                break;
            case "prompt":
                profile.Prompt = args[3];
                break;
            case "output_language":
                profile.OutputLanguage = args[3];
                break;
            case "concurrency":
                if (!int.TryParse(args[3], out var concurrency) || concurrency < 1)
                {
                    error.WriteLine("concurrency must be a positive integer.");
                    return 1;
                }

                profile.Concurrency = concurrency;
                break;
            default:
                error.WriteLine($"Unknown config key: {args[2]}");
                return 1;
        }

        store.Save(config);
        output.WriteLine($"Updated {args[2]} for profile '{config.ActiveProfile}'. Config: {store.ConfigPath}");
        return 0;
    }

    private static int RunConfigList(string[] args, TextWriter output)
    {
        var store = new ConfigStore();
        var config = store.Load();
        var profile = config.GetOrCreateProfile(config.ActiveProfile);

        output.WriteLine($"config: {store.ConfigPath}");
        output.WriteLine($"active_profile: {config.ActiveProfile}");
        output.WriteLine($"provider: {profile.Provider}");
        output.WriteLine($"base_url: {profile.BaseUrl}");
        output.WriteLine($"model: {profile.Model}");
        output.WriteLine($"api_key_ref: {profile.ApiKeyRef ?? "(not set)"}");
        output.WriteLine($"api_key_env: {profile.ApiKeyEnv ?? "(not set)"}");
        output.WriteLine($"prompt: {(string.IsNullOrWhiteSpace(profile.Prompt) ? "(default)" : profile.Prompt)}");
        output.WriteLine($"output_language: {profile.OutputLanguage}");
        output.WriteLine($"concurrency: {profile.Concurrency}");
        return 0;
    }

    private static int RunPick(
        string[] args,
        TextWriter output,
        TextWriter error,
        ISecretStore secretStore,
        IPhotoRatingClient? ratingClient)
    {
        if (args.Length < 2)
        {
            return WriteUsage(error);
        }

        var options = ParseProductCommandOptions(args, 2, PhotoPreviewOptions.Fast, allowConcurrency: true, error);
        if (options is null)
        {
            return 1;
        }

        var result = ImportDirectory(args[1], error);
        if (result is null)
        {
            return 1;
        }

        if (!options.Json)
        {
            output.WriteLine($"Picked {result.PhotoCount} photo(s).");
        }

        using var database = OpenCatalogDatabase();
        var processing = ProcessPendingJobsCore(
            database,
            result.ProjectId,
            force: false,
            retryFailed: true,
            options.ModelOverride,
            error,
            secretStore,
            ratingClient,
            options.Json ? null : output,
            SelectionPrompt.Text,
            options.Preview,
            options.ConcurrencyOverride);
        if (processing is null)
        {
            return 1;
        }

        var project = database.ListProjects().First(project => project.Id == result.ProjectId);
        var results = BuildResultsJson(database, project);
        if (options.Json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new ProductDirectoryJson(
                    "pick",
                    ToProjectScopeJson(project)!,
                    new ScanSummaryJson(result.PhotoCount, result.PendingRatingJobs),
                    processing,
                    results),
                CliJsonContext.Default.ProductDirectoryJson));
            return processing.Failed == 0 ? 0 : 1;
        }

        WriteProcessingSummary(processing, output);
        output.WriteLine("Results:");
        WriteResultsSummary(results, output);
        return processing.Failed == 0 ? 0 : 1;
    }

    private static int RunSinglePhotoProductCommand(
        string[] args,
        TextWriter output,
        TextWriter error,
        ISecretStore secretStore,
        IPhotoRatingClient? ratingClient,
        ProductCommandKind kind)
    {
        if (args.Length < 2)
        {
            return WriteUsage(error);
        }

        var commandName = kind == ProductCommandKind.Rate ? "rate" : "coach";
        var imagePath = Path.GetFullPath(args[1]);
        if (Directory.Exists(imagePath) || !File.Exists(imagePath))
        {
            error.WriteLine($"{commandName} requires one image file.");
            return 1;
        }

        var options = ParseProductCommandOptions(args, 2, PhotoPreviewOptions.High, allowConcurrency: false, error);
        if (options is null)
        {
            return 1;
        }

        var context = CreateRatingContext(null, options.ModelOverride, error, secretStore, ratingClient);
        if (context is null)
        {
            return 1;
        }

        using var ownedClient = context.OwnedClient;
        var prompt = BuildRatingPrompt(
            context.Profile,
            kind == ProductCommandKind.Rate ? RatingPrompt.Text : CoachingPrompt.Text);
        var result = context.Client
            .RatePhotoAsync(
                new PhotoRatingRequest(
                    context.BaseUrl,
                    context.ApiKey.Secret!,
                    context.Profile.Model,
                    prompt,
                    imagePath,
                    options.Preview),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        if (options.Json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new SinglePhotoProductJson(commandName, imagePath, ToProductRatingJson(result.Rating), result.Audit.Error),
                CliJsonContext.Default.SinglePhotoProductJson));
        }
        else if (result.Rating is null)
        {
            output.WriteLine($"{commandName}: failed {Path.GetFileNameWithoutExtension(imagePath)} - {result.Audit.Error ?? "unknown error"}");
        }
        else
        {
            output.WriteLine(
                $"{commandName}: {result.Rating.Score.ToString("0.0", CultureInfo.InvariantCulture)} {result.Rating.Category} {Path.GetFileNameWithoutExtension(imagePath)} - {result.Rating.Reason}");
        }

        return result.Rating is null ? 1 : 0;
    }

    private static int RunScan(
        string[] args,
        TextWriter output,
        TextWriter error,
        ISecretStore secretStore,
        IPhotoRatingClient? ratingClient)
    {
        if (args.Length < 2)
        {
            return WriteUsage(error);
        }

        var json = false;
        var modelOverride = default(string?);
        for (var index = 2; index < args.Length; index++)
        {
            if (args[index] == "--json")
            {
                json = true;
                continue;
            }

            if (args[index] == "--model" && index + 1 < args.Length && !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                modelOverride = args[index + 1];
                index++;
                continue;
            }

            return WriteUsage(error);
        }

        var result = ImportDirectory(args[1], error);
        if (result is null)
        {
            return 1;
        }

        if (!json)
        {
            output.WriteLine($"Scanned {result.PhotoCount} photo(s). Database: {result.DatabasePath}");
        }

        using var database = OpenCatalogDatabase();
        var processing = ProcessPendingJobsCore(
            database,
            result.ProjectId,
            force: false,
            retryFailed: true,
            modelOverride,
            error,
            secretStore,
            ratingClient,
            json ? null : output);
        if (processing is null)
        {
            return 1;
        }

        var project = database.ListProjects().First(project => project.Id == result.ProjectId);
        var results = BuildResultsJson(database, project);
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new ScanJson(
                    ToProjectScopeJson(project)!,
                    new ScanSummaryJson(result.PhotoCount, result.PendingRatingJobs),
                    processing,
                    results),
                CliJsonContext.Default.ScanJson));
            return processing.Failed == 0 ? 0 : 1;
        }

        WriteProcessingSummary(processing, output);
        output.WriteLine("Results:");
        WriteResultsSummary(results, output);
        return processing.Failed == 0 ? 0 : 1;
    }

    private static ImportResult? ImportDirectory(string directory, TextWriter error)
    {
        var databasePath = ConfigPaths.GetDatabasePath();
        using var database = OpenCatalogDatabase();
        try
        {
            var result = new ImportWorkflow().ImportDirectory(database, directory);
            return new ImportResult(databasePath, result.ProjectId, result.PhotoCount, result.PendingRatingJobs);
        }
        catch (DirectoryNotFoundException ex)
        {
            error.WriteLine(ex.Message);
            return null;
        }
    }

    private static int RunArena(
        string[] args,
        TextWriter output,
        TextWriter error,
        ISecretStore secretStore,
        IPhotoRatingClient? ratingClient)
    {
        if (args.Length >= 2 && args[1] == "list")
        {
            return RunArenaList(args, output, error);
        }

        if (args.Length >= 2 && args[1] == "show")
        {
            return RunArenaShow(args, output, error);
        }

        if (args.Length is not (4 or 6) || args[2] != "--models")
        {
            return WriteUsage(error);
        }

        var models = args[3]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .ToArray();
        if (models.Length == 0)
        {
            error.WriteLine("arena requires at least one model.");
            return 1;
        }

        var limit = 8;
        if (args.Length == 6)
        {
            if (args[4] != "--limit" || !int.TryParse(args[5], out limit) || limit < 1)
            {
                return WriteUsage(error);
            }
        }

        var import = ImportDirectory(args[1], error);
        if (import is null)
        {
            return 1;
        }

        using var database = OpenCatalogDatabase();
        var project = database.ListProjects().First(project => project.Id == import.ProjectId);
        var photos = database
            .ListPhotos(project.Id)
            .Where(photo => !string.IsNullOrWhiteSpace(photo.JpegPath) && File.Exists(photo.JpegPath))
            .Take(limit)
            .ToArray();

        var context = CreateRatingContext(null, null, error, secretStore, ratingClient);
        if (context is null)
        {
            return 1;
        }

        using var ownedClient = context.OwnedClient;
        var prompt = BuildRatingPrompt(context.Profile);
        var arenaRunId = database.CreateArenaRun(
            project.Id,
            context.Profile.Provider,
            string.Join(",", models),
            prompt,
            context.Profile.OutputLanguage,
            limit);

        var messages = new List<string>();
        foreach (var model in models)
        {
            foreach (var photo in photos)
            {
                var result = context.Client
                    .RatePhotoAsync(
                        new PhotoRatingRequest(
                            context.BaseUrl,
                            context.ApiKey.Secret!,
                            model,
                            prompt,
                            photo.JpegPath!),
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                var rating = result.Rating;
                database.SaveArenaRating(
                    arenaRunId,
                    photo.Id,
                    context.Profile.Provider,
                    model,
                    rating?.PhotoType,
                    rating?.Score,
                    rating?.Category,
                    rating is null ? "[]" : JsonSerializer.Serialize(rating.Criteria.ToArray(), RatingJsonContext.Default.AiRatingCriterionArray),
                    rating?.Reason ?? string.Empty,
                    result.Audit.Prompt,
                    result.Audit.RequestJsonRedacted,
                    result.Audit.RawMessageContent,
                    result.Audit.RawResponseJson,
                    result.Audit.HttpStatus,
                    result.Audit.Error);
                messages.Add(rating is null
                    ? $"{model} {photo.BaseName}: failed - {result.Audit.Error ?? "unknown error"}"
                    : $"{model} {photo.BaseName}: {rating.Score.ToString("0.0", CultureInfo.InvariantCulture)} {rating.Category}");
            }
        }

        output.WriteLine($"Arena: {photos.Length} photo(s) x {models.Length} model(s). Run: {arenaRunId}");
        foreach (var message in messages)
        {
            output.WriteLine(message);
        }

        WriteArenaSummary(database, arenaRunId, output);
        return 0;
    }

    private static int RunArenaList(string[] args, TextWriter output, TextWriter error)
    {
        var json = args.Contains("--json", StringComparer.Ordinal);
        var selectors = args.Skip(2).Where(arg => arg != "--json").ToArray();
        if (selectors.Length > 1)
        {
            return WriteUsage(error);
        }

        using var database = OpenCatalogDatabase();
        PhotoProject? project = null;
        if (selectors.Length == 1)
        {
            project = FindProject(database, selectors[0]);
            if (project is null)
            {
                error.WriteLine($"Project not found: {selectors[0]}");
                return 1;
            }
        }

        var projectsById = database.ListProjects().ToDictionary(item => item.Id);
        var runs = database.ListArenaRuns(project?.Id);
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(BuildArenaListJson(database, project, runs), CliJsonContext.Default.ArenaListJson));
            return 0;
        }

        output.WriteLine(project is null ? "Arena runs: all" : $"Arena runs: {project.SourceDirectory}");
        foreach (var run in runs.OrderByDescending(item => item.CreatedAt))
        {
            var source = projectsById.TryGetValue(run.ProjectId, out var runProject)
                ? runProject.SourceDirectory
                : $"project:{run.ProjectId}";
            var ratingCount = database.ListArenaRatings(run.Id).Count;
            output.WriteLine(
                $"Run: {run.Id} project: {source} provider: {run.Provider} models: {run.ModelsCsv} ratings: {ratingCount} limit: {run.Limit} created: {run.CreatedAt:O}");
        }

        return 0;
    }

    private static int RunArenaShow(string[] args, TextWriter output, TextWriter error)
    {
        var json = args.Contains("--json", StringComparer.Ordinal);
        var values = args.Skip(2).Where(arg => arg != "--json").ToArray();
        if (values.Length != 1 || !long.TryParse(values[0], out var arenaRunId) || arenaRunId < 1)
        {
            return WriteUsage(error);
        }

        using var database = OpenCatalogDatabase();
        var run = database.ListArenaRuns().FirstOrDefault(item => item.Id == arenaRunId);
        if (run is null)
        {
            error.WriteLine($"Arena run not found: {arenaRunId}");
            return 1;
        }

        var project = database.ListProjects().FirstOrDefault(item => item.Id == run.ProjectId);
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(BuildArenaShowJson(database, run, project), CliJsonContext.Default.ArenaShowJson));
            return 0;
        }

        output.WriteLine($"Arena: {run.Id}");
        output.WriteLine($"Project: {project?.SourceDirectory ?? $"project:{run.ProjectId}"}");
        output.WriteLine($"provider: {run.Provider}");
        output.WriteLine($"models: {run.ModelsCsv}");
        output.WriteLine($"limit: {run.Limit}");
        output.WriteLine($"created: {run.CreatedAt:O}");
        WriteArenaSummary(database, run.Id, output);
        WriteArenaPhotoDetails(database, run.Id, output);
        return 0;
    }

    private static int RunStatus(string[] args, TextWriter output, TextWriter error)
    {
        var json = args.Contains("--json", StringComparer.Ordinal);
        var selectors = args.Skip(1).Where(arg => arg != "--json").ToArray();
        if (selectors.Length > 1)
        {
            return WriteUsage(error);
        }

        using var database = OpenCatalogDatabase();
        PhotoProject? project = null;
        if (selectors.Length == 1)
        {
            project = FindProject(database, selectors[0]);
            if (project is null)
            {
                error.WriteLine($"Project not found: {selectors[0]}");
                return 1;
            }
        }

        var summary = database.GetRatingJobSummary(project?.Id);
        var rated = project is null
            ? database.ListProjects().SelectMany(item => database.ListPhotos(item.Id)).Sum(photo => database.ListRatings(photo.Id).Count > 0 ? 1 : 0)
            : database.ListPhotos(project.Id).Sum(photo => database.ListRatings(photo.Id).Count > 0 ? 1 : 0);

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new StatusJson(project is null ? "all" : "project", ToProjectScopeJson(project), ToJobSummaryJson(summary), rated),
                CliJsonContext.Default.StatusJson));
            return 0;
        }

        output.WriteLine(project is null ? "Project: all" : $"Project: {project.SourceDirectory}");
        output.WriteLine($"jobs: {summary.Total}");
        output.WriteLine($"pending: {summary.Pending}");
        output.WriteLine($"rated: {rated}");
        output.WriteLine($"failed: {summary.Failed}");
        return 0;
    }

    private static int RunReset(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length is not (3 or 4) || args[1] != "ratings")
        {
            return WriteUsage(error);
        }

        var includeAudit = false;
        if (args.Length == 4)
        {
            if (args[3] != "--with-audit")
            {
                return WriteUsage(error);
            }

            includeAudit = true;
        }

        using var database = OpenCatalogDatabase();
        var project = FindProject(database, args[2]);
        if (project is null)
        {
            error.WriteLine($"Project not found: {args[2]}");
            return 1;
        }

        var deleted = database.ResetRatings(project.Id, includeAudit);
        var pending = database.GetRatingJobSummary(project.Id).Pending;
        output.WriteLine(
            $"Reset {deleted} rating(s). Project: {project.SourceDirectory}; pending: {pending}; audit: {(includeAudit ? "deleted" : "preserved")}");
        return 0;
    }

    private static int RunResults(string[] args, TextWriter output, TextWriter error)
    {
        var json = args.Contains("--json", StringComparer.Ordinal);
        var selectors = args.Skip(1).Where(arg => arg != "--json").ToArray();
        if (selectors.Length > 1)
        {
            return WriteUsage(error);
        }

        using var database = OpenCatalogDatabase();
        PhotoProject? project = null;
        if (selectors.Length == 1)
        {
            project = FindProject(database, selectors[0]);
            if (project is null)
            {
                error.WriteLine($"Project not found: {selectors[0]}");
                return 1;
            }
        }

        var results = BuildResultsJson(database, project);
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(results, CliJsonContext.Default.ResultsJson));
            return 0;
        }

        WriteResultsSummary(results, output);
        return 0;
    }

    private static ResultsJson BuildResultsJson(ProjectDatabase database, PhotoProject? project)
    {
        var projects = project is null ? database.ListProjects() : [project];
        var jobsByPhotoId = projects
            .SelectMany(item => database.ListRatingJobs(item.Id))
            .GroupBy(job => job.PhotoId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(job => job.UpdatedAt).First());
        var results = projects
            .SelectMany(item => database.ListPhotos(item.Id))
            .Select(photo => new PhotoResult(
                photo,
                database.ListRatings(photo.Id).FirstOrDefault(),
                jobsByPhotoId.GetValueOrDefault(photo.Id)))
            .ToArray();
        var rated = results.Where(item => item.Rating is not null).ToArray();
        var failed = results.Count(item => item.Rating is null && item.Job?.Status == "failed");
        var unrated = results.Count(item => item.Rating is null && item.Job?.Status != "failed");
        var keep = CountCategory(rated, "keep");
        var maybe = CountCategory(rated, "maybe");
        var reject = CountCategory(rated, "reject");

        var orderedTop = rated
            .OrderByDescending(item => item.Rating!.Score)
            .ThenBy(item => item.Photo.BaseName, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(ToPhotoResultJson)
            .ToArray();
        var orderedAll = results
            .OrderByDescending(item => item.Rating?.Score ?? 0)
            .ThenBy(item => item.Photo.BaseName, StringComparer.OrdinalIgnoreCase)
            .Select(ToPhotoResultJson)
            .ToArray();

        return new ResultsJson(
            project is null ? "all" : "project",
            ToProjectScopeJson(project),
            new ResultsSummaryJson(results.Length, rated.Length, unrated, failed, keep, maybe, reject),
            orderedTop,
            orderedAll);
    }

    private static void WriteResultsSummary(ResultsJson results, TextWriter output)
    {
        output.WriteLine(results.Project is null ? "Project: all" : $"Project: {results.Project.SourceDirectory}");
        output.WriteLine($"photos: {results.Summary.Photos}");
        output.WriteLine($"rated: {results.Summary.Rated}");
        output.WriteLine($"unrated: {results.Summary.Unrated}");
        output.WriteLine($"failed: {results.Summary.Failed}");
        output.WriteLine($"keep: {results.Summary.Keep}");
        output.WriteLine($"maybe: {results.Summary.Maybe}");
        output.WriteLine($"reject: {results.Summary.Reject}");
        output.WriteLine("top:");

        foreach (var item in results.Top.Where(item => item.Score is not null))
        {
            output.WriteLine(
                $"  {item.Score!.Value.ToString("0.0", CultureInfo.InvariantCulture)} {item.Category} {item.BaseName} - {item.Reason}");
        }

        output.WriteLine("all:");
        foreach (var item in results.All)
        {
            if (item.Score is null)
            {
                if (item.Status == "failed")
                {
                    output.WriteLine($"  failed {item.BaseName} - {item.Error ?? "unknown error"}");
                    continue;
                }

                output.WriteLine($"  unrated {item.BaseName}");
                continue;
            }

            output.WriteLine(
                $"  {item.Score.Value.ToString("0.0", CultureInfo.InvariantCulture)} {item.Category} {item.BaseName} - {item.Reason}");
        }
    }

    private static void WriteArenaSummary(ProjectDatabase database, long arenaRunId, TextWriter output)
    {
        var ratings = database.ListArenaRatings(arenaRunId);
        output.WriteLine("models:");
        foreach (var group in ratings.GroupBy(rating => rating.Model).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var scored = group.Where(rating => rating.Score is not null).ToArray();
            var average = scored.Length == 0 ? 0 : scored.Average(rating => rating.Score!.Value);
            output.WriteLine(
                $"{group.Key} avg: {average.ToString("0.0", CultureInfo.InvariantCulture)} keep: {CountArenaCategory(scored, "keep")} maybe: {CountArenaCategory(scored, "maybe")} reject: {CountArenaCategory(scored, "reject")} fail: {group.Count(rating => rating.Error is not null)}");
        }

        output.WriteLine("largest disagreements:");
        foreach (var disagreement in ratings
                     .Where(rating => rating.Score is not null)
                     .GroupBy(rating => rating.PhotoId)
                     .Select(group => new
                     {
                         Photo = database.GetPhoto(group.Key),
                         Ratings = group.OrderBy(rating => rating.Model, StringComparer.OrdinalIgnoreCase).ToArray(),
                         Spread = group.Max(rating => rating.Score!.Value) - group.Min(rating => rating.Score!.Value),
                     })
                     .Where(item => item.Photo is not null && item.Ratings.Length > 1)
                     .OrderByDescending(item => item.Spread)
                     .ThenBy(item => item.Photo!.BaseName, StringComparer.OrdinalIgnoreCase)
                     .Take(5))
        {
            output.WriteLine(
                $"{disagreement.Photo!.BaseName} {string.Join(" ", disagreement.Ratings.Select(rating => $"{rating.Model}={rating.Score!.Value.ToString("0.0", CultureInfo.InvariantCulture)}"))}");
        }
    }

    private static void WriteArenaPhotoDetails(ProjectDatabase database, long arenaRunId, TextWriter output)
    {
        var ratings = database.ListArenaRatings(arenaRunId);
        output.WriteLine("photos:");
        foreach (var group in ratings.GroupBy(rating => rating.PhotoId))
        {
            var photo = database.GetPhoto(group.Key);
            output.WriteLine(photo?.BaseName ?? $"photo:{group.Key}");
            foreach (var rating in group.OrderBy(rating => rating.Model, StringComparer.OrdinalIgnoreCase))
            {
                if (rating.Score is null)
                {
                    output.WriteLine($"  {rating.Model} failed - {rating.Error ?? "unknown error"}");
                    continue;
                }

                output.WriteLine(
                    $"  {rating.Model} {rating.Score.Value.ToString("0.0", CultureInfo.InvariantCulture)} {rating.Category} - {rating.Reason}");
            }
        }
    }

    private static ArenaListJson BuildArenaListJson(ProjectDatabase database, PhotoProject? project, IReadOnlyList<ArenaRun> runs)
    {
        var projectsById = database.ListProjects().ToDictionary(item => item.Id);
        return new ArenaListJson(
            project is null ? "all" : "project",
            ToProjectScopeJson(project),
            runs
                .OrderByDescending(item => item.CreatedAt)
                .Select(run =>
                {
                    projectsById.TryGetValue(run.ProjectId, out var runProject);
                    return new ArenaRunJson(
                        run.Id,
                        run.ProjectId,
                        runProject?.SourceDirectory,
                        run.Provider,
                        run.ModelsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        run.OutputLanguage,
                        run.Limit,
                        database.ListArenaRatings(run.Id).Count,
                        run.CreatedAt);
                })
                .ToArray());
    }

    private static ArenaShowJson BuildArenaShowJson(ProjectDatabase database, ArenaRun run, PhotoProject? project)
    {
        var ratings = database.ListArenaRatings(run.Id);
        return new ArenaShowJson(
            new ArenaRunJson(
                run.Id,
                run.ProjectId,
                project?.SourceDirectory,
                run.Provider,
                run.ModelsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                run.OutputLanguage,
                run.Limit,
                ratings.Count,
                run.CreatedAt),
            BuildArenaSummaryJson(database, ratings),
            ratings
                .GroupBy(rating => rating.PhotoId)
                .Select(group =>
                {
                    var photo = database.GetPhoto(group.Key);
                    return new ArenaPhotoJson(
                        group.Key,
                        photo?.BaseName ?? $"photo:{group.Key}",
                        group
                            .OrderBy(rating => rating.Model, StringComparer.OrdinalIgnoreCase)
                            .Select(ToArenaRatingJson)
                            .ToArray());
                })
                .OrderBy(item => item.BaseName, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static ArenaSummaryJson BuildArenaSummaryJson(ProjectDatabase database, IReadOnlyList<ArenaRating> ratings)
    {
        return new ArenaSummaryJson(
            ratings
                .GroupBy(rating => rating.Model)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var scored = group.Where(rating => rating.Score is not null).ToArray();
                    var average = scored.Length == 0 ? 0 : scored.Average(rating => rating.Score!.Value);
                    return new ArenaModelSummaryJson(
                        group.Key,
                        average,
                        CountArenaCategory(scored, "keep"),
                        CountArenaCategory(scored, "maybe"),
                        CountArenaCategory(scored, "reject"),
                        group.Count(rating => rating.Error is not null));
                })
                .ToArray(),
            ratings
                .Where(rating => rating.Score is not null)
                .GroupBy(rating => rating.PhotoId)
                .Select(group => new
                {
                    Photo = database.GetPhoto(group.Key),
                    Ratings = group.OrderBy(rating => rating.Model, StringComparer.OrdinalIgnoreCase).ToArray(),
                    Spread = group.Max(rating => rating.Score!.Value) - group.Min(rating => rating.Score!.Value),
                })
                .Where(item => item.Photo is not null && item.Ratings.Length > 1)
                .OrderByDescending(item => item.Spread)
                .ThenBy(item => item.Photo!.BaseName, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .Select(item => new ArenaDisagreementJson(
                    item.Photo!.Id,
                    item.Photo.BaseName,
                    item.Spread,
                    item.Ratings.Select(ToArenaRatingJson).ToArray()))
                .ToArray());
    }

    private static ArenaRatingJson ToArenaRatingJson(ArenaRating rating)
    {
        return new ArenaRatingJson(
            rating.Model,
            rating.PhotoType,
            rating.Score,
            rating.Category,
            rating.Reason,
            rating.Error);
    }

    private static int CountArenaCategory(IEnumerable<ArenaRating> ratings, string category)
    {
        return ratings.Count(rating => string.Equals(rating.Category, category, StringComparison.OrdinalIgnoreCase));
    }

    private static int CountCategory(IEnumerable<PhotoResult> results, string category)
    {
        return results.Count(item => string.Equals(item.Rating?.Category, category, StringComparison.OrdinalIgnoreCase));
    }

    private static PhotoRatingResultJson ToPhotoResultJson(PhotoResult item)
    {
        if (item.Rating is null)
        {
            var status = item.Job?.Status == "failed" ? "failed" : "unrated";
            return new PhotoRatingResultJson(
                item.Photo.Id,
                item.Photo.BaseName,
                null,
                null,
                null,
                null,
                status,
                item.Job?.LastError);
        }

        return new PhotoRatingResultJson(
            item.Photo.Id,
            item.Photo.BaseName,
            item.Rating.PhotoType,
            item.Rating.Score,
            item.Rating.Category,
            item.Rating.Reason,
            "rated",
            null);
    }

    private static int RunExport(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 4)
        {
            return WriteUsage(error);
        }

        var category = args[1];
        if (!IsRatingCategory(category))
        {
            error.WriteLine($"Unknown export filter: {category}");
            return 1;
        }

        using var database = OpenCatalogDatabase();
        var project = FindProject(database, args[2]);
        if (project is null)
        {
            error.WriteLine($"Project not found: {args[2]}");
            return 1;
        }

        var photos = database
            .ListPhotos(project.Id)
            .Where(photo => string.Equals(
                database.ListRatings(photo.Id).FirstOrDefault()?.Category,
                category,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var result = new ExportService().Export(photos, args[3], DateTimeOffset.UtcNow);

        output.WriteLine(
            $"Exported {result.ExportedFiles.Count} file(s) from {photos.Length} photo(s). Directory: {result.ExportDirectory}");
        return 0;
    }

    private static bool IsRatingCategory(string value)
    {
        return string.Equals(value, "keep", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "maybe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "reject", StringComparison.OrdinalIgnoreCase);
    }

    private static ProductCommandOptions? ParseProductCommandOptions(
        string[] args,
        int startIndex,
        PhotoPreviewOptions defaultPreview,
        bool allowConcurrency,
        TextWriter error)
    {
        var json = false;
        string? modelOverride = null;
        int? concurrencyOverride = null;
        var preview = defaultPreview;

        for (var index = startIndex; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--json":
                    json = true;
                    break;
                case "--model":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        return WriteProductOptionError(error, "--model requires a value.");
                    }

                    modelOverride = args[++index];
                    break;
                case "--concurrency":
                    if (!allowConcurrency)
                    {
                        return WriteProductOptionError(error, "--concurrency is only supported by pick.");
                    }

                    if (index + 1 >= args.Length ||
                        !int.TryParse(args[index + 1], out var concurrency) ||
                        concurrency < 1)
                    {
                        return WriteProductOptionError(error, "--concurrency must be a positive integer.");
                    }

                    concurrencyOverride = concurrency;
                    index++;
                    break;
                case "--quality":
                    if (index + 1 >= args.Length ||
                        !TryParsePreviewQuality(args[index + 1], out preview))
                    {
                        return WriteProductOptionError(error, "--quality must be fast, standard, high, or detail.");
                    }

                    index++;
                    break;
                case "--preview-edge":
                    if (index + 1 >= args.Length ||
                        !int.TryParse(args[index + 1], out var edge) ||
                        edge < 256)
                    {
                        return WriteProductOptionError(error, "--preview-edge must be an integer >= 256.");
                    }

                    preview = preview with { MaxEdge = edge };
                    index++;
                    break;
                case "--preview-jpeg-quality":
                    if (index + 1 >= args.Length ||
                        !int.TryParse(args[index + 1], out var quality) ||
                        quality is < 1 or > 100)
                    {
                        return WriteProductOptionError(error, "--preview-jpeg-quality must be between 1 and 100.");
                    }

                    preview = preview with { JpegQuality = quality };
                    index++;
                    break;
                default:
                    return WriteProductOptionError(error, $"Unknown option: {args[index]}");
            }
        }

        return new ProductCommandOptions(json, modelOverride, preview, concurrencyOverride);
    }

    private static ProductCommandOptions? WriteProductOptionError(TextWriter error, string message)
    {
        error.WriteLine(message);
        return null;
    }

    private static bool TryParsePreviewQuality(string value, out PhotoPreviewOptions preview)
    {
        preview = value.ToLowerInvariant() switch
        {
            "fast" => PhotoPreviewOptions.Fast,
            "standard" => PhotoPreviewOptions.Standard,
            "high" => PhotoPreviewOptions.High,
            "detail" => PhotoPreviewOptions.Detail,
            _ => null!,
        };

        return preview is not null;
    }

    private static void WriteProcessingSummary(ProcessingJson result, TextWriter output)
    {
        foreach (var message in result.Messages)
        {
            output.WriteLine(message);
        }

        output.WriteLine(
            $"Rated {result.Rated} photo(s), skipped {result.Skipped}, failed {result.Failed}. Provider: {result.Provider}; model: {result.Model}; key source: {result.KeySource}; config: {result.ConfigPath}");
    }

    private static ProcessingJson? ProcessPendingJobsCore(
        ProjectDatabase database,
        long? projectId,
        bool force,
        bool retryFailed,
        string? modelOverride,
        TextWriter error,
        ISecretStore secretStore,
        IPhotoRatingClient? ratingClient,
        TextWriter? progressOutput,
        string? promptOverride = null,
        PhotoPreviewOptions? preview = null,
        int? concurrencyOverride = null)
    {
        var context = CreateRatingContext(null, modelOverride, error, secretStore, ratingClient);
        if (context is null)
        {
            return null;
        }

        using var ownedClient = context.OwnedClient;
        var jobs = retryFailed
            ? database
                .ListRatingJobs(projectId)
                .Where(job => job.Status is "pending" or "failed")
                .ToArray()
            : database.ListPendingRatingJobs(projectId);
        var worker = new RatingWorker(context.Client);
        var options = new RatingWorkerOptions(
            context.BaseUrl,
            context.ApiKey.Secret!,
            context.Profile,
            BuildRatingPrompt(context.Profile, promptOverride),
            force,
            concurrencyOverride ?? context.Profile.Concurrency,
            preview);
        var result = progressOutput is not null && ShouldUseLiveProgress(progressOutput, jobs.Count)
            ? ProcessPendingJobsWithLiveProgress(worker, database, jobs, options)
            : worker.ProcessPending(
                database,
                jobs,
                options,
                progressOutput is null ? null : progress => progressOutput.WriteLine(FormatProgressEvent(progress)));

        return new ProcessingJson(
            result.Rated,
            result.Skipped,
            result.Failed,
            context.Profile.Provider,
            context.Profile.Model,
            context.ApiKey.Source,
            context.Store.ConfigPath,
            result.Messages.ToArray());
    }

    private static bool ShouldUseLiveProgress(TextWriter output, int jobCount)
    {
        return jobCount > 0 && ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected;
    }

    private static RatingWorkerResult ProcessPendingJobsWithLiveProgress(
        RatingWorker worker,
        ProjectDatabase database,
        IReadOnlyList<RatingJob> jobs,
        RatingWorkerOptions options)
    {
        RatingWorkerResult? result = null;
        AnsiConsole
            .Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new ElapsedTimeColumn(),
                new SpinnerColumn())
            .Start(context =>
            {
                var task = context.AddTask("Rating photos", maxValue: jobs.Count);
                result = worker.ProcessPending(
                    database,
                    jobs,
                    options,
                    progress =>
                    {
                        task.Description = $"Rating {progress.Current}/{progress.Total} {Markup.Escape(progress.Label)}";
                        task.Value = progress.Current;
                    });
                task.Description = "Rating complete";
                task.Value = jobs.Count;
            });

        return result ?? new RatingWorkerResult(0, 0, 0, []);
    }

    private static string FormatProgressEvent(RatingWorkerProgress progress)
    {
        return $"rating {progress.Current}/{progress.Total}: {progress.Label}";
    }

    private static int RunProjects(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 3 || args[1] != "list" || args[2] != "--json")
        {
            return WriteUsage(error);
        }

        using var database = OpenCatalogDatabase();
        var projects = database
            .ListProjects()
            .Select(project => new ProjectSummaryJson(
                project.Id,
                project.SourceDirectory,
                project.CreatedAt,
                project.LastOpenedAt,
                database.ListPhotos(project.Id).Count))
            .ToArray();

        output.WriteLine(JsonSerializer.Serialize(new ProjectsJson(projects), CliJsonContext.Default.ProjectsJson));
        return 0;
    }

    private static int RunOpen(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 3 || args[2] != "--json")
        {
            return WriteUsage(error);
        }

        using var database = OpenCatalogDatabase();
        var project = FindProject(database, args[1]);
        if (project is null)
        {
            error.WriteLine($"Project not found: {args[1]}");
            return 1;
        }

        output.WriteLine(JsonSerializer.Serialize(new OpenJson(ToProjectJson(project, database)), CliJsonContext.Default.OpenJson));
        return 0;
    }

    private static int RunPhotos(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 5 ||
            args[1] != "list" ||
            args[2] != "--project" ||
            args[4] != "--json")
        {
            return WriteUsage(error);
        }

        if (!long.TryParse(args[3], out var projectId) || projectId < 1)
        {
            error.WriteLine("project id must be a positive integer.");
            return 1;
        }

        using var database = OpenCatalogDatabase();
        var project = database.ListProjects().FirstOrDefault(project => project.Id == projectId);
        if (project is null)
        {
            error.WriteLine($"Project not found: {projectId}");
            return 1;
        }

        var photos = database.ListPhotos(project.Id).Select(photo => ToPhotoJson(photo, database)).ToArray();
        output.WriteLine(JsonSerializer.Serialize(new PhotosJson(project.Id, photos), CliJsonContext.Default.PhotosJson));
        return 0;
    }

    private static RatingContext? CreateRatingContext(
        string? providerOverride,
        string? modelOverride,
        TextWriter error,
        ISecretStore secretStore,
        IPhotoRatingClient? ratingClient)
    {
        var store = new ConfigStore();
        var config = store.Load();
        var profile = config.GetOrCreateProfile(config.ActiveProfile);
        if (!string.IsNullOrWhiteSpace(providerOverride))
        {
            profile.Provider = providerOverride;
        }
        if (!string.IsNullOrWhiteSpace(modelOverride))
        {
            profile.Model = modelOverride;
        }

        var apiKey = ApiKeyResolver.Resolve(profile, secretStore);
        if (!apiKey.IsAvailable || string.IsNullOrEmpty(apiKey.Secret))
        {
            error.WriteLine(apiKey.Error ?? "API key is unavailable.");
            return null;
        }

        if (!ProviderRatingClientFactory.IsSupported(profile.Provider))
        {
            error.WriteLine($"Unsupported provider: {profile.Provider}");
            return null;
        }

        if (!Uri.TryCreate(profile.BaseUrl, UriKind.Absolute, out var baseUrl))
        {
            error.WriteLine($"Invalid base_url: {profile.BaseUrl}");
            return null;
        }

        var ownedClient = ratingClient is null ? ProviderRatingClientFactory.Create(profile.Provider) : null;
        return new RatingContext(store, profile, apiKey, baseUrl, ratingClient ?? ownedClient!, ownedClient);
    }

    private static int RunAuth(string[] args, TextWriter output, TextWriter error, TextReader input, ISecretStore secretStore)
    {
        if (args.Length < 2)
        {
            return WriteUsage(error);
        }

        return args[1] switch
        {
            "login" => RunAuthLogin(args, output, error, input, secretStore),
            "status" => RunAuthStatus(args, output, error, secretStore),
            "logout" => RunAuthLogout(args, output, error, secretStore),
            _ => WriteUsage(error),
        };
    }

    private static int RunAuthLogin(string[] args, TextWriter output, TextWriter error, TextReader input, ISecretStore secretStore)
    {
        var profileName = GetOption(args, "--profile") ?? "default";
        var useStdin = args.Contains("--api-key-stdin", StringComparer.Ordinal);
        if (!useStdin)
        {
            error.WriteLine("Use --api-key-stdin to read the API key from standard input.");
            return 1;
        }

        var apiKey = input.ReadLine();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            error.WriteLine("No API key was provided on standard input.");
            return 1;
        }

        var keyRef = $"photo-selector/{profileName}";
        secretStore.Set(keyRef, apiKey.Trim());

        var store = new ConfigStore();
        var config = store.Load();
        config.ActiveProfile = profileName;
        var profile = config.GetOrCreateProfile(profileName);
        profile.ApiKeyRef = keyRef;
        profile.Provider = string.IsNullOrWhiteSpace(profile.Provider) ? "openai-compatible" : profile.Provider;
        store.Save(config);

        output.WriteLine($"Stored API key in system secret store as {keyRef}. Config: {store.ConfigPath}");
        return 0;
    }

    private static int RunAuthStatus(string[] args, TextWriter output, TextWriter error, ISecretStore secretStore)
    {
        var profileName = GetOption(args, "--profile") ?? new ConfigStore().Load().ActiveProfile;
        var store = new ConfigStore();
        var config = store.Load();
        var profile = config.GetOrCreateProfile(profileName);
        var resolution = ApiKeyResolver.Resolve(profile, secretStore);

        output.WriteLine($"profile: {profileName}");
        output.WriteLine($"secret_store: {secretStore.ProviderName}");
        output.WriteLine($"api_key_ref: {profile.ApiKeyRef ?? "(not set)"}");
        output.WriteLine($"api_key_env: {profile.ApiKeyEnv ?? "(not set)"}");
        output.WriteLine(resolution.IsAvailable ? $"key: available via {resolution.Source}" : $"key: unavailable ({resolution.Error})");
        return 0;
    }

    private static int RunAuthLogout(string[] args, TextWriter output, TextWriter error, ISecretStore secretStore)
    {
        var profileName = GetOption(args, "--profile") ?? "default";
        var keyRef = $"photo-selector/{profileName}";
        secretStore.Delete(keyRef);

        var store = new ConfigStore();
        var config = store.Load();
        var profile = config.GetOrCreateProfile(profileName);
        if (string.Equals(profile.ApiKeyRef, keyRef, StringComparison.Ordinal))
        {
            profile.ApiKeyRef = null;
            store.Save(config);
        }

        output.WriteLine($"Removed API key reference {keyRef}.");
        return 0;
    }

    private static ProjectDatabase OpenCatalogDatabase()
    {
        var database = ProjectDatabase.Open(ConfigPaths.GetDatabasePath());
        try
        {
            database.Migrate();
        }
        catch
        {
            database.Dispose();
            throw;
        }

        return database;
    }

    private static PhotoProject? FindProject(ProjectDatabase database, string projectSelector)
    {
        var projects = database.ListProjects();
        if (long.TryParse(projectSelector, out var projectId))
        {
            return projects.FirstOrDefault(project => project.Id == projectId);
        }

        var sourceDirectory = Path.GetFullPath(projectSelector);
        return projects.FirstOrDefault(project =>
            string.Equals(project.SourceDirectory, sourceDirectory, StringComparison.OrdinalIgnoreCase));
    }

    private static ProjectScopeJson? ToProjectScopeJson(PhotoProject? project)
    {
        return project is null ? null : new ProjectScopeJson(project.Id, project.SourceDirectory);
    }

    private static JobSummaryJson ToJobSummaryJson(RatingJobSummary summary)
    {
        return new JobSummaryJson(summary.Total, summary.Pending, summary.Completed, summary.Failed);
    }

    private static ProjectJson ToProjectJson(PhotoProject project, ProjectDatabase database)
    {
        return new ProjectJson(
            project.Id,
            project.SourceDirectory,
            project.CreatedAt,
            project.LastOpenedAt,
            database.ListPhotos(project.Id).Select(photo => ToPhotoJson(photo, database)).ToArray());
    }

    private static PhotoJson ToPhotoJson(PhotoItem photo, ProjectDatabase database)
    {
        var latestRating = database.ListRatings(photo.Id).FirstOrDefault();
        return new PhotoJson(
            photo.Id,
            photo.ProjectId,
            photo.BaseName,
            photo.JpegPath,
            photo.RawPath,
            photo.CaptureTime,
            photo.ImportStatus,
            latestRating is null
                ? null
                : new RatingJson(
                    latestRating.Id,
                    latestRating.Provider,
                    latestRating.Model,
                    latestRating.PhotoType,
                    latestRating.Score,
                    latestRating.Category,
                    ParseCriteriaJson(latestRating.CriteriaJson),
                    latestRating.Reason,
                    latestRating.CreatedAt));
    }

    private static ProductRatingJson? ToProductRatingJson(AiRating? rating)
    {
        return rating is null
            ? null
            : new ProductRatingJson(
                rating.PhotoType,
                rating.Score,
                rating.Category,
                rating.Criteria
                    .Select(criterion => new RatingCriterionJson(criterion.Name, criterion.Score, criterion.Comment))
                    .ToArray(),
                rating.Reason);
    }

    private static RatingCriterionJson[] ParseCriteriaJson(string criteriaJson)
    {
        try
        {
            return JsonSerializer.Deserialize(criteriaJson, CliJsonContext.Default.RatingCriterionJsonArray) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static readonly HelpOptionJson JsonOption = new("--json", "boolean", false, [], "Emit machine-readable JSON.");

    private static readonly HelpOptionJson ModelOption = new("--model", "string", false, [], "Override the configured model for this invocation.");

    private static readonly HelpCommandJson[] HelpCommands =
    [
        new(
            "help",
            "photo-selector help [command] [--json]",
            "Show human or machine-readable command help.",
            [new HelpArgumentJson("command", false, "string", "command", "Command name, such as pick or projects list.")],
            [JsonOption],
            new HelpOutputJson(true, true, "Human overview or command schema."),
            ["photo-selector help --json", "photo-selector help pick --json"]),
        new(
            "pick",
            "photo-selector pick <directory>",
            "Select best photos from a directory using the selection prompt.",
            [new HelpArgumentJson("directory", true, "path", "directory", "Directory containing JPEG and optional RAW pairs.")],
            [
                new HelpOptionJson("--quality", "string", false, ["fast", "standard", "high", "detail"], "Choose preview size and JPEG compression preset."),
                new HelpOptionJson("--preview-edge", "integer", false, [], "Override the longest preview edge in pixels."),
                new HelpOptionJson("--preview-jpeg-quality", "integer", false, [], "Override preview JPEG quality from 1 to 100."),
                new HelpOptionJson("--concurrency", "integer", false, [], "Override concurrent AI requests for this run."),
                ModelOption,
                JsonOption,
            ],
            new HelpOutputJson(true, true, "Directory processing summary and ranked photo results."),
            ["photo-selector pick \"C:\\Photos\\Shoot\" --json", "photo-selector pick \"C:\\Photos\\Shoot\" --concurrency 2"]),
        new(
            "rate",
            "photo-selector rate <image>",
            "Rate one photo with the detailed critique prompt.",
            [new HelpArgumentJson("image", true, "path", "file", "JPEG or readable image file.")],
            [
                new HelpOptionJson("--quality", "string", false, ["fast", "standard", "high", "detail"], "Choose preview size and JPEG compression preset."),
                new HelpOptionJson("--preview-edge", "integer", false, [], "Override the longest preview edge in pixels."),
                new HelpOptionJson("--preview-jpeg-quality", "integer", false, [], "Override preview JPEG quality from 1 to 100."),
                ModelOption,
                JsonOption,
            ],
            new HelpOutputJson(true, true, "Single-photo rating result."),
            ["photo-selector rate \"C:\\Photos\\Shoot\\DSC_0001.JPG\" --json"]),
        new(
            "coach",
            "photo-selector coach <image>",
            "Give practical coaching advice for one photo.",
            [new HelpArgumentJson("image", true, "path", "file", "JPEG or readable image file.")],
            [
                new HelpOptionJson("--quality", "string", false, ["fast", "standard", "high", "detail"], "Choose preview size and JPEG compression preset."),
                new HelpOptionJson("--preview-edge", "integer", false, [], "Override the longest preview edge in pixels."),
                new HelpOptionJson("--preview-jpeg-quality", "integer", false, [], "Override preview JPEG quality from 1 to 100."),
                ModelOption,
                JsonOption,
            ],
            new HelpOutputJson(true, true, "Single-photo coaching result."),
            ["photo-selector coach \"C:\\Photos\\Shoot\\DSC_0001.JPG\""]),
        new(
            "arena",
            "photo-selector arena <directory> --models <model1,model2>",
            "Compare multiple models on photos from one directory.",
            [
                new HelpArgumentJson("directory", true, "path", "directory", "Directory containing photos."),
            ],
            [
                new HelpOptionJson("--models", "string", true, [], "Comma-separated model names."),
                new HelpOptionJson("--limit", "integer", false, [], "Limit the number of photos evaluated."),
            ],
            new HelpOutputJson(true, false, "Arena run summary."),
            ["photo-selector arena \"C:\\Photos\\Shoot\" --models model-a,model-b --limit 5"]),
        new(
            "arena list",
            "photo-selector arena list [directory] [--json]",
            "List saved arena runs.",
            [new HelpArgumentJson("directory", false, "path", "directory", "Optional project directory filter.")],
            [JsonOption],
            new HelpOutputJson(true, true, "Arena run list."),
            ["photo-selector arena list --json"]),
        new(
            "arena show",
            "photo-selector arena show <run-id> [--json]",
            "Show one saved arena run with model comparisons.",
            [new HelpArgumentJson("run-id", true, "integer", "id", "Arena run id.")],
            [JsonOption],
            new HelpOutputJson(true, true, "Arena run detail."),
            ["photo-selector arena show 1 --json"]),
        new(
            "scan",
            "photo-selector scan <directory>",
            "Synchronously index and rate a directory with the default rating prompt.",
            [new HelpArgumentJson("directory", true, "path", "directory", "Directory containing JPEG and optional RAW pairs.")],
            [ModelOption, JsonOption],
            new HelpOutputJson(true, true, "Directory processing summary and rating results."),
            ["photo-selector scan \"C:\\Photos\\Shoot\" --json"]),
        new(
            "status",
            "photo-selector status [directory]",
            "Show catalog job and rating status.",
            [new HelpArgumentJson("directory", false, "path", "directory", "Optional project directory filter.")],
            [JsonOption],
            new HelpOutputJson(true, true, "Job summary and rated count."),
            ["photo-selector status --json"]),
        new(
            "reset ratings",
            "photo-selector reset ratings <directory>",
            "Remove saved ratings for a directory so the next pick or scan can evaluate it again.",
            [new HelpArgumentJson("directory", true, "path", "directory", "Project directory.")],
            [new HelpOptionJson("--with-audit", "boolean", false, [], "Also delete audit logs.")],
            new HelpOutputJson(true, false, "Reset summary."),
            ["photo-selector reset ratings \"C:\\Photos\\Shoot\""]),
        new(
            "results",
            "photo-selector results [directory]",
            "Show saved rating coverage, top candidates, and all ranked results.",
            [new HelpArgumentJson("directory", false, "path", "directory", "Optional project directory filter.")],
            [JsonOption],
            new HelpOutputJson(true, true, "Rating results summary."),
            ["photo-selector results \"C:\\Photos\\Shoot\" --json"]),
        new(
            "export",
            "photo-selector export <keep|maybe|reject> <directory> <target>",
            "Copy selected JPEG and RAW pairs into a timestamped export directory.",
            [
                new HelpArgumentJson("category", true, "string", "enum", "Rating category to export."),
                new HelpArgumentJson("directory", true, "path", "directory", "Project directory."),
                new HelpArgumentJson("target", true, "path", "directory", "Export parent directory."),
            ],
            [],
            new HelpOutputJson(true, false, "Export summary."),
            ["photo-selector export keep \"C:\\Photos\\Shoot\" \"C:\\Photos\\Selected\""]),
        new(
            "projects list",
            "photo-selector projects list --json",
            "List indexed projects for agent/API callers.",
            [],
            [JsonOption],
            new HelpOutputJson(false, true, "Project list."),
            ["photo-selector projects list --json"]),
        new(
            "open",
            "photo-selector open <project-id|directory> --json",
            "Open one project context by id or directory.",
            [new HelpArgumentJson("project-id|directory", true, "string", "selector", "Project id or source directory.")],
            [JsonOption],
            new HelpOutputJson(false, true, "Project detail with photos."),
            ["photo-selector open \"C:\\Photos\\Shoot\" --json"]),
        new(
            "photos list",
            "photo-selector photos list --project <project-id> --json",
            "List photos for one project.",
            [],
            [
                new HelpOptionJson("--project", "integer", true, [], "Project id."),
                JsonOption,
            ],
            new HelpOutputJson(false, true, "Photo list."),
            ["photo-selector photos list --project 1 --json"]),
        new(
            "auth login",
            "photo-selector auth login --profile default --api-key-stdin",
            "Store an API key in the configured secret provider.",
            [],
            [
                new HelpOptionJson("--profile", "string", true, [], "Profile name."),
                new HelpOptionJson("--api-key-stdin", "boolean", true, [], "Read API key from stdin."),
            ],
            new HelpOutputJson(true, false, "Authentication summary."),
            ["Get-Content key.txt | photo-selector auth login --profile default --api-key-stdin"]),
        new(
            "auth status",
            "photo-selector auth status --profile default",
            "Show configured secret reference availability.",
            [],
            [new HelpOptionJson("--profile", "string", true, [], "Profile name.")],
            new HelpOutputJson(true, false, "Auth status."),
            ["photo-selector auth status --profile default"]),
        new(
            "auth logout",
            "photo-selector auth logout --profile default",
            "Remove the stored API key for a profile.",
            [],
            [new HelpOptionJson("--profile", "string", true, [], "Profile name.")],
            new HelpOutputJson(true, false, "Logout summary."),
            ["photo-selector auth logout --profile default"]),
        new(
            "config set",
            "photo-selector config set <key> <value>",
            "Set provider, endpoint, model, API key env, prompt, output language, or concurrency.",
            [
                new HelpArgumentJson("key", true, "string", "enum", "provider, base_url, model, api_key_env, prompt, output_language, or concurrency."),
                new HelpArgumentJson("value", true, "string", "value", "Configuration value."),
            ],
            [],
            new HelpOutputJson(true, false, "Config update summary."),
            ["photo-selector config set model qwen/qwen3-vl-plus"]),
        new(
            "config list",
            "photo-selector config list",
            "Show active provider configuration without revealing API keys.",
            [],
            [],
            new HelpOutputJson(true, false, "Config summary."),
            ["photo-selector config list"]),
    ];

    private static HelpCommandJson? FindHelpCommand(string selector)
    {
        return HelpCommands.FirstOrDefault(command =>
            string.Equals(command.Name, selector, StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteHelpOverview(TextWriter output)
    {
        output.WriteLine("Photo Selector");
        output.WriteLine();
        output.WriteLine("Usage:");
        foreach (var command in HelpCommands)
        {
            output.WriteLine($"  {command.Usage}");
        }

        output.WriteLine();
        output.WriteLine("Agent/API:");
        output.WriteLine("  photo-selector help --json");
        output.WriteLine("  photo-selector help <command> --json");
    }

    private static void WriteCommandHelp(HelpCommandJson command, TextWriter output)
    {
        output.WriteLine(command.Usage);
        output.WriteLine(command.Summary);
        if (command.Arguments.Length > 0)
        {
            output.WriteLine("Arguments:");
            foreach (var argument in command.Arguments)
            {
                output.WriteLine($"  {argument.Name}: {argument.Description}");
            }
        }

        if (command.Options.Length > 0)
        {
            output.WriteLine("Options:");
            foreach (var option in command.Options)
            {
                output.WriteLine($"  {option.Name}: {option.Description}");
            }
        }

        if (command.Examples.Length > 0)
        {
            output.WriteLine("Examples:");
            foreach (var example in command.Examples)
            {
                output.WriteLine($"  {example}");
            }
        }
    }

    private static int WriteUsage(TextWriter error)
    {
        WriteHelpOverview(error);
        return 1;
    }

    private static string? GetOption(string[] args, string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == option)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(ProjectsJson))]
    [JsonSerializable(typeof(OpenJson))]
    [JsonSerializable(typeof(PhotosJson))]
    [JsonSerializable(typeof(ProductDirectoryJson))]
    [JsonSerializable(typeof(SinglePhotoProductJson))]
    [JsonSerializable(typeof(ScanJson))]
    [JsonSerializable(typeof(ResultsJson))]
    [JsonSerializable(typeof(StatusJson))]
    [JsonSerializable(typeof(ArenaListJson))]
    [JsonSerializable(typeof(ArenaShowJson))]
    [JsonSerializable(typeof(HelpCatalogJson))]
    [JsonSerializable(typeof(HelpCommandJson))]
    [JsonSerializable(typeof(RatingCriterionJson[]))]
    private sealed partial class CliJsonContext : JsonSerializerContext;

    private sealed record HelpCatalogJson(
        string Name,
        string Version,
        HelpCommandJson[] Commands);

    private sealed record HelpCommandJson(
        string Name,
        string Usage,
        string Summary,
        HelpArgumentJson[] Arguments,
        HelpOptionJson[] Options,
        HelpOutputJson Output,
        string[] Examples);

    private sealed record HelpArgumentJson(
        string Name,
        bool Required,
        string Type,
        string Kind,
        string Description);

    private sealed record HelpOptionJson(
        string Name,
        string Type,
        bool Required,
        string[] Values,
        string Description);

    private sealed record HelpOutputJson(
        bool Text,
        bool Json,
        string Contract);

    private sealed record ProjectsJson(ProjectSummaryJson[] Projects);

    private sealed record OpenJson(ProjectJson Project);

    private sealed record PhotosJson(long ProjectId, PhotoJson[] Photos);

    private sealed record ProductDirectoryJson(
        string Command,
        ProjectScopeJson Project,
        ScanSummaryJson Scan,
        ProcessingJson Processing,
        ResultsJson Results);

    private sealed record SinglePhotoProductJson(
        string Command,
        string ImagePath,
        ProductRatingJson? Rating,
        string? Error);

    private sealed record ScanJson(
        ProjectScopeJson Project,
        ScanSummaryJson Scan,
        ProcessingJson Processing,
        ResultsJson Results);

    private sealed record ScanSummaryJson(int Photos, int PendingRatingJobs);

    private sealed record ResultsJson(
        string Scope,
        ProjectScopeJson? Project,
        ResultsSummaryJson Summary,
        PhotoRatingResultJson[] Top,
        PhotoRatingResultJson[] All);

    private sealed record StatusJson(
        string Scope,
        ProjectScopeJson? Project,
        JobSummaryJson Jobs,
        int Rated);

    private sealed record ProcessingJson(
        int Rated,
        int Skipped,
        int Failed,
        string Provider,
        string Model,
        string KeySource,
        string ConfigPath,
        string[] Messages);

    private sealed record ArenaListJson(
        string Scope,
        ProjectScopeJson? Project,
        ArenaRunJson[] Runs);

    private sealed record ArenaShowJson(
        ArenaRunJson Run,
        ArenaSummaryJson Summary,
        ArenaPhotoJson[] Photos);

    private sealed record ImportResult(string DatabasePath, long ProjectId, int PhotoCount, int PendingRatingJobs);

    private sealed record PhotoResult(PhotoItem Photo, PhotoRating? Rating, RatingJob? Job);

    private sealed record RatingContext(
        ConfigStore Store,
        AiProfile Profile,
        ApiKeyResolution ApiKey,
        Uri BaseUrl,
        IPhotoRatingClient Client,
        IPhotoRatingClient? OwnedClient);

    private sealed record ProductCommandOptions(
        bool Json,
        string? ModelOverride,
        PhotoPreviewOptions Preview,
        int? ConcurrencyOverride);

    private enum ProductCommandKind
    {
        Rate,
        Coach,
    }

    private sealed record ProjectScopeJson(long Id, string SourceDirectory);

    private sealed record JobSummaryJson(int Total, int Pending, int Completed, int Failed);

    private sealed record ResultsSummaryJson(
        int Photos,
        int Rated,
        int Unrated,
        int Failed,
        int Keep,
        int Maybe,
        int Reject);

    private sealed record PhotoRatingResultJson(
        long PhotoId,
        string BaseName,
        string? PhotoType,
        double? Score,
        string? Category,
        string? Reason,
        string Status,
        string? Error);

    private sealed record ProductRatingJson(
        string PhotoType,
        double Score,
        string Category,
        RatingCriterionJson[] Criteria,
        string Reason);

    private sealed record ArenaRunJson(
        long Id,
        long ProjectId,
        string? SourceDirectory,
        string Provider,
        string[] Models,
        string OutputLanguage,
        int Limit,
        int RatingCount,
        DateTimeOffset CreatedAt);

    private sealed record ArenaSummaryJson(
        ArenaModelSummaryJson[] Models,
        ArenaDisagreementJson[] LargestDisagreements);

    private sealed record ArenaModelSummaryJson(
        string Model,
        double Average,
        int Keep,
        int Maybe,
        int Reject,
        int Fail);

    private sealed record ArenaDisagreementJson(
        long PhotoId,
        string BaseName,
        double Spread,
        ArenaRatingJson[] Ratings);

    private sealed record ArenaPhotoJson(
        long PhotoId,
        string BaseName,
        ArenaRatingJson[] Ratings);

    private sealed record ArenaRatingJson(
        string Model,
        string? PhotoType,
        double? Score,
        string? Category,
        string Reason,
        string? Error);

    private sealed record ProjectSummaryJson(
        long Id,
        string SourceDirectory,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastOpenedAt,
        int PhotoCount);

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
        string ImportStatus,
        RatingJson? LatestRating);

    private sealed record RatingJson(
        long Id,
        string Provider,
        string Model,
        string PhotoType,
        double Score,
        string Category,
        RatingCriterionJson[] Criteria,
        string Reason,
        DateTimeOffset CreatedAt);

    private sealed record RatingCriterionJson(string Name, double Score, string Comment);

    private static string BuildRatingPrompt(AiProfile profile, string? defaultPrompt = null)
    {
        var prompt = string.IsNullOrWhiteSpace(profile.Prompt)
            ? defaultPrompt ?? DefaultPhotoRatingPrompt.Text
            : profile.Prompt;

        return $"""
            {prompt}

            Output all human-readable comments and verdicts in {profile.OutputLanguage}.
            Keep JSON property names exactly as specified.
            """;
    }
}
