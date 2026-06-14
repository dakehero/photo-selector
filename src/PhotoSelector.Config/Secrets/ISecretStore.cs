namespace PhotoSelector.Config.Secrets;

public sealed record SecretStoreStatus(bool IsAvailable, string? Error);

public interface ISecretStore
{
    string ProviderName { get; }

    SecretStoreStatus GetStatus();

    void Set(string keyRef, string secret);

    string? Get(string keyRef);

    void Delete(string keyRef);
}
