using System.Text.Json;
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

    public static int Run(string[] args, TextWriter output, TextWriter error, TextReader input, ISecretStore? secretStore = null)
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
                "rate" => RunRate(args, output, error, secretStore),
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

    private static int RunRate(string[] args, TextWriter output, TextWriter error, ISecretStore secretStore)
    {
        if (args.Length is not (2 or 4))
        {
            return WriteUsage(error);
        }

        if (args.Length == 4 &&
            (args[2] != "--provider" ||
             !string.Equals(args[3], "openai-compatible", StringComparison.OrdinalIgnoreCase)))
        {
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
        var apiKey = ApiKeyResolver.Resolve(profile, secretStore);

        output.WriteLine(
            $"AI rating via {profile.Provider} model {profile.Model} is not wired yet; config loaded from {store.ConfigPath}; key source: {apiKey.Source}; key available: {apiKey.IsAvailable}.");
        return 0;
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
        error.WriteLine("  photo-selector auth login --profile default --api-key-stdin");
        error.WriteLine("  photo-selector auth status --profile default");
        error.WriteLine("  photo-selector auth logout --profile default");
        error.WriteLine("  photo-selector config set <provider|base_url|model|api_key_env|concurrency> <value>");
        error.WriteLine("  photo-selector config list");
        error.WriteLine("  photo-selector scan <directory>");
        error.WriteLine("  photo-selector list <project-db> --json");
        error.WriteLine("  photo-selector export <project-db> --category keep --out <directory>");
        error.WriteLine("  photo-selector rate <project-db> [--provider openai-compatible]");
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
