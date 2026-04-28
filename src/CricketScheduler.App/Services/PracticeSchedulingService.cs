using CricketScheduler.App.Models;

namespace CricketScheduler.App.Services;

/// <summary>
/// Generates a weekday practice schedule in two phases per weekend:
///
///   Phase 1 — Playing teams: teams with a scheduled match that weekend are
///   placed at their match ground, Mon–Fri, up to 3 per slot.
///
///   Phase 2 — Non-playing fill: remaining slot capacity (below 3 teams/slot)
///   is filled with teams from the full league pool who have NO match that
///   weekend. Each non-playing team receives at most one slot per weekend.
///   Teams with the fewest cumulative practice sessions are prioritised first
///   so weekday assignments stay balanced across the whole season for everyone.
///
/// Hard constraints (never violated):
///   - Match opponents never share a slot.
///   - Max 3 teams per slot per day per ground.
///   - Playing teams practice at their match ground only.
///
/// Soft constraints (scoring penalty, relaxed to ensure full coverage):
///   - Prefer no same-division teams in the same slot (+5 to score).
///   - Prefer weekdays this team has used least (usage × 10 to score).
///   - Prefer slots with fewer teams already assigned (+count to score).
/// </summary>
public sealed class PracticeSchedulingService
{
    public List<PracticeSlot> Generate(League league)
    {
        // team → division lookup
        var teamDivision = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var div in league.Divisions)
            foreach (var team in div.Teams)
                teamDivision[team.Name] = div.Name;

        // Full team pool (all teams in the league)
        var allTeams = league.Divisions
            .SelectMany(d => d.Teams.Select(t => t.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

        // Per-team weekday usage [0=Mon…4=Fri] shared across all weeks for even distribution
        var teamDayUsage = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        var result = new List<PracticeSlot>();

        foreach (var weekendGroup in matchesByWeekend)
        {
            var saturday = weekendGroup.Key;
            var monday   = saturday.AddDays(-5); // Monday of that calendar week

            // Build match-opponent pairs (HARD constraint)
            var teamOpponent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in weekendGroup)
            {
                teamOpponent[match.TeamOne] = match.TeamTwo;
                teamOpponent[match.TeamTwo] = match.TeamOne;
            }

            // Build team → ground for teams playing this weekend
            var teamGround = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in weekendGroup)
            {
                teamGround.TryAdd(match.TeamOne, match.Ground!.Name);
                teamGround.TryAdd(match.TeamTwo, match.Ground!.Name);
            }

            // Collect all grounds that have at least one match this weekend
            var groundNames = teamGround.Values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // slots[groundName][dayIndex] = list of assigned team names (max 3)
            var groundSlots = groundNames.ToDictionary(
                g => g,
                _ => Enumerable.Range(0, 5).Select(_ => new List<string>()).ToArray(),
                StringComparer.OrdinalIgnoreCase);

            // ── Phase 1: place playing teams at their match ground ────────────
            var groundTeams = teamGround
                .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList(),
                              StringComparer.OrdinalIgnoreCase);

            foreach (var (groundName, teams) in groundTeams)
            {
                var slots     = groundSlots[groundName];
                var remaining = new List<string>(teams);

                while (remaining.Count > 0)
                {
                    // Most-constrained first: team blocked on the most days goes next
                    var next = remaining
                        .OrderByDescending(t => CountHardBlockedDays(t, slots, teamOpponent))
                        .ThenBy(t => t)
                        .First();
                    remaining.Remove(next);

                    AssignToSlot(next, slots, teamDivision, teamOpponent, teamDayUsage);
                }
            }

            // ── Phase 2: fill remaining capacity with non-playing teams ───────
            var nonPlaying = allTeams
                .Where(t => !teamGround.ContainsKey(t))
                .ToList();

            // Prioritise teams with the fewest cumulative practice sessions so far
            // (sum of all weekday usage counts) — this is the "make it even" rule.
            // After the first pass each team that gets a slot this weekend is skipped
            // so no non-playing team is assigned more than once per weekend.
            var assignedThisWeekend = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Keep picking the least-used unassigned non-playing team and placing them
            // in the best available slot across any ground, until no more slots remain.
            while (true)
            {
                var candidate = nonPlaying
                    .Where(t => !assignedThisWeekend.Contains(t))
                    .OrderBy(t => EnsureUsage(teamDayUsage, t).Sum())  // fewest total sessions
                    .ThenBy(t => t)
                    .FirstOrDefault();

                if (candidate is null) break; // all non-playing teams assigned for this weekend

                // Find the best (ground, day) slot across all grounds
                int    bestScore  = int.MaxValue;
                string? bestGround = null;
                int    bestDay    = -1;

                var usage = EnsureUsage(teamDayUsage, candidate);
                var div   = teamDivision.GetValueOrDefault(candidate, candidate);

                foreach (var (groundName, slots) in groundSlots)
                {
                    for (int dayIdx = 0; dayIdx < 5; dayIdx++)
                    {
                        var slotTeams = slots[dayIdx];
                        if (slotTeams.Count >= 3) continue;

                        bool hasSameDiv = slotTeams.Any(t =>
                            teamDivision.GetValueOrDefault(t, t) == div);

                        int score = usage[dayIdx] * 10 + slotTeams.Count + (hasSameDiv ? 5 : 0);
                        if (score < bestScore)
                        {
                            bestScore  = score;
                            bestGround = groundName;
                            bestDay    = dayIdx;
                        }
                    }
                }

                if (bestGround is null) break; // no remaining capacity anywhere

                groundSlots[bestGround][bestDay].Add(candidate);
                usage[bestDay]++;
                assignedThisWeekend.Add(candidate);
            }

            // ── Phase 3: emit PracticeSlots for every occupied slot ───────────
            foreach (var (groundName, slots) in groundSlots)
            {
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

    // Days where the slot is already full or the team's match opponent is present.
    private static int CountHardBlockedDays(
        string team, List<string>[] slots, Dictionary<string, string> teamOpponent)
    {
        var opponent = teamOpponent.GetValueOrDefault(team);
        int blocked  = 0;
        for (int d = 0; d < 5; d++)
        {
            if (slots[d].Count >= 3) { blocked++; continue; }
            if (opponent != null && SlotContains(slots[d], opponent)) blocked++;
        }
        return blocked;
    }

    // Find the best day for a playing team and assign them.
    private static void AssignToSlot(
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
            if (slotTeams.Count >= 3) continue;
            if (opponent != null && SlotContains(slotTeams, opponent)) continue;

            bool hasSameDiv = slotTeams.Any(t => teamDivision.GetValueOrDefault(t, t) == div);
            int score = usage[dayIdx] * 10 + slotTeams.Count + (hasSameDiv ? 5 : 0);

            if (score < bestScore) { bestScore = score; bestDay = dayIdx; }
        }

        if (bestDay < 0) return;

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
