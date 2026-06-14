using PhotoSelector.Config.Secrets;

namespace PhotoSelector.Tests;

public sealed class SecretStoreFactoryTests
{
    [Theory]
    [InlineData(SecretStorePlatform.Windows, typeof(WindowsCredentialSecretStore), "windows-credential-manager")]
    [InlineData(SecretStorePlatform.MacOS, typeof(MacOsKeychainSecretStore), "macos-keychain")]
    [InlineData(SecretStorePlatform.Linux, typeof(FreedesktopSecretServiceStore), "freedesktop-secret-service")]
    [InlineData(SecretStorePlatform.Unsupported, typeof(UnsupportedSecretStore), "unsupported")]
    public void Create_for_platform_returns_expected_provider(
        SecretStorePlatform platform,
        Type expectedType,
        string providerName)
    {
        var store = SecretStoreFactory.Create(platform);

        Assert.IsType(expectedType, store);
        Assert.Equal(providerName, store.ProviderName);
    }

    [Fact]
    public void Create_memory_returns_in_memory_provider()
    {
        var store = SecretStoreFactory.Create(SecretStoreKind.Memory);

        Assert.IsType<MemorySecretStore>(store);
        Assert.Equal("memory", store.ProviderName);
    }

    [Fact]
    public void Memory_store_reports_available_status()
    {
        var store = new MemorySecretStore();

        var status = store.GetStatus();

        Assert.True(status.IsAvailable);
        Assert.Null(status.Error);
    }

    [Fact]
    public void Unsupported_store_reports_unavailable_status()
    {
        var store = new UnsupportedSecretStore();

        var status = store.GetStatus();

        Assert.False(status.IsAvailable);
        Assert.Equal("System secret storage is not supported on this platform.", status.Error);
    }
}
