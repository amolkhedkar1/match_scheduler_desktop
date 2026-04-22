namespace CricketScheduler.App.Models;

public sealed class SchedulingRequest
{
    public required string TeamName { get; set; }
    public required DateOnly Date   { get; set; }
    public TimeOnly? StartTime      { get; set; }
    public TimeOnly? EndTime        { get; set; }

    public bool IsFullDayBlock => StartTime is null || EndTime is null;
}
