namespace PhotoSelector.Config.Secrets;

public interface ISecretStore
{
    void Set(string keyRef, string secret);

    string? Get(string keyRef);

    void Delete(string keyRef);
}
