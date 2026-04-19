namespace CricketScheduler.App.Models;

public sealed class SchedulingRequest
{
    public required string TeamName { get; init; }
    public required DateOnly Date { get; init; }
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }

    public bool IsFullDayBlock => StartTime is null || EndTime is null;
}
