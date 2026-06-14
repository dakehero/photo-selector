using System.Text.Json;
using PhotoSelector.Cli;
using PhotoSelector.Config;
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
    public void Auth_login_stores_secret_reference_shared_by_status()
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

        var configOutput = new StringWriter();
        Assert.Equal(0, CliApp.Run(["config", "list"], configOutput, TextWriter.Null, TextReader.Null, secretStore));
        Assert.Contains("openai-compatible", configOutput.ToString());
        Assert.Contains("gpt-4.1-mini", configOutput.ToString());
        Assert.Contains("api_key_ref", configOutput.ToString());
    }

    [Fact]
    public void Auth_status_verbose_reports_secret_store_diagnostics()
    {
        using var tempDirectory = new TempDirectory();
        using var env = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);
        var secretStore = new UnavailableSecretStore();

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(
            ["auth", "status", "--profile", "default", "--verbose"],
            output,
            error,
            TextReader.Null,
            secretStore);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("secret_store: unavailable-test", output.ToString());
        Assert.Contains("secret_store_available: false", output.ToString());
        Assert.Contains("secret_store_error: test store unavailable", output.ToString());
    }

    [Fact]
    public void Auth_status_json_reports_secret_store_and_key_diagnostics_without_secret()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);
        using var apiKeyEnv = new ScopedEnvironment("PHOTO_SELECTOR_TEST_API_KEY", "sk-json-test");
        var secretStore = new MemorySecretStore();

        Assert.Equal(
            0,
            CliApp.Run(
                ["config", "set", "api_key_env", "PHOTO_SELECTOR_TEST_API_KEY"],
                TextWriter.Null,
                TextWriter.Null,
                TextReader.Null,
                secretStore));

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(
            ["auth", "status", "--profile", "default", "--json"],
            output,
            error,
            TextReader.Null,
            secretStore);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.DoesNotContain("sk-json-test", output.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("default", root.GetProperty("profile").GetString());
        Assert.Equal("memory", root.GetProperty("secretStore").GetString());
        Assert.True(root.GetProperty("secretStoreAvailable").GetBoolean());
        Assert.Equal("PHOTO_SELECTOR_TEST_API_KEY", root.GetProperty("apiKeyEnv").GetString());
        Assert.True(root.GetProperty("keyAvailable").GetBoolean());
        Assert.Equal("api_key_env", root.GetProperty("keySource").GetString());
    }

    [Fact]
    public void Auth_status_reports_unavailable_key_when_secret_store_read_fails()
    {
        using var tempDirectory = new TempDirectory();
        using var env = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);
        var secretStore = new UnavailableSecretStore(throwOnGet: true);

        var store = new ConfigStore();
        var config = store.Load();
        config.GetOrCreateProfile("default").ApiKeyRef = "photo-selector/default";
        store.Save(config);

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(
            ["auth", "status", "--profile", "default"],
            output,
            error,
            TextReader.Null,
            secretStore);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("key: unavailable", output.ToString());
        Assert.Contains("test store unavailable", output.ToString());
    }

    [Fact]
    public void Auth_status_uses_env_key_when_secret_store_read_fails()
    {
        using var tempDirectory = new TempDirectory();
        using var configEnv = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);
        using var apiKeyEnv = new ScopedEnvironment("PHOTO_SELECTOR_TEST_API_KEY", "sk-env-test");
        var secretStore = new UnavailableSecretStore(throwOnGet: true);

        var store = new ConfigStore();
        var config = store.Load();
        var profile = config.GetOrCreateProfile("default");
        profile.ApiKeyRef = "photo-selector/default";
        profile.ApiKeyEnv = "PHOTO_SELECTOR_TEST_API_KEY";
        store.Save(config);

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(
            ["auth", "status", "--profile", "default"],
            output,
            error,
            TextReader.Null,
            secretStore);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("key: available via api_key_env", output.ToString());
    }

    [Fact]
    public void Auth_login_fails_before_writing_when_secret_store_is_unavailable()
    {
        using var tempDirectory = new TempDirectory();
        using var env = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);
        var secretStore = new UnavailableSecretStore();

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(
            ["auth", "login", "--profile", "default", "--api-key-stdin"],
            output,
            error,
            new StringReader("sk-test\n"),
            secretStore);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("Secret store unavailable: test store unavailable", error.ToString());
        Assert.False(secretStore.SetCalled);
    }

    [Fact]
    public void Auth_logout_fails_before_deleting_when_secret_store_is_unavailable()
    {
        using var tempDirectory = new TempDirectory();
        using var env = new ScopedEnvironment("PHOTO_SELECTOR_CONFIG_HOME", tempDirectory.Path);
        var secretStore = new UnavailableSecretStore();
        var store = new ConfigStore();
        var config = store.Load();
        config.GetOrCreateProfile("default").ApiKeyRef = "photo-selector/default";
        store.Save(config);

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(
            ["auth", "logout", "--profile", "default"],
            output,
            error,
            TextReader.Null,
            secretStore);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("Secret store unavailable: test store unavailable", error.ToString());
        Assert.False(secretStore.DeleteCalled);
        Assert.Equal("photo-selector/default", store.Load().GetOrCreateProfile("default").ApiKeyRef);
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

    private sealed class UnavailableSecretStore(bool throwOnGet = false) : ISecretStore
    {
        public bool SetCalled { get; private set; }

        public bool DeleteCalled { get; private set; }

        public string ProviderName => "unavailable-test";

        public SecretStoreStatus GetStatus()
        {
            return new SecretStoreStatus(false, "test store unavailable");
        }

        public void Set(string keyRef, string secret)
        {
            SetCalled = true;
            throw new InvalidOperationException("Set should not be called when status is unavailable.");
        }

        public string? Get(string keyRef)
        {
            if (throwOnGet)
            {
                throw new InvalidOperationException("test store unavailable");
            }

            return null;
        }

        public void Delete(string keyRef)
        {
            DeleteCalled = true;
            throw new InvalidOperationException("Delete should not be called when status is unavailable.");
        }
    }
}
