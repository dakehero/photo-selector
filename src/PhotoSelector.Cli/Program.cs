using System.Globalization;
using System.Text.Json;
using PhotoSelector.Agent.Workers;
using PhotoSelector.Agent.Workflows;
using PhotoSelector.Ai.Ratings;
using PhotoSelector.Config;
using PhotoSelector.Config.Secrets;
using PhotoSelector.Core.Exporting;
using PhotoSelector.Core.Projects;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        return CliApp.Run(args, Console.Out, Console.Error, Console.In);
    }
}

public static class CliApp
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
            return args[0] switch
            {
                "auth" => RunAuth(args, output, error, input, secretStore),
                "config" => RunConfig(args, output, error),
                "import" => RunImport(args, output, error),
                "scan" => RunScan(args, output, error, secretStore, ratingClient),
                "status" => RunStatus(args, output, error),
                "process" => RunProcess(args, output, error, secretStore, ratingClient),
                "flush" => RunFlush(args, output, error, secretStore, ratingClient),
                "reset" => RunReset(args, output, error),
                "results" => RunResults(args, output, error),
                "export" => RunExport(args, output, error),
                "projects" => RunProjects(args, output, error),
                "open" => RunOpen(args, output, error),
                "photos" => RunPhotos(args, output, error),
                _ => WriteUsage(error),
            };
        }
        catch (Exception ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
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

    private static int RunImport(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 2)
        {
            return WriteUsage(error);
        }

        var result = ImportDirectory(args[1], error);
        if (result is null)
        {
            return 1;
        }

        output.WriteLine(
            $"Imported {result.PhotoCount} photo(s). Project: {result.ProjectId}. Catalog: {result.DatabasePath}; pending: {result.PendingRatingJobs}");
        return 0;
    }

    private static int RunScan(
        string[] args,
        TextWriter output,
        TextWriter error,
        ISecretStore secretStore,
        IPhotoRatingClient? ratingClient)
    {
        if (args.Length != 2)
        {
            return WriteUsage(error);
        }

        var result = ImportDirectory(args[1], error);
        if (result is null)
        {
            return 1;
        }

        output.WriteLine($"Scanned {result.PhotoCount} photo(s). Database: {result.DatabasePath}");

        using var database = OpenCatalogDatabase();
        return ProcessPendingJobs(database, result.ProjectId, force: false, output, error, secretStore, ratingClient);
    }

    private static ImportResult? ImportDirectory(string directory, TextWriter error, bool forceJobs = false)
    {
        var databasePath = ConfigPaths.GetDatabasePath();
        using var database = OpenCatalogDatabase();
        try
        {
            var result = new ImportWorkflow().ImportDirectory(database, directory, forceJobs);
            return new ImportResult(databasePath, result.ProjectId, result.PhotoCount, result.PendingRatingJobs);
        }
        catch (DirectoryNotFoundException ex)
        {
            error.WriteLine(ex.Message);
            return null;
        }
    }

    private static int RunStatus(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length > 2)
        {
            return WriteUsage(error);
        }

        using var database = OpenCatalogDatabase();
        PhotoProject? project = null;
        if (args.Length == 2)
        {
            project = FindProject(database, args[1]);
            if (project is null)
            {
                error.WriteLine($"Project not found: {args[1]}");
                return 1;
            }
        }

        var summary = database.GetRatingJobSummary(project?.Id);
        var rated = project is null
            ? database.ListProjects().SelectMany(item => database.ListPhotos(item.Id)).Sum(photo => database.ListRatings(photo.Id).Count > 0 ? 1 : 0)
            : database.ListPhotos(project.Id).Sum(photo => database.ListRatings(photo.Id).Count > 0 ? 1 : 0);

        output.WriteLine(project is null ? "Project: all" : $"Project: {project.SourceDirectory}");
        output.WriteLine($"jobs: {summary.Total}");
        output.WriteLine($"pending: {summary.Pending}");
        output.WriteLine($"rated: {rated}");
        output.WriteLine($"failed: {summary.Failed}");
        return 0;
    }

    private static int RunProcess(
        string[] args,
        TextWriter output,
        TextWriter error,
        ISecretStore secretStore,
        IPhotoRatingClient? ratingClient)
    {
        if (args.Length > 2)
        {
            return WriteUsage(error);
        }

        using var database = OpenCatalogDatabase();
        PhotoProject? project = null;
        if (args.Length == 2)
        {
            project = FindProject(database, args[1]);
            if (project is null)
            {
                error.WriteLine($"Project not found: {args[1]}");
                return 1;
            }
        }

        return ProcessPendingJobs(database, project?.Id, force: false, output, error, secretStore, ratingClient);
    }

    private static int RunFlush(
        string[] args,
        TextWriter output,
        TextWriter error,
        ISecretStore secretStore,
        IPhotoRatingClient? ratingClient)
    {
        if (args.Length is not (2 or 3))
        {
            return WriteUsage(error);
        }

        var runNow = false;
        if (args.Length == 3)
        {
            if (args[2] != "--now")
            {
                return WriteUsage(error);
            }

            runNow = true;
        }

        var result = ImportDirectory(args[1], error, forceJobs: true);
        if (result is null)
        {
            return 1;
        }

        output.WriteLine(
            $"Flushed {result.PhotoCount} photo(s). Project: {result.ProjectId}. Catalog: {result.DatabasePath}; pending: {result.PendingRatingJobs}");

        if (!runNow)
        {
            return 0;
        }

        using var database = OpenCatalogDatabase();
        return ProcessPendingJobs(database, result.ProjectId, force: true, output, error, secretStore, ratingClient);
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
        if (args.Length > 2)
        {
            return WriteUsage(error);
        }

        using var database = OpenCatalogDatabase();
        PhotoProject? project = null;
        if (args.Length == 2)
        {
            project = FindProject(database, args[1]);
            if (project is null)
            {
                error.WriteLine($"Project not found: {args[1]}");
                return 1;
            }
        }

        var projects = project is null ? database.ListProjects() : [project];
        var results = projects
            .SelectMany(item => database.ListPhotos(item.Id))
            .Select(photo => new PhotoResult(photo, database.ListRatings(photo.Id).FirstOrDefault()))
            .ToArray();
        var rated = results.Where(item => item.Rating is not null).ToArray();
        var keep = CountCategory(rated, "keep");
        var maybe = CountCategory(rated, "maybe");
        var reject = CountCategory(rated, "reject");

        output.WriteLine(project is null ? "Project: all" : $"Project: {project.SourceDirectory}");
        output.WriteLine($"photos: {results.Length}");
        output.WriteLine($"rated: {rated.Length}");
        output.WriteLine($"unrated: {results.Length - rated.Length}");
        output.WriteLine($"keep: {keep}");
        output.WriteLine($"maybe: {maybe}");
        output.WriteLine($"reject: {reject}");
        output.WriteLine("top:");

        foreach (var item in rated
                     .OrderByDescending(item => item.Rating!.Score)
                     .ThenBy(item => item.Photo.BaseName, StringComparer.OrdinalIgnoreCase)
                     .Take(5))
        {
            output.WriteLine(
                $"  {item.Rating!.Score.ToString("0.0", CultureInfo.InvariantCulture)} {item.Rating.Category} {item.Photo.BaseName} - {item.Rating.Reason}");
        }

        return 0;
    }

    private static int CountCategory(IEnumerable<PhotoResult> results, string category)
    {
        return results.Count(item => string.Equals(item.Rating?.Category, category, StringComparison.OrdinalIgnoreCase));
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

    private static int ProcessPendingJobs(
        ProjectDatabase database,
        long? projectId,
        bool force,
        TextWriter output,
        TextWriter error,
        ISecretStore secretStore,
        IPhotoRatingClient? ratingClient)
    {
        var context = CreateRatingContext(null, error, secretStore, ratingClient);
        if (context is null)
        {
            return 1;
        }

        using var ownedClient = context.OwnedClient;
        var jobs = database.ListPendingRatingJobs(projectId);
        var result = new RatingWorker(context.Client).ProcessPending(
            database,
            jobs,
            new RatingWorkerOptions(
                context.BaseUrl,
                context.ApiKey.Secret!,
                context.Profile,
                BuildRatingPrompt(context.Profile),
                force));

        foreach (var message in result.Messages)
        {
            output.WriteLine(message);
        }

        output.WriteLine(
            $"Rated {result.Rated} photo(s), skipped {result.Skipped}, failed {result.Failed}. Provider: {context.Profile.Provider}; model: {context.Profile.Model}; key source: {context.ApiKey.Source}; config: {context.Store.ConfigPath}");
        return result.Failed == 0 ? 0 : 1;
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

        output.WriteLine(JsonSerializer.Serialize(new ProjectsJson(projects), JsonOptions));
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

        output.WriteLine(JsonSerializer.Serialize(new OpenJson(ToProjectJson(project, database)), JsonOptions));
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
        output.WriteLine(JsonSerializer.Serialize(new PhotosJson(project.Id, photos), JsonOptions));
        return 0;
    }

    private static RatingContext? CreateRatingContext(
        string? providerOverride,
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

    private static RatingCriterionJson[] ParseCriteriaJson(string criteriaJson)
    {
        try
        {
            return JsonSerializer.Deserialize<RatingCriterionJson[]>(criteriaJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int WriteUsage(TextWriter error)
    {
        error.WriteLine("Usage:");
        error.WriteLine("  photo-selector auth login --profile default --api-key-stdin");
        error.WriteLine("  photo-selector auth status --profile default");
        error.WriteLine("  photo-selector auth logout --profile default");
        error.WriteLine("  photo-selector config set <provider|base_url|model|api_key_env|prompt|output_language|concurrency> <value>");
        error.WriteLine("  photo-selector config list");
        error.WriteLine("  photo-selector import <directory>");
        error.WriteLine("  photo-selector scan <directory>");
        error.WriteLine("  photo-selector status [directory]");
        error.WriteLine("  photo-selector process [directory]");
        error.WriteLine("  photo-selector flush <directory> [--now]");
        error.WriteLine("  photo-selector reset ratings <directory> [--with-audit]");
        error.WriteLine("  photo-selector results [directory]");
        error.WriteLine("  photo-selector export <keep|maybe|reject> <directory> <target>");
        error.WriteLine("  photo-selector projects list --json");
        error.WriteLine("  photo-selector open <project-id|directory> --json");
        error.WriteLine("  photo-selector photos list --project <project-id> --json");
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

    private sealed record ProjectsJson(ProjectSummaryJson[] Projects);

    private sealed record OpenJson(ProjectJson Project);

    private sealed record PhotosJson(long ProjectId, PhotoJson[] Photos);

    private sealed record ImportResult(string DatabasePath, long ProjectId, int PhotoCount, int PendingRatingJobs);

    private sealed record PhotoResult(PhotoItem Photo, PhotoRating? Rating);

    private sealed record RatingContext(
        ConfigStore Store,
        AiProfile Profile,
        ApiKeyResolution ApiKey,
        Uri BaseUrl,
        IPhotoRatingClient Client,
        IPhotoRatingClient? OwnedClient);

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

    private static string BuildRatingPrompt(AiProfile profile)
    {
        var prompt = string.IsNullOrWhiteSpace(profile.Prompt)
            ? DefaultPhotoRatingPrompt.Text
            : profile.Prompt;

        return $"""
            {prompt}

            Output all human-readable comments and verdicts in {profile.OutputLanguage}.
            Keep JSON property names exactly as specified.
            """;
    }
}
