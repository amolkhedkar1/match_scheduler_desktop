namespace CricketScheduler.App.Models;

public sealed class Match
{
    public int Sequence { get; set; }
    public required string TournamentName { get; init; }
    public required string DivisionName { get; init; }
    public string MatchType { get; init; } = "League";
    public required string TeamOne { get; init; }
    public required string TeamTwo { get; init; }

    public DateOnly? Date { get; set; }
    public TimeSlot? Slot { get; set; }
    public Ground? Ground { get; set; }
    public bool IsFixed { get; set; }

    public string? UmpireOne   { get; set; }
    public string? UmpireTwo   { get; set; }
    public string? UmpireThree { get; set; }
    public string? UmpireFour  { get; set; }
    public string? MatchManager { get; set; }
    public string? ScorerOne   { get; set; }
    public string? ScorerTwo   { get; set; }

    /// <summary>Populated for unscheduled matches — reason why scheduling failed.</summary>
    public string? UnscheduledReason { get; set; }
}
