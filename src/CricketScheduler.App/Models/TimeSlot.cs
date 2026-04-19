namespace CricketScheduler.App.Models;

public sealed class TimeSlot
{
    public required TimeOnly Start { get; init; }
    public required TimeOnly End { get; init; }
}
