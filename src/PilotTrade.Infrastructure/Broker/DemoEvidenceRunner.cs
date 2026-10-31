namespace PilotTrade.Infrastructure.Broker;

using System.Text.Json;
using PilotTrade.Domain;
using PilotTrade.Domain.Broker;

// Runs the probe for --demo-readonly-evidence. The output path is checked before the adapter is even created,
// so a bad path can never spend broker requests or lose a finished run.
public static class DemoEvidenceRunner
{
    public const string Flag = "--demo-readonly-evidence";
    public const int Success = 0, StepsFailed = 1, BadOutputPath = 2, WriteFailed = 3;

    public static async Task<int> RunAsync(string[] args, string? accountId, Func<IBrokerAdapter> createAdapter, IClock clock, TextWriter output, TextWriter error, CancellationToken ct)
    {
        var flagIndex = Array.IndexOf(args, Flag);
        if (flagIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[flagIndex + 1]) || args[flagIndex + 1].StartsWith("--", StringComparison.Ordinal))
        {
            error.WriteLine($"Usage: {Flag} <output.json> [--epic EPIC]");
            return BadOutputPath;
        }

        // dotnet run starts in the project folder, so the check uses the resolved path.
        var path = Path.GetFullPath(args[flagIndex + 1]);
        if (OutputPathProblem(path) is { } problem)
        {
            error.WriteLine($"{problem}: {path}. No broker request was made.");
            return BadOutputPath;
        }

        var epicIndex = Array.IndexOf(args, "--epic");
        var epic = epicIndex >= 0 && epicIndex + 1 < args.Length ? args[epicIndex + 1] : "EURUSD";
        var evidence = await new DemoReadOnlyProbe(createAdapter(), clock).RunAsync(accountId, epic, ct);
        var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
        try { await File.WriteAllTextAsync(path, json, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The evidence is sanitized, so it is safe to print rather than lose the run.
            error.WriteLine($"Could not write {path}. Evidence follows:");
            error.WriteLine(json);
            return WriteFailed;
        }

        output.WriteLine($"Evidence written to {path}");
        output.WriteLine(evidence.AllPassed ? "All read-only steps passed." : "One or more read-only steps failed.");
        return evidence.AllPassed ? Success : StepsFailed;
    }

    private static string? OutputPathProblem(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (folder is null || !Directory.Exists(folder)) return "Evidence folder not found";
        if (Directory.Exists(path)) return "Evidence path is a folder";
        var check = path + ".write-check";
        try
        {
            File.WriteAllText(check, "");
            File.Delete(check);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "Evidence folder is not writable"; }
    }
}
