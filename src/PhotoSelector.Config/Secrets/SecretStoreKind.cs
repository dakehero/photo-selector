namespace PhotoSelector.Config.Secrets;

public enum SecretStoreKind
{
    System,
    Memory,
}

public enum SecretStorePlatform
{
    Windows,
    MacOS,
    Linux,
    Unsupported,
}
