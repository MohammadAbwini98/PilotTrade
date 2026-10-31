namespace PilotTrade.IntegrationTests;

using System.Runtime.Versioning;
using System.Text.RegularExpressions;

// Real OS stores: only the throwaway target is ever written, and protected targets are only tested for refusal.
[Trait("Category", "Credentials")]
public class WindowsCredentialStoreTests
{
    [WindowsFact, SupportedOSPlatform("windows")]
    public async Task ProtectedDemoTarget_CannotBeMutatedByDefault()
    {
        var store = new WindowsCredentialManagerSecretStore(allowProtectedMutations: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetAsync("capital:Demo:api_key", "x", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync("capital:Demo:api_key", CancellationToken.None));
    }

    [WindowsFact, SupportedOSPlatform("windows")]
    public async Task ProtectedLiveTarget_CannotBeMutatedByDefault()
    {
        var store = new WindowsCredentialManagerSecretStore(allowProtectedMutations: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetAsync("capital:Live:api_key", "x", CancellationToken.None));
    }

    [WindowsFact, SupportedOSPlatform("windows")]
    public async Task RoundTrip_GenericCredential()
    {
        var store = new WindowsCredentialManagerSecretStore();
        var key = "PilotTrade/Test/RoundTrip";
        await store.SetAsync(key, "TEST_VALUE_123", CancellationToken.None);
        (await store.GetAsync(key, CancellationToken.None)).Should().Be("TEST_VALUE_123");
        await store.DeleteAsync(key, CancellationToken.None);
        (await store.GetAsync(key, CancellationToken.None)).Should().BeNull();
    }

    [WindowsFact, SupportedOSPlatform("windows")]
    public void PlatformFactory_UsesCredentialManager() =>
        PlatformSecretStores.Create(false).Should().BeOfType<WindowsCredentialManagerSecretStore>();
}

[Trait("Category", "Credentials")]
public class MacKeychainStoreTests
{
    [MacFact]
    public async Task ProtectedTargets_CannotBeMutatedByDefault()
    {
        var store = new MacOsKeychainSecretStore();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetAsync("capital:Demo:api_key", "x", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync("capital:Live:api_key", CancellationToken.None));
    }

    [MacFact]
    public async Task RoundTrip_PersistsAcrossInstances_WithSpecialCharacters()
    {
        var key = "PilotTrade/Test/RoundTrip";
        var value = "TEST \"value\" \\ 123";
        await new MacOsKeychainSecretStore().SetAsync(key, value, CancellationToken.None);
        (await new MacOsKeychainSecretStore().GetAsync(key, CancellationToken.None)).Should().Be(value);
        await new MacOsKeychainSecretStore().DeleteAsync(key, CancellationToken.None);
        (await new MacOsKeychainSecretStore().GetAsync(key, CancellationToken.None)).Should().BeNull();
    }

    [MacFact]
    public void PlatformFactory_UsesKeychain() =>
        PlatformSecretStores.Create(false).Should().BeOfType<MacOsKeychainSecretStore>();
}

// Runs on every OS: the store's command contract, against a stand-in for /usr/bin/security.
[Trait("Category", "Credentials")]
public class KeychainCommandContractTests
{
    private sealed class FakeSecurityTool : IKeychainCommandRunner
    {
        public Dictionary<string, string> Items { get; } = new();
        public List<IReadOnlyList<string>> Arguments { get; } = new();
        public int? ForcedExitCode { get; set; }
        public bool DropWrites { get; set; }

        public Task<KeychainCommandResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput, CancellationToken ct)
        {
            Arguments.Add(arguments);
            if (ForcedExitCode is { } code) return Task.FromResult(new KeychainCommandResult(code, ""));
            string Target() => arguments[arguments.ToList().IndexOf("-s") + 1];
            switch (arguments[0])
            {
                case "-i":
                    var match = Regex.Match(standardInput!, "^add-generic-password -U -T /usr/bin/security -a \"PilotTrade\" -s \"(.+?)\" -w \"(.*)\"\n$");
                    match.Success.Should().BeTrue();
                    if (!DropWrites) Items[match.Groups[1].Value] = match.Groups[2].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                    return Task.FromResult(new KeychainCommandResult(0, ""));
                case "find-generic-password":
                    return Task.FromResult(Items.TryGetValue(Target(), out var value) ? new KeychainCommandResult(0, value + "\n") : new KeychainCommandResult(44, ""));
                case "delete-generic-password":
                    return Task.FromResult(new KeychainCommandResult(Items.Remove(Target()) ? 0 : 44, ""));
            }
            throw new InvalidOperationException("Unexpected command");
        }
    }

    [Fact]
    public async Task Secret_IsNeverInProcessArguments_AndUsesTheDocumentedTarget()
    {
        var tool = new FakeSecurityTool();
        var store = new MacOsKeychainSecretStore(allowProtectedMutations: true, tool);
        await store.SetAsync("capital:Demo:api_key", "S3CRET \"quoted\" \\", CancellationToken.None);
        tool.Items["CapitalTrading/Demo/ApiKey"].Should().Be("S3CRET \"quoted\" \\");
        tool.Arguments.SelectMany(a => a).Should().NotContain(a => a.Contains("S3CRET"));
        (await store.GetAsync("capital:Demo:api_key", CancellationToken.None)).Should().Be("S3CRET \"quoted\" \\");
    }

    [Fact]
    public async Task DemoAndLive_UseSeparateTargets()
    {
        var tool = new FakeSecurityTool();
        var store = new MacOsKeychainSecretStore(allowProtectedMutations: true, tool);
        await store.SetAsync("capital:Demo:password", "demo", CancellationToken.None);
        (await store.GetAsync("capital:Live:password", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task UnprivilegedCaller_IsRefusedBeforeAnyCommand()
    {
        var tool = new FakeSecurityTool();
        var store = new MacOsKeychainSecretStore(runner: tool);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetAsync("capital:Demo:password", "x", CancellationToken.None));
        tool.Arguments.Should().BeEmpty();
    }

    [Fact]
    public async Task UnconfirmedWrite_Fails()
    {
        var store = new MacOsKeychainSecretStore(allowProtectedMutations: true, new FakeSecurityTool { DropWrites = true });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetAsync("capital:Demo:password", "S3CRET", CancellationToken.None));
        ex.Message.Should().NotContain("S3CRET");
    }

    [Fact]
    public async Task LockedKeychain_IsAnError_NotMissing()
    {
        var store = new MacOsKeychainSecretStore(runner: new FakeSecurityTool { ForcedExitCode = 36 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAsync("capital:Demo:password", CancellationToken.None));
    }

    [Fact]
    public async Task ControlCharacters_AreRefused_SoNoCommandCanBeInjected()
    {
        var tool = new FakeSecurityTool();
        var store = new MacOsKeychainSecretStore(allowProtectedMutations: true, tool);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync("k", "x\"\ndelete-keychain", CancellationToken.None));
        tool.Arguments.Should().BeEmpty();
    }
}

[Trait("Category", "Credentials")]
public class CredentialManagerRollbackTests
{
    private sealed class FailingStore : ISecretStore
    {
        private readonly InMemorySecretStore _inner = new();
        public int FailOnWrite { get; set; } = int.MaxValue;
        private int _writes;
        public Task SetAsync(string key, string value, CancellationToken ct)
        {
            if (++_writes == FailOnWrite) throw new IOException("store detail");
            return _inner.SetAsync(key, value, ct);
        }
        public Task<string?> GetAsync(string key, CancellationToken ct) => _inner.GetAsync(key, ct);
        public Task DeleteAsync(string key, CancellationToken ct) => _inner.DeleteAsync(key, ct);
    }

    private static async Task<string?[]> Read(ISecretStore store) => new[]
    {
        await store.GetAsync("capital:Demo:api_key", CancellationToken.None),
        await store.GetAsync("capital:Demo:password", CancellationToken.None),
        await store.GetAsync("capital:Demo:identifier", CancellationToken.None)
    };

    [Fact]
    public async Task FailedReplacement_RestoresThePreviousSet()
    {
        var store = new FailingStore();
        var manager = new PrivilegedCredentialManager(store);
        await manager.ConfigureDemoCredentialsAsync("k1", "p1", "i1", false, CancellationToken.None);
        store.FailOnWrite = 6; // the second write of the replacement
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ConfigureDemoCredentialsAsync("k2", "p2", "i2", true, CancellationToken.None));
        ex.Message.Should().NotContain("store detail");
        (await Read(store)).Should().Equal("k1", "p1", "i1");
    }

    [Fact]
    public async Task FailedFirstSetup_LeavesNothingBehind()
    {
        var store = new FailingStore { FailOnWrite = 3 };
        var manager = new PrivilegedCredentialManager(store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ConfigureDemoCredentialsAsync("k", "p", "i", false, CancellationToken.None));
        (await Read(store)).Should().AllSatisfy(v => v.Should().BeNull());
    }

    [Fact]
    public async Task ExistingCredentials_NeedExplicitReplace()
    {
        var store = new FailingStore();
        var manager = new PrivilegedCredentialManager(store);
        await manager.ConfigureDemoCredentialsAsync("k1", "p1", "i1", false, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ConfigureDemoCredentialsAsync("k2", "p2", "i2", false, CancellationToken.None));
        (await Read(store)).Should().Equal("k1", "p1", "i1");
    }

    [Fact]
    public async Task UnsupportedPlatformStore_FailsClosed()
    {
        var store = new UnsupportedPlatformSecretStore();
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => store.GetAsync("k", CancellationToken.None));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => store.SetAsync("k", "v", CancellationToken.None));
    }
}
