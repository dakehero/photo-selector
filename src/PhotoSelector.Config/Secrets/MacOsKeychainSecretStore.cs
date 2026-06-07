namespace PhotoSelector.Config.Secrets;

public sealed class MacOsKeychainSecretStore : CommandSecretStore
{
    public MacOsKeychainSecretStore()
        : base(
            keyRef => ("security", ["add-generic-password", "-U", "-s", ToServiceName(keyRef), "-a", Environment.UserName, "-w"]),
            keyRef => ("security", ["find-generic-password", "-s", ToServiceName(keyRef), "-a", Environment.UserName, "-w"]),
            keyRef => ("security", ["delete-generic-password", "-s", ToServiceName(keyRef), "-a", Environment.UserName]))
    {
    }

    public override string ProviderName => "macos-keychain";

    private static string ToServiceName(string keyRef)
    {
        return $"PhotoSelector:{keyRef}";
    }
}
