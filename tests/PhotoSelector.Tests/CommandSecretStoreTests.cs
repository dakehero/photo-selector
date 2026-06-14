using PhotoSelector.Config.Secrets;

namespace PhotoSelector.Tests;

public sealed class CommandSecretStoreTests
{
    [Fact]
    public void Command_store_reports_unavailable_when_tool_is_missing()
    {
        var store = new TestCommandSecretStore("photo-selector-missing-secret-tool", "missing test tool");

        var status = store.GetStatus();

        Assert.False(status.IsAvailable);
        Assert.Equal("missing test tool", status.Error);
    }

    [Fact]
    public void Command_store_throws_readable_error_when_tool_is_missing()
    {
        var store = new TestCommandSecretStore("photo-selector-missing-secret-tool", "missing test tool");

        var setError = Assert.Throws<InvalidOperationException>(() => store.Set("key", "secret"));
        var getError = Assert.Throws<InvalidOperationException>(() => store.Get("key"));
        var deleteError = Assert.Throws<InvalidOperationException>(() => store.Delete("key"));

        Assert.Contains("missing test tool", setError.Message);
        Assert.Contains("missing test tool", getError.Message);
        Assert.Contains("missing test tool", deleteError.Message);
    }

    private sealed class TestCommandSecretStore(string toolName, string unavailableMessage)
        : CommandSecretStore(
            toolName,
            unavailableMessage,
            _ => (toolName, []),
            _ => (toolName, []),
            _ => (toolName, []))
    {
        public override string ProviderName => "test-command";
    }
}
