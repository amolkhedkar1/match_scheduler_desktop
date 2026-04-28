namespace CricketScheduler.App.Models;

public sealed class PracticeSlot
{
    public required DateOnly Date { get; init; }
    public required string GroundName { get; init; }
    public string? TeamOne { get; set; }
    public string? TeamTwo { get; set; }
    public string? TeamThree { get; set; }

    public IEnumerable<string> Teams =>
        new[] { TeamOne, TeamTwo, TeamThree }
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t!);
}
