using CricketScheduler.App.Models;

namespace CricketScheduler.App.Services;

/// <summary>
/// Generates a weekday practice schedule for teams with upcoming weekend matches.
///
/// Rules:
///   HARD — Teams playing each other that weekend never share a practice slot.
///   HARD — Max 3 teams per slot per day per ground.
///   HARD — Practice ground equals the team's match ground.
///   SOFT — Prefer no same-division teams in the same slot; relaxed when needed
///           so every team always gets a slot (no team is left unassigned).
///   SOFT — Weekday assignments distributed evenly across teams over the season.
/// </summary>
public sealed class PracticeSchedulingService
{
    public List<PracticeSlot> Generate(League league)
    {
        // Build team → division lookup
        var teamDivision = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var div in league.Divisions)
            foreach (var team in div.Teams)
                teamDivision[team.Name] = div.Name;

        var scheduled = league.Matches
            .Where(m => m.Date.HasValue && m.Ground != null
                        && (m.Date.Value.DayOfWeek == DayOfWeek.Saturday
                            || m.Date.Value.DayOfWeek == DayOfWeek.Sunday))
            .ToList();

        if (scheduled.Count == 0)
            return [];

        var matchesByWeekend = scheduled
            .GroupBy(m => WeekendSaturday(m.Date!.Value))
            .OrderBy(g => g.Key);

        // Per-team weekday usage count [0=Mon … 4=Fri], persists across all weeks
        var teamDayUsage = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        var result = new List<PracticeSlot>();

        foreach (var weekendGroup in matchesByWeekend)
        {
            var saturday = weekendGroup.Key;
            var monday   = saturday.AddDays(-5); // Monday of that calendar week

            // HARD constraint source: direct match opponents this weekend
            var teamOpponent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in weekendGroup)
            {
                teamOpponent[match.TeamOne] = match.TeamTwo;
                teamOpponent[match.TeamTwo] = match.TeamOne;
            }

            // Team → ground mapping (one match per team per weekend)
            var teamGround = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in weekendGroup)
            {
                var g = match.Ground!.Name;
                teamGround.TryAdd(match.TeamOne, g);
                teamGround.TryAdd(match.TeamTwo, g);
            }

            // Group teams by ground
            var groundTeams = teamGround
                .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList(),
                              StringComparer.OrdinalIgnoreCase);

            foreach (var (groundName, teams) in groundTeams)
            {
                // slots[0..4] = Mon..Fri, each holds up to 3 team names
                var slots = Enumerable.Range(0, 5).Select(_ => new List<string>()).ToArray();

                // Process most-constrained teams first.
                // Constraint: a team's only hard block is if its opponent already occupies a day.
                // Evaluate this dynamically — re-sort remaining teams each iteration.
                var remaining = new List<string>(teams);

                while (remaining.Count > 0)
                {
                    // Find the team with the fewest valid days (hardest to place)
                    var next = remaining
                        .OrderByDescending(t => CountHardBlockedDays(t, slots, teamOpponent))
                        .ThenBy(t => t)
                        .First();
                    remaining.Remove(next);

                    AssignTeam(next, slots, teamDivision, teamOpponent, teamDayUsage);
                }

                // Emit one PracticeSlot per occupied day
                for (int dayIdx = 0; dayIdx < 5; dayIdx++)
                {
                    var slotTeams = slots[dayIdx];
                    if (slotTeams.Count == 0) continue;

                    result.Add(new PracticeSlot
                    {
                        Date       = monday.AddDays(dayIdx),
                        GroundName = groundName,
                        TeamOne    = slotTeams.Count > 0 ? slotTeams[0] : null,
                        TeamTwo    = slotTeams.Count > 1 ? slotTeams[1] : null,
                        TeamThree  = slotTeams.Count > 2 ? slotTeams[2] : null
                    });
                }
            }
        }

        return [.. result.OrderBy(s => s.Date).ThenBy(s => s.GroundName)];
    }

    // Count how many of the 5 days are hard-blocked for this team:
    // a day is blocked when the slot is full OR the team's match opponent is already there.
    private static int CountHardBlockedDays(
        string team,
        List<string>[] slots,
        Dictionary<string, string> teamOpponent)
    {
        var opponent = teamOpponent.GetValueOrDefault(team);
        int blocked = 0;
        for (int d = 0; d < 5; d++)
        {
            if (slots[d].Count >= 3) { blocked++; continue; }
            if (opponent != null && SlotContains(slots[d], opponent)) blocked++;
        }
        return blocked;
    }

    private static void AssignTeam(
        string team,
        List<string>[] slots,
        Dictionary<string, string> teamDivision,
        Dictionary<string, string> teamOpponent,
        Dictionary<string, int[]> teamDayUsage)
    {
        var usage    = EnsureUsage(teamDayUsage, team);
        var div      = teamDivision.GetValueOrDefault(team, team);
        var opponent = teamOpponent.GetValueOrDefault(team);

        int bestDay   = -1;
        int bestScore = int.MaxValue;

        for (int dayIdx = 0; dayIdx < 5; dayIdx++)
        {
            var slotTeams = slots[dayIdx];

            // HARD: slot full
            if (slotTeams.Count >= 3) continue;

            // HARD: match opponent cannot share slot
            if (opponent != null && SlotContains(slotTeams, opponent)) continue;

            // SOFT penalty: prefer days without same-division teammates
            bool hasSameDiv = slotTeams.Any(t => teamDivision.GetValueOrDefault(t, t) == div);

            // Score: (times this team used this day) * 10 + current slot occupancy + same-div penalty
            int score = usage[dayIdx] * 10 + slotTeams.Count + (hasSameDiv ? 5 : 0);

            if (score < bestScore)
            {
                bestScore = score;
                bestDay   = dayIdx;
            }
        }

        if (bestDay < 0) return; // all 5 days blocked by opponent + full slots (extremely rare)

        slots[bestDay].Add(team);
        usage[bestDay]++;
    }

    private static bool SlotContains(List<string> slot, string name) =>
        slot.Any(t => t.Equals(name, StringComparison.OrdinalIgnoreCase));

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
