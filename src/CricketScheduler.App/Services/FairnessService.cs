using CricketScheduler.App.Models;

namespace CricketScheduler.App.Services;

public sealed class FairnessService
{
    public Dictionary<string, int> GroundUsage(IEnumerable<Match> matches) =>
        matches.Where(m => m.Ground is not null)
            .GroupBy(m => m.Ground!.Name)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> TimeSlotUsage(IEnumerable<Match> matches) =>
        matches.Where(m => m.Slot is not null)
            .GroupBy(m => $"{m.Slot!.Start:HH\\:mm}-{m.Slot!.End:HH\\:mm}")
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
}
