using PhotoSelector.Cli;
using PhotoSelector.Config.Secrets;

namespace PhotoSelector.Tests;

public sealed class CliConfigTests
{
    [Fact]
    public void Config_commands_write_shared_toml_config()
    {
        using var tempDirectory = new TempDirectory();
        var secretStore = new MemorySecretStore();
        var output = new StringWriter();
        var error = new StringWriter();

        using var env = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);

        Assert.Equal(0, CliApp.Run(["config", "set", "provider", "openai-compatible"], output, error, TextReader.Null, secretStore));
        Assert.Equal(0, CliApp.Run(["config", "set", "base_url", "https://api.openai.com/v1"], output, error, TextReader.Null, secretStore));
        Assert.Equal(0, CliApp.Run(["config", "set", "model", "gpt-4.1-mini"], output, error, TextReader.Null, secretStore));
        Assert.Equal(0, CliApp.Run(["config", "set", "api_key_env", "OPENAI_API_KEY"], output, error, TextReader.Null, secretStore));
        Assert.Equal(0, CliApp.Run(["config", "set", "output_language", "zh-Hans"], output, error, TextReader.Null, secretStore));

        var configPath = Path.Combine(tempDirectory.Path, "config.toml");
        Assert.True(File.Exists(configPath));
        var toml = File.ReadAllText(configPath);
        Assert.Contains("active_profile = \"default\"", toml);
        Assert.Contains("provider = \"openai-compatible\"", toml);
        Assert.Contains("base_url = \"https://api.openai.com/v1\"", toml);
        Assert.Contains("model = \"gpt-4.1-mini\"", toml);
        Assert.Contains("api_key_env = \"OPENAI_API_KEY\"", toml);
        Assert.Contains("output_language = \"zh-Hans\"", toml);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Auth_login_stores_secret_reference_shared_by_status_and_process()
    {
        using var tempDirectory = new TempDirectory();
        var secretStore = new MemorySecretStore();

        using var env = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);

        var loginOutput = new StringWriter();
        var loginError = new StringWriter();
        var loginExitCode = CliApp.Run(
            ["auth", "login", "--profile", "default", "--api-key-stdin"],
            loginOutput,
            loginError,
            new StringReader("sk-test\n"),
            secretStore);

        Assert.Equal(0, loginExitCode);
        Assert.Equal(string.Empty, loginError.ToString());
        Assert.Equal("sk-test", secretStore.Get("photo-selector/default"));

        var statusOutput = new StringWriter();
        var statusError = new StringWriter();
        var statusExitCode = CliApp.Run(
            ["auth", "status", "--profile", "default"],
            statusOutput,
            statusError,
            TextReader.Null,
            secretStore);

        Assert.Equal(0, statusExitCode);
        Assert.Contains("photo-selector/default", statusOutput.ToString());
        Assert.Contains("available", statusOutput.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secret_store: memory", statusOutput.ToString());
        Assert.Equal(string.Empty, statusError.ToString());

        var processOutput = new StringWriter();
        var processError = new StringWriter();
        var processExitCode = CliApp.Run(
            ["process"],
            processOutput,
            processError,
            TextReader.Null,
            secretStore);

        Assert.Equal(0, processExitCode);
        Assert.Contains("Rated 0 photo(s)", processOutput.ToString());
        Assert.Contains("openai-compatible", processOutput.ToString());
        Assert.Contains("gpt-4.1-mini", processOutput.ToString());
        Assert.Contains("api_key_ref", processOutput.ToString());
        Assert.Equal(string.Empty, processError.ToString());
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
