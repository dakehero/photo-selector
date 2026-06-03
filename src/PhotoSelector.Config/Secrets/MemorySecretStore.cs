namespace PhotoSelector.Config.Secrets;

public sealed class MemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> secrets = new(StringComparer.Ordinal);

    public void Set(string keyRef, string secret)
    {
        secrets[keyRef] = secret;
    }

    public string? Get(string keyRef)
    {
        return secrets.GetValueOrDefault(keyRef);
    }

    public void Delete(string keyRef)
    {
        secrets.Remove(keyRef);
    }
}
