namespace PhotoSelector.Config.Secrets;

public sealed class UnsupportedSecretStore : ISecretStore
{
    public string ProviderName => "unsupported";

    public void Set(string keyRef, string secret)
    {
        throw new NotSupportedException("System secret storage is not supported on this platform.");
    }

    public string? Get(string keyRef)
    {
        return null;
    }

    public void Delete(string keyRef)
    {
    }
}
