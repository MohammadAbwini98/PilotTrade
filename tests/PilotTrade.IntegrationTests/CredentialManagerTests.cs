namespace PilotTrade.IntegrationTests;

public class CredentialManagerTests
{
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