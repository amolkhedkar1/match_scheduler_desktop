namespace CricketScheduler.App.Models;

public sealed class ConstraintViolation
{
    public required string MatchId { get; init; }
    public required string Rule { get; init; }
    public required string Reason { get; init; }
}
