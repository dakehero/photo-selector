using System.Text.Json;
using PhotoSelector.Ai.Ratings;
using PhotoSelector.Config;
using PhotoSelector.Config.Secrets;
using PhotoSelector.Core.Exporting;
using PhotoSelector.Core.Projects;
using PhotoSelector.Core.Scanning;
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
                "scan" => RunScan(args, output, error),
                "list" => RunList(args, output, error),
                "export" => RunExport(args, output, error),
                "rate" => RunRate(args, output, error, secretStore, ratingClient),
                "audit" => RunAudit(args, output, error),
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
                database.ListPhotos(project.Id).Select(photo => ToPhotoJson(photo, database)).ToArray()))
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

    private static int RunRate(
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

        var force = false;
        string? providerOverride = null;
        for (var index = 2; index < args.Length; index++)
        {
            if (args[index] == "--force")
            {
                force = true;
                continue;
            }

            if (args[index] == "--provider" &&
                index + 1 < args.Length)
            {
                providerOverride = args[index + 1];
                index++;
                continue;
            }

            return WriteUsage(error);
        }

        var databasePath = Path.GetFullPath(args[1]);
        if (!File.Exists(databasePath))
        {
            error.WriteLine($"Database not found: {databasePath}");
            return 1;
        }

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
            return 1;
        }

        if (!ProviderRatingClientFactory.IsSupported(profile.Provider))
        {
            error.WriteLine($"Unsupported provider: {profile.Provider}");
            return 1;
        }

        if (!Uri.TryCreate(profile.BaseUrl, UriKind.Absolute, out var baseUrl))
        {
            error.WriteLine($"Invalid base_url: {profile.BaseUrl}");
            return 1;
        }

        using var database = OpenExistingDatabase(databasePath);
        var photos = database
            .ListProjects()
            .SelectMany(project => database.ListPhotos(project.Id))
            .ToArray();

        using var ownedClient = ratingClient is null ? ProviderRatingClientFactory.Create(profile.Provider) : null;
        var client = ratingClient ?? ownedClient!;
        var prompt = BuildRatingPrompt(profile);
        var rated = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var photo in photos)
        {
            if (!force && database.ListRatings(photo.Id).Count > 0)
            {
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(photo.JpegPath) || !File.Exists(photo.JpegPath))
            {
                skipped++;
                continue;
            }

            try
            {
                var result = client
                    .RatePhotoAsync(
                        new PhotoRatingRequest(baseUrl, apiKey.Secret, profile.Model, prompt, photo.JpegPath),
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                var rating = result.Rating;
                if (rating is null)
                {
                    database.SaveRatingAuditLog(
                        photo.Id,
                        null,
                        profile.Provider,
                        profile.Model,
                        result.Audit.Prompt,
                        result.Audit.RequestJsonRedacted,
                        result.Audit.RawMessageContent,
                        result.Audit.RawResponseJson,
                        result.Audit.HttpStatus,
                        result.Audit.Error);
                    failed++;
                    error.WriteLine($"{photo.BaseName}: {result.Audit.Error ?? "AI rating response could not be parsed."}");
                    continue;
                }

                var criteriaJson = JsonSerializer.Serialize(rating.Criteria, JsonOptions);
                var ratingId = database.SaveRating(
                    photo.Id,
                    profile.Provider,
                    profile.Model,
                    rating.PhotoType,
                    rating.Score,
                    rating.Category,
                    criteriaJson,
                    rating.Reason);
                database.SaveRatingAuditLog(
                    photo.Id,
                    ratingId,
                    profile.Provider,
                    profile.Model,
                    result.Audit.Prompt,
                    result.Audit.RequestJsonRedacted,
                    result.Audit.RawMessageContent,
                    result.Audit.RawResponseJson,
                    result.Audit.HttpStatus,
                    result.Audit.Error);
                rated++;
                output.WriteLine($"{photo.BaseName}: {rating.Score} {rating.Category} - {rating.Reason}");
            }
            catch (Exception ex)
            {
                failed++;
                error.WriteLine($"{photo.BaseName}: {ex.Message}");
            }
        }

        output.WriteLine(
            $"Rated {rated} photo(s), skipped {skipped}, failed {failed}. Provider: {profile.Provider}; model: {profile.Model}; key source: {apiKey.Source}; config: {store.ConfigPath}");
        return failed == 0 ? 0 : 1;
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

    private static int RunAudit(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 5 || args[2] != "--photo-id" || args[4] != "--json")
        {
            return WriteUsage(error);
        }

        if (!long.TryParse(args[3], out var photoId) || photoId < 1)
        {
            error.WriteLine("photo-id must be a positive integer.");
            return 1;
        }

        using var database = OpenExistingDatabase(args[1]);
        var logs = database
            .ListRatingAuditLogs(photoId)
            .Select(log => new AuditLogJson(
                log.Id,
                log.PhotoId,
                log.RatingId,
                log.Provider,
                log.Model,
                log.Prompt,
                log.RequestJsonRedacted,
                log.RawMessageContent,
                log.RawResponseJson,
                log.HttpStatus,
                log.Error,
                log.CreatedAt))
            .ToArray();

        output.WriteLine(JsonSerializer.Serialize(new AuditJson(logs), JsonOptions));
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

    private static ProjectDatabase OpenExistingDatabase(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Database not found: {fullPath}", fullPath);
        }

        var database = ProjectDatabase.Open(fullPath);
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

    private static string GetProjectDatabasePath(string sourceDirectory)
    {
        return Path.Combine(sourceDirectory, ".photo-selector", "photo-selector.db");
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
        error.WriteLine("  photo-selector scan <directory>");
        error.WriteLine("  photo-selector list <project-db> --json");
        error.WriteLine("  photo-selector export <project-db> --category keep --out <directory>");
        error.WriteLine("  photo-selector rate <project-db> [--provider openai-compatible] [--force]");
        error.WriteLine("  photo-selector audit <project-db> --photo-id <id> --json");
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

    private sealed record ListJson(ProjectJson[] Projects);

    private sealed record AuditJson(AuditLogJson[] Logs);

    private sealed record AuditLogJson(
        long Id,
        long PhotoId,
        long? RatingId,
        string Provider,
        string Model,
        string Prompt,
        string RequestJsonRedacted,
        string RawMessageContent,
        string RawResponseJson,
        int? HttpStatus,
        string? Error,
        DateTimeOffset CreatedAt);

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
