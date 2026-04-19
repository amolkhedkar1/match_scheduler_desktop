namespace CricketScheduler.App.Models;

public sealed class Tournament
{
    public required string Name { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public List<DateOnly> DiscardedDates { get; init; } = [];
    public List<Ground> Grounds { get; init; } = [];
    public List<TimeSlot> TimeSlots { get; init; } = [];
}
