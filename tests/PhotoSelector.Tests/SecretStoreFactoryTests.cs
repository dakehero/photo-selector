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
}
