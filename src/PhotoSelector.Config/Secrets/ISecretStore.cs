namespace PhotoSelector.Config.Secrets;

public interface ISecretStore
{
    string ProviderName { get; }

    void Set(string keyRef, string secret);

    string? Get(string keyRef);

    void Delete(string keyRef);
}
