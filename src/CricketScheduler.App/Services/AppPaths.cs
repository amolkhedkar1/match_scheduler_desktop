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
                {
                    return candidate;
                }
            }

            current = current.Parent;
        }

        if (!string.IsNullOrWhiteSpace(firstExisting))
        {
            return firstExisting;
        }

        // Fallback for first run outside repo structure.
        var fallback = Path.Combine(baseDirectory, "data", "leagues");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static bool HasAnyLeagueData(string leaguesRoot)
    {
        foreach (var leagueDir in Directory.GetDirectories(leaguesRoot))
        {
            if (File.Exists(Path.Combine(leagueDir, "tournament.csv")))
            {
                return true;
            }
        }

        return false;
    }
}
