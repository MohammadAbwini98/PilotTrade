namespace PilotTrade.Api;

// A published build carries its UI beside the executable and may sit in a read-only folder.
// A development run does not, and keeps its working-directory defaults.
public static class PortableLayout
{
    public const string DefaultUrl = "http://127.0.0.1:5099";

    public static bool IsPackaged(string baseDirectory) =>
        File.Exists(Path.Combine(baseDirectory, "wwwroot", "index.html"));

    public static string DatabasePath(string localAppData) =>
        Path.Combine(localAppData, "PilotTrade", "pilottrade.db");

    public static string UserDatabaseConnection()
    {
        var path = DatabasePath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return $"Data Source={path}";
    }
}
