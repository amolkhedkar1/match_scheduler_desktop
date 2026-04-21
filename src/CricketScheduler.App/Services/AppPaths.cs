using System.IO;

namespace CricketScheduler.App.Services;

public static class AppPaths
{
    public static string ResolveLeaguesRoot()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var current = new DirectoryInfo(baseDirectory);
        string? firstExisting = null;

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "data", "leagues");
            if (Directory.Exists(candidate))
            {
                firstExisting ??= candidate;
                if (HasAnyLeagueData(candidate))
                    return candidate;
            }
            current = current.Parent;
        }

        if (!string.IsNullOrWhiteSpace(firstExisting))
            return firstExisting;

        var fallback = Path.Combine(baseDirectory, "data", "leagues");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>
    /// Returns (and creates) the outputs folder at the same level as the data folder.
    /// All exports go here — never into the data folder — so persistent data is never overwritten.
    /// </summary>
    public static string ResolveOutputsRoot()
    {
        // Walk up from leagues root: data/leagues -> data -> root -> outputs
        var leaguesRoot = ResolveLeaguesRoot();
        var dataRoot    = Path.GetDirectoryName(leaguesRoot)!;       // .../data
        var projectRoot = Path.GetDirectoryName(dataRoot)!;          // project root
        var outputsPath = Path.Combine(projectRoot, "outputs");
        Directory.CreateDirectory(outputsPath);
        return outputsPath;
    }

    /// <summary>Returns a timestamped filename inside the outputs folder.</summary>
    public static string TimestampedExportPath(string baseName, string extension = "csv")
    {
        var ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var file = $"{baseName}_{ts}.{extension}";
        return Path.Combine(ResolveOutputsRoot(), file);
    }

    private static bool HasAnyLeagueData(string leaguesRoot)
    {
        foreach (var leagueDir in Directory.GetDirectories(leaguesRoot))
            if (File.Exists(Path.Combine(leagueDir, "tournament.csv")))
                return true;
        return false;
    }
}
