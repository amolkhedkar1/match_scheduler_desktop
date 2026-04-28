namespace CricketScheduler.App.Models;

public sealed class League
{
    public required string Name       { get; init; }
    public required Tournament Tournament { get; init; }
    public List<Division> Divisions   { get; set; } = [];
    public List<SchedulingRequest> Constraints { get; set; } = [];
    public List<Match> Matches        { get; set; } = [];
    /// <summary>Matches that could not be scheduled — persisted across sessions.</summary>
    public List<Match> UnscheduledMatches { get; set; } = [];
    /// <summary>Generated practice slots — one weekday slot per ground per week.</summary>
    public List<PracticeSlot> PracticeSchedule { get; set; } = [];
}
