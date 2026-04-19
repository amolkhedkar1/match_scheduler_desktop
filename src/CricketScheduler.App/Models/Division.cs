namespace CricketScheduler.App.Models;

public sealed class Division
{
    public required string Name { get; init; }
    public List<Team> Teams { get; init; } = [];
    public bool IsRoundRobin { get; init; } = true;
    public int? MatchesPerTeam { get; init; }
}
