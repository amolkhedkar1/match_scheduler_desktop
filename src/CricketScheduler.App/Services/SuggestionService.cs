using CricketScheduler.App.Models;

namespace CricketScheduler.App.Services;

public sealed class SuggestionService
{
    public IReadOnlyList<string> SuggestConstraintRelaxations(IReadOnlyList<(Match Match, string Reason)> unscheduled)
    {
        var messages = new List<string>();
        if (unscheduled.Count == 0)
        {
            return messages;
        }

        if (unscheduled.Any(x => x.Reason.Contains("weekend", StringComparison.OrdinalIgnoreCase)))
        {
            messages.Add("Relax: one match per weekend.");
            messages.Add("Relax: max 2 consecutive no-match weekends per team.");
        }

        if (unscheduled.Any(x => x.Reason.Contains("Scheduling request", StringComparison.OrdinalIgnoreCase)))
        {
            messages.Add("Relax: team availability constraints.");
        }

        messages.Add("Relax: ground fairness.");
        messages.Add("Relax: time slot fairness.");
        return messages.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
