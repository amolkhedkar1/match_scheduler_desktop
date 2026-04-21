namespace CricketScheduler.App.Models;

/// <summary>
/// A ground+time-slot combination that must not be used for scheduling,
/// optionally restricted to a specific date or division.
/// </summary>
public sealed class ForbiddenSlot
{
    public string? GroundName { get; init; }
    public TimeSlot? TimeSlot  { get; init; }
    public DateOnly? Date      { get; init; }
    public string?  Division  { get; init; }

    public string Display =>
        $"{(Date.HasValue ? Date.Value.ToString("yyyy-MM-dd") : "Any date")}  " +
        $"| {(GroundName ?? "Any ground")}  " +
        $"| {(TimeSlot is null ? "Any slot" : $"{TimeSlot.Start:HH\\:mm}–{TimeSlot.End:HH\\:mm}")}  " +
        $"| {(Division ?? "All divisions")}";
}
