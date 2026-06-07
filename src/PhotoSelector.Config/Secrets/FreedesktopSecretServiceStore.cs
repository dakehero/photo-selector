namespace PhotoSelector.Config.Secrets;

public sealed class FreedesktopSecretServiceStore : CommandSecretStore
{
    public FreedesktopSecretServiceStore()
        : base(
            keyRef => ("secret-tool", ["store", "--label", $"Photo Selector {keyRef}", "app", "photo-selector", "key_ref", keyRef]),
            keyRef => ("secret-tool", ["lookup", "app", "photo-selector", "key_ref", keyRef]),
            keyRef => ("secret-tool", ["clear", "app", "photo-selector", "key_ref", keyRef]))
    {
    }

    public override string ProviderName => "freedesktop-secret-service";
}
