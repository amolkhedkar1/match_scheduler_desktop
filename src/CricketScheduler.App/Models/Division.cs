namespace CricketScheduler.App.Models;

public sealed class Division
{
    public required string Name     { get; init; }
    public List<Team> Teams         { get; init; } = [];

    /// <summary>
    /// When true, all unique pairs within this division are scheduled (round robin).
    /// When false, <see cref="MatchesPerTeam"/> determines how many opponents each team faces.
    /// </summary>
    public bool IsRoundRobin  { get; set; } = true;

    /// <summary>
    /// Used only when <see cref="IsRoundRobin"/> is false.
    /// Number of distinct opponents each team should play.
    /// </summary>
    public int? MatchesPerTeam { get; set; }

    /// <summary>
    /// Pre-computed pairings for fixed mode — stored so scheduler can use them directly.
    /// Each tuple is (TeamA, TeamB). Populated by GeneratePairings command on the Division page.
    /// </summary>
    public List<(string TeamA, string TeamB)> FixedPairings { get; set; } = [];

    /// <summary>Human-readable summary shown in UI lists.</summary>
    public string ModeSummary =>
        IsRoundRobin
            ? "Round Robin"
            : $"Fixed — {MatchesPerTeam?.ToString() ?? "?"} matches/team" +
              (FixedPairings.Count > 0 ? $" ({FixedPairings.Count} pairs)" : "");
}
