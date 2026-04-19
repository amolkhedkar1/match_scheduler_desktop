using CricketScheduler.App.Models;

namespace CricketScheduler.App.Services;

public sealed class ConstraintService
{
    public IEnumerable<ConstraintViolation> ValidateRequestOverlaps(IEnumerable<SchedulingRequest> requests)
    {
        var byTeamDate = requests.GroupBy(r => (r.TeamName, r.Date));
        foreach (var group in byTeamDate)
        {
            var fullDayCount = group.Count(x => x.IsFullDayBlock);
            if (fullDayCount > 1)
            {
                yield return new ConstraintViolation
                {
                    MatchId = $"{group.Key.TeamName}-{group.Key.Date:yyyyMMdd}",
                    Rule = "DuplicateFullDayConstraint",
                    Reason = "Multiple full-day unavailability entries for the same team/date."
                };
            }
        }
    }
}
