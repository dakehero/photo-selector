namespace PhotoSelector.Config.Secrets;

public static class SecretStoreFactory
{
    public static ISecretStore CreateDefault()
    {
        return Create(SecretStoreKind.System);
    }

    public static ISecretStore Create(SecretStoreKind kind)
    {
        return kind switch
        {
            SecretStoreKind.Memory => new MemorySecretStore(),
            SecretStoreKind.System => Create(CurrentPlatform()),
            _ => throw new NotSupportedException($"Unsupported secret store kind: {kind}"),
        };
    }

    public static ISecretStore Create(SecretStorePlatform platform)
    {
        return platform switch
        {
            SecretStorePlatform.Windows => new WindowsCredentialSecretStore(),
            SecretStorePlatform.MacOS => new MacOsKeychainSecretStore(),
            SecretStorePlatform.Linux => new FreedesktopSecretServiceStore(),
            _ => new UnsupportedSecretStore(),
        };
    }

    private static SecretStorePlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return SecretStorePlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return SecretStorePlatform.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return SecretStorePlatform.Linux;
        }

        return SecretStorePlatform.Unsupported;
    }
}
