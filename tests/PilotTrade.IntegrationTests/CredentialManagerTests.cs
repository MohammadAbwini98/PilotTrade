namespace PilotTrade.IntegrationTests;

public class CredentialManagerTests
{
    [Fact]
    public async Task ProtectedDemoTarget_CannotBeMutatedByDefault()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsCredentialManagerSecretStore(allowProtectedMutations: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetAsync("capital:Demo:api_key", "x", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync("capital:Demo:api_key", CancellationToken.None));
    }

    [Fact]
    public async Task ProtectedLiveTarget_CannotBeMutatedByDefault()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsCredentialManagerSecretStore(allowProtectedMutations: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetAsync("capital:Live:api_key", "x", CancellationToken.None));
    }

    [Fact]
    public async Task RoundTrip_GenericCredential()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsCredentialManagerSecretStore();
        var key = "PilotTrade/Test/RoundTrip";
        await store.SetAsync(key, "TEST_VALUE_123", CancellationToken.None);
        var val = await store.GetAsync(key, CancellationToken.None);
        val.Should().Be("TEST_VALUE_123");
        await store.DeleteAsync(key, CancellationToken.None);
        var after = await store.GetAsync(key, CancellationToken.None);
        after.Should().BeNull();
    }
}