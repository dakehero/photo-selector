namespace PhotoSelector.Config.Secrets;

public sealed class MacOsKeychainSecretStore : CommandSecretStore
{
    public MacOsKeychainSecretStore()
        : base(
            "security",
            "macOS security command was not found. Use api_key_env or run on a macOS user session with Keychain access.",
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
