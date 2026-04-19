namespace CricketScheduler.App.Models;

public sealed class League
{
    public required string Name { get; init; }
    public required Tournament Tournament { get; init; }
    public List<Division> Divisions { get; init; } = [];
    public List<SchedulingRequest> Constraints { get; init; } = [];
    public List<Match> Matches { get; init; } = [];
}
