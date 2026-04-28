using CricketScheduler.App.Models;

namespace CricketScheduler.App.Services;

/// <summary>
/// Generates a weekday practice schedule for teams with upcoming weekend matches.
/// Algorithm:
///   - For each weekend (Sat+Sun), identify teams with a scheduled match and their assigned ground.
///   - Practice slots are Mon–Fri of that same calendar week, at the team's match ground.
///   - Up to 3 teams share one slot per day per ground.
///   - No two teams from the same division share a slot.
///   - Weekdays are distributed evenly across teams over the full schedule.
/// </summary>
public sealed class PracticeSchedulingService
{
    public List<PracticeSlot> Generate(League league)
    {
        // Build team → division name lookup
        var teamDivision = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var div in league.Divisions)
            foreach (var team in div.Teams)
                teamDivision[team.Name] = div.Name;

        // Only consider matches that are fully scheduled (date + ground assigned)
        var scheduled = league.Matches
            .Where(m => m.Date.HasValue && m.Ground != null
                        && (m.Date.Value.DayOfWeek == DayOfWeek.Saturday
                            || m.Date.Value.DayOfWeek == DayOfWeek.Sunday))
            .ToList();

        if (scheduled.Count == 0)
            return [];

        // Group matches by the Saturday anchor of their weekend
        var matchesByWeekend = scheduled
            .GroupBy(m => WeekendSaturday(m.Date!.Value))
            .OrderBy(g => g.Key);

        // Track per-team weekday usage across all weeks for even distribution
        // Index 0=Mon … 4=Fri
        var teamDayUsage = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        var result = new List<PracticeSlot>();

        foreach (var weekendGroup in matchesByWeekend)
        {
            var saturday = weekendGroup.Key;
            var monday   = saturday.AddDays(-5); // Mon of that calendar week

            // Build team → ground mapping for this weekend; one entry per team
            // (a team can only have one match per weekend per the scheduling rules)
            var teamGround = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in weekendGroup)
            {
                var groundName = match.Ground!.Name;
                teamGround.TryAdd(match.TeamOne, groundName);
                teamGround.TryAdd(match.TeamTwo, groundName);
            }

            // Group teams by ground
            var groundTeams = teamGround
                .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList(),
                              StringComparer.OrdinalIgnoreCase);

            foreach (var (groundName, teams) in groundTeams)
            {
                // 5 practice days: Mon–Fri
                // slots[dayIndex] holds the teams assigned to that day (max 3)
                var slots = Enumerable.Range(0, 5).Select(_ => new List<string>()).ToArray();

                // Sort teams: most-constrained-day teams first (fewest valid days available)
                // so harder-to-place teams get first pick.
                var orderedTeams = teams.OrderByDescending(t =>
                {
                    var usage = EnsureUsage(teamDayUsage, t);
                    var div   = teamDivision.GetValueOrDefault(t, t);
                    // Count how many days are already unavailable for this team
                    int blocked = 0;
                    for (int d = 0; d < 5; d++)
                    {
                        if (slots[d].Count >= 3) blocked++;
                        else if (slots[d].Any(x => teamDivision.GetValueOrDefault(x, x) == div))
                            blocked++;
                    }
                    return blocked;
                }).ToList();

                foreach (var team in orderedTeams)
                {
                    var usage  = EnsureUsage(teamDayUsage, team);
                    var div    = teamDivision.GetValueOrDefault(team, team);

                    int bestDay   = -1;
                    int bestScore = int.MaxValue;

                    for (int dayIdx = 0; dayIdx < 5; dayIdx++)
                    {
                        var slotTeams = slots[dayIdx];

                        if (slotTeams.Count >= 3) continue;

                        // No same-division sharing
                        if (slotTeams.Any(t => teamDivision.GetValueOrDefault(t, t) == div))
                            continue;

                        // Score: prefer the day this team has used least,
                        // then prefer the day with fewer teams already assigned.
                        int score = usage[dayIdx] * 10 + slotTeams.Count;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestDay   = dayIdx;
                        }
                    }

                    if (bestDay < 0) continue; // could not place — skip

                    slots[bestDay].Add(team);
                    usage[bestDay]++;
                }

                // Emit one PracticeSlot per occupied day
                for (int dayIdx = 0; dayIdx < 5; dayIdx++)
                {
                    var slotTeams = slots[dayIdx];
                    if (slotTeams.Count == 0) continue;

                    result.Add(new PracticeSlot
                    {
                        Date      = monday.AddDays(dayIdx),
                        GroundName = groundName,
                        TeamOne   = slotTeams.Count > 0 ? slotTeams[0] : null,
                        TeamTwo   = slotTeams.Count > 1 ? slotTeams[1] : null,
                        TeamThree = slotTeams.Count > 2 ? slotTeams[2] : null
                    });
                }
            }
        }

        return [.. result.OrderBy(s => s.Date).ThenBy(s => s.GroundName)];
    }

    // Returns the Saturday of the weekend that date belongs to.
    private static DateOnly WeekendSaturday(DateOnly date) =>
        date.DayOfWeek == DayOfWeek.Saturday ? date : date.AddDays(-1);

    private static int[] EnsureUsage(Dictionary<string, int[]> map, string team)
    {
        if (!map.TryGetValue(team, out var usage))
        {
            usage = new int[5];
            map[team] = usage;
        }
        return usage;
    }
}
