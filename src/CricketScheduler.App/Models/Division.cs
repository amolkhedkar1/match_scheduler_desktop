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

    /// <summary>Human-readable summary shown in UI lists.</summary>
    public string ModeSummary =>
        IsRoundRobin
            ? "Round Robin"
            : $"Fixed — {MatchesPerTeam?.ToString() ?? "?"} matches/team";
}
