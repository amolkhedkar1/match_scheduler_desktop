using CricketScheduler.App.Models;

namespace CricketScheduler.App.Services;

public sealed class SchedulingService
{
    public SchedulingResult Generate(League league)
    {
        var generatedMatches = MatchGenerator.GenerateMatches(league);
        var schedulableSlots = SchedulingMatrixBuilder.BuildSlots(league.Tournament);
        var scheduled = new List<Match>();
        var unscheduled = new List<(Match Match, string Reason)>();

        var candidates = generatedMatches
            .OrderByDescending(m => league.Constraints.Count(c => c.TeamName == m.TeamOne || c.TeamName == m.TeamTwo))
            .ThenBy(m => m.DivisionName)
            .ToList();

        foreach (var match in candidates)
        {
            var best = schedulableSlots
                .Where(slot => ConstraintEvaluator.IsSlotAllowed(match, slot, league, scheduled, out _))
                .Select(slot => new { Slot = slot, Score = SlotScorer.Score(match, slot, league, scheduled) })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (best is null)
            {
                unscheduled.Add((match, "No valid slot due to constraints and available matrix."));
                continue;
            }

            match.Date = best.Slot.Date;
            match.Slot = best.Slot.TimeSlot;
            match.Ground = best.Slot.Ground;
            scheduled.Add(match);
        }

        AssignUmpires(scheduled, league.Divisions);

        for (var i = 0; i < scheduled.Count; i++)
        {
            scheduled[i].Sequence = i + 1;
        }

        return new SchedulingResult(scheduled, unscheduled);
    }

    private static void AssignUmpires(List<Match> matches, List<Division> divisions)
    {
        var byTeamAssignments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var allTeamsByDivision = divisions.ToDictionary(d => d.Name, d => d.Teams.Select(t => t.Name).ToList(), StringComparer.OrdinalIgnoreCase);

        var ordered = matches.Where(m => m.Date is not null && m.Slot is not null).OrderBy(m => m.Date).ThenBy(m => m.Slot!.Start).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var match = ordered[i];
            if (!allTeamsByDivision.TryGetValue(match.DivisionName, out _))
            {
                continue;
            }

            // Prefer teams playing in the immediate previous/next match for continuity.
            var continuityCandidates = new List<string>();
            if (i > 0)
            {
                continuityCandidates.Add(ordered[i - 1].TeamOne);
                continuityCandidates.Add(ordered[i - 1].TeamTwo);
            }
            if (i < ordered.Count - 1)
            {
                continuityCandidates.Add(ordered[i + 1].TeamOne);
                continuityCandidates.Add(ordered[i + 1].TeamTwo);
            }

            var selected = continuityCandidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(team => !string.Equals(team, match.TeamOne, StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(team, match.TeamTwo, StringComparison.OrdinalIgnoreCase))
                .OrderBy(team => byTeamAssignments.GetValueOrDefault(team, 0))
                .Take(2)
                .ToList();

            if (selected.Count < 2)
            {
                var fallback = divisions
                    .Where(d => !string.Equals(d.Name, match.DivisionName, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(d => d.Teams)
                    .Select(t => t.Name)
                    .Where(team => !selected.Contains(team, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(team => byTeamAssignments.GetValueOrDefault(team, 0))
                    .Take(2 - selected.Count)
                    .ToList();

                selected.AddRange(fallback);
            }

            match.UmpireOne = selected.ElementAtOrDefault(0);
            match.UmpireTwo = selected.ElementAtOrDefault(1);

            foreach (var ump in selected)
            {
                byTeamAssignments[ump] = byTeamAssignments.GetValueOrDefault(ump, 0) + 1;
            }
        }
    }
}

public sealed record SchedulingResult(
    IReadOnlyList<Match> ScheduledMatches,
    IReadOnlyList<(Match Match, string Reason)> UnscheduledMatches);

internal static class MatchGenerator
{
    public static List<Match> GenerateMatches(League league)
    {
        var matches = new List<Match>();
        foreach (var division in league.Divisions)
        {
            var teams = division.Teams.Select(t => t.Name).OrderBy(x => x).ToList();
            if (division.IsRoundRobin)
            {
                for (var i = 0; i < teams.Count; i++)
                {
                    for (var j = i + 1; j < teams.Count; j++)
                    {
                        matches.Add(CreateMatch(league.Tournament.Name, division.Name, teams[i], teams[j]));
                    }
                }
            }
            else
            {
                var targetMatches = Math.Min(division.MatchesPerTeam ?? teams.Count, teams.Count);
                var pairs = CustomPairings(teams, targetMatches);
                matches.AddRange(pairs.Select(p => CreateMatch(league.Tournament.Name, division.Name, p.Item1, p.Item2)));
            }
        }

        return matches;
    }

    private static IEnumerable<(string, string)> CustomPairings(List<string> teams, int pairCount)
    {
        var result = new List<(string, string)>();
        var left = 0;
        var right = teams.Count - 1;
        while (left < right && result.Count < pairCount)
        {
            result.Add((teams[left], teams[right]));
            left++;
            right--;
        }
        return result;
    }

    private static Match CreateMatch(string tournamentName, string divisionName, string teamOne, string teamTwo) =>
        new()
        {
            TournamentName = tournamentName,
            DivisionName = divisionName,
            TeamOne = teamOne,
            TeamTwo = teamTwo
        };
}

internal sealed record SchedulableSlot(DateOnly Date, Ground Ground, TimeSlot TimeSlot);

internal static class SchedulingMatrixBuilder
{
    public static List<SchedulableSlot> BuildSlots(Tournament tournament)
    {
        var slots = new List<SchedulableSlot>();
        for (var date = tournament.StartDate; date <= tournament.EndDate; date = date.AddDays(1))
        {
            var day = date.DayOfWeek;
            if (day is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                continue;
            }

            if (tournament.DiscardedDates.Contains(date))
            {
                continue;
            }

            foreach (var ground in tournament.Grounds)
            {
                foreach (var slot in tournament.TimeSlots)
                {
                    slots.Add(new SchedulableSlot(date, ground, slot));
                }
            }
        }

        return slots;
    }
}

internal static class ConstraintEvaluator
{
    public static bool IsSlotAllowed(Match match, SchedulableSlot slot, League league, List<Match> scheduled, out string reason)
    {
        reason = string.Empty;
        var sameTimeExisting = scheduled.FirstOrDefault(m =>
            m.Date == slot.Date &&
            m.Ground?.Name == slot.Ground.Name &&
            m.Slot?.Start == slot.TimeSlot.Start &&
            m.Slot?.End == slot.TimeSlot.End);
        if (sameTimeExisting is not null)
        {
            reason = "Ground/time already used.";
            return false;
        }

        var teamBusyThisWeekend = scheduled.Any(m =>
            IsWeekendEqual(m.Date, slot.Date) &&
            (TeamMatch(match.TeamOne, m) || TeamMatch(match.TeamTwo, m)));
        if (teamBusyThisWeekend)
        {
            reason = "Team already has match this weekend.";
            return false;
        }

        if (IsBlockedBySchedulingRequest(match.TeamOne, slot, league.Constraints) ||
            IsBlockedBySchedulingRequest(match.TeamTwo, slot, league.Constraints))
        {
            reason = "Scheduling request blocks slot.";
            return false;
        }

        if (ViolatesNoGapRule(match, slot, scheduled))
        {
            reason = "Would violate max 2 consecutive no-match weekends.";
            return false;
        }

        return true;
    }

    private static bool IsBlockedBySchedulingRequest(string team, SchedulableSlot slot, List<SchedulingRequest> constraints)
    {
        var requests = constraints.Where(c => string.Equals(c.TeamName, team, StringComparison.OrdinalIgnoreCase) && c.Date == slot.Date).ToList();
        foreach (var req in requests)
        {
            if (req.IsFullDayBlock)
            {
                return true;
            }

            if (req.StartTime is not null && req.EndTime is not null)
            {
                var overlap = req.StartTime < slot.TimeSlot.End && slot.TimeSlot.Start < req.EndTime;
                if (overlap)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ViolatesNoGapRule(Match pending, SchedulableSlot candidate, List<Match> scheduled)
    {
        var teams = new[] { pending.TeamOne, pending.TeamTwo };
        foreach (var team in teams)
        {
            var existingWeekends = scheduled
                .Where(m => TeamMatch(team, m) && m.Date is not null)
                .Select(m => WeekendKey(m.Date!.Value))
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var withCandidate = existingWeekends.Append(WeekendKey(candidate.Date)).Distinct().OrderBy(d => d).ToList();
            if (withCandidate.Count < 2)
            {
                continue;
            }

            var maxGap = 0;
            for (var i = 1; i < withCandidate.Count; i++)
            {
                var weekendDiff = (int)((withCandidate[i] - withCandidate[i - 1]).TotalDays / 7) - 1;
                maxGap = Math.Max(maxGap, weekendDiff);
            }

            if (maxGap > 2)
            {
                return true;
            }
        }

        return false;
    }

    private static DateTime WeekendKey(DateOnly date)
    {
        var offsetToSaturday = date.DayOfWeek switch
        {
            DayOfWeek.Saturday => 0,
            DayOfWeek.Sunday => 1,
            _ => ((int)date.DayOfWeek + 1) % 7
        };
        return date.ToDateTime(TimeOnly.MinValue).AddDays(-offsetToSaturday);
    }
    private static bool TeamMatch(string team, Match m) => string.Equals(team, m.TeamOne, StringComparison.OrdinalIgnoreCase) || string.Equals(team, m.TeamTwo, StringComparison.OrdinalIgnoreCase);
    private static bool IsWeekendEqual(DateOnly? left, DateOnly right) => left is not null && WeekendKey(left.Value) == WeekendKey(right);
}

internal static class SlotScorer
{
    public static int Score(Match match, SchedulableSlot slot, League league, List<Match> scheduled)
    {
        var score = 0;

        // Prefer less-used grounds/times for fairness.
        var groundUse = scheduled.Count(m => string.Equals(m.Ground?.Name, slot.Ground.Name, StringComparison.OrdinalIgnoreCase));
        var timeUse = scheduled.Count(m => m.Slot?.Start == slot.TimeSlot.Start && m.Slot?.End == slot.TimeSlot.End);
        score += Math.Max(0, 100 - (groundUse * 10));
        score += Math.Max(0, 100 - (timeUse * 8));

        // Prefer earlier assignment when constraints are dense.
        var teamConstraints = league.Constraints.Count(c =>
            string.Equals(c.TeamName, match.TeamOne, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.TeamName, match.TeamTwo, StringComparison.OrdinalIgnoreCase));
        score += teamConstraints * 5;

        return score;
    }
}
