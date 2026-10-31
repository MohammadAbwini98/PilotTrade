namespace PilotTrade.Infrastructure.Security;

using System.Diagnostics;
using System.Text;
using PilotTrade.Domain.Security;

public sealed record KeychainCommandResult(int ExitCode, string Output);

public interface IKeychainCommandRunner
{
    Task<KeychainCommandResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput, CancellationToken ct);
}

public sealed class SecurityToolRunner : IKeychainCommandRunner
{
    public async Task<KeychainCommandResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput, CancellationToken ct)
    {
        var info = new ProcessStartInfo("/usr/bin/security")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var a in arguments) info.ArgumentList.Add(a);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the macOS security tool.");
        if (standardInput is not null) await process.StandardInput.WriteAsync(standardInput);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        await error; // drained and discarded: it can echo command text
        return new KeychainCommandResult(process.ExitCode, await output);
    }
}

// Generic passwords in the user's login Keychain; persistent and needs no administrator rights.
// Writes go through `security -i` on stdin so a secret never appears in process arguments.
public sealed class MacOsKeychainSecretStore : ISecretStore
{
    private const int NotFoundExitCode = 44; // errSecItemNotFound
    private const string Account = "PilotTrade";
    private readonly bool _allowProtectedMutations;
    private readonly IKeychainCommandRunner _runner;

    public MacOsKeychainSecretStore(bool allowProtectedMutations = false, IKeychainCommandRunner? runner = null)
    {
        _allowProtectedMutations = allowProtectedMutations;
        _runner = runner ?? new SecurityToolRunner();
    }

    private void GuardProtected(string target, string operation)
    {
        if (CredentialTargets.IsProtected(target) && !_allowProtectedMutations)
            throw new InvalidOperationException($"Protected credential target '{target}' cannot be {operation} by this caller.");
    }

    private static string Quote(string value)
    {
        if (value.Any(char.IsControl)) throw new ArgumentException("Credential values cannot contain control characters.");
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    public async Task SetAsync(string key, string value, CancellationToken ct)
    {
        var target = CredentialTargets.For(key);
        GuardProtected(target, "written");
        var command = new StringBuilder("add-generic-password -U -T /usr/bin/security")
            .Append(" -a ").Append(Quote(Account))
            .Append(" -s ").Append(Quote(target))
            .Append(" -w ").Append(Quote(value))
            .Append('\n').ToString();
        await _runner.RunAsync(new[] { "-i" }, command, ct);

        // Interactive mode does not report a failed command through its exit code, so confirm by reading back.
        if (await GetAsync(key, ct) != value)
            throw new InvalidOperationException("The macOS Keychain did not store the credential.");
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        var target = CredentialTargets.For(key);
        var result = await _runner.RunAsync(new[] { "find-generic-password", "-a", Account, "-s", target, "-w" }, null, ct);
        if (result.ExitCode == NotFoundExitCode) return null;
        if (result.ExitCode != 0) throw new InvalidOperationException($"The macOS Keychain read failed (exit {result.ExitCode}).");
        return result.Output.TrimEnd('\n');
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        var target = CredentialTargets.For(key);
        GuardProtected(target, "deleted");
        var result = await _runner.RunAsync(new[] { "delete-generic-password", "-a", Account, "-s", target }, null, ct);
        if (result.ExitCode != 0 && result.ExitCode != NotFoundExitCode)
            throw new InvalidOperationException($"The macOS Keychain delete failed (exit {result.ExitCode}).");
    }
}
