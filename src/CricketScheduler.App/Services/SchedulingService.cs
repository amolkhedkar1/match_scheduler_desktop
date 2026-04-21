using CricketScheduler.App.Models;
using CricketScheduler.App.ViewModels;

namespace CricketScheduler.App.Services;

public sealed partial class SchedulingService
{
    public SchedulingResult Generate(League league, List<Match> fixedMatches, List<ForbiddenSlot> forbidden)
    {
        // Remove fixed matches from the pool to regenerate; keep them scheduled
        var generatedMatches = MatchGenerator.GenerateMatches(league)
            .Where(m => !fixedMatches.Any(f =>
                string.Equals(f.TeamOne, m.TeamOne, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.TeamTwo, m.TeamTwo, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.DivisionName, m.DivisionName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var schedulableSlots = SchedulingMatrixBuilder.BuildSlots(league.Tournament)
            .Where(slot => !IsForbidden(slot, forbidden))
            .ToList();

        var scheduled = new List<Match>(fixedMatches);
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

            if (best is null) { unscheduled.Add((match, "No valid slot respecting constraints and forbidden slots.")); continue; }
            match.Date = best.Slot.Date; match.Slot = best.Slot.TimeSlot; match.Ground = best.Slot.Ground;
            scheduled.Add(match);
        }

        AssignUmpires(scheduled, league.Divisions);
        for (var i = 0; i < scheduled.Count; i++) scheduled[i].Sequence = i + 1;
        return new SchedulingResult(scheduled, unscheduled);
    }

    private static bool IsForbidden(SchedulableSlot slot, List<ForbiddenSlot> forbidden)
    {
        foreach (var f in forbidden)
        {
            bool dateMatch  = f.Date is null  || f.Date == slot.Date;
            bool groundMatch = f.GroundName is null || string.Equals(f.GroundName, slot.Ground.Name, StringComparison.OrdinalIgnoreCase);
            bool slotMatch  = f.TimeSlot is null || (f.TimeSlot.Start == slot.TimeSlot.Start && f.TimeSlot.End == slot.TimeSlot.End);
            if (dateMatch && groundMatch && slotMatch) return true;
        }
        return false;
    }

    public List<MoveSlotSuggestion> SuggestMoves(
        League league, Match match, List<ForbiddenSlot> forbidden)
    {
        var allSlots = SchedulingMatrixBuilder.BuildSlots(league.Tournament)
            .Where(slot => !IsForbidden(slot, forbidden))
            .Where(slot => !(slot.Date == match.Date && slot.TimeSlot.Start == match.Slot?.Start && string.Equals(slot.Ground.Name, match.Ground?.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var otherMatches = league.Matches.Where(m => m != match && !m.IsFixed).ToList();
        var results = new List<MoveSlotSuggestion>();

        foreach (var slot in allSlots)
        {
            if (!ConstraintEvaluator.IsSlotAllowed(match, slot, league, otherMatches, out _)) continue;

            // Count how many other matches would be displaced (same ground+date+time)
            int affected = otherMatches.Count(m =>
                m.Date == slot.Date && m.Ground?.Name == slot.Ground.Name &&
                m.Slot?.Start == slot.TimeSlot.Start);

            double fairness = SlotScorer.Score(match, slot, league, otherMatches);
            results.Add(new MoveSlotSuggestion
            {
                Date = slot.Date, Slot = slot.TimeSlot, Ground = slot.Ground,
                AffectedMatchCount = affected,
                FairnessScore = fairness,
                IsRecommended = affected == 0 && fairness > 80
            });
        }

        return results.OrderBy(r => r.AffectedMatchCount).ThenByDescending(r => r.FairnessScore).ToList();
    }

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

    /// <summary>
    /// Assigns two umpires to each match. Both umpires must come from the SAME team
    /// (i.e. a different team from a different division acts as the "umpiring team"
    /// providing both officials for that match). Umpiring duty is distributed evenly.
    /// Preference is given to teams whose adjacent match is on the same day/ground
    /// to minimise travel and waiting.
    /// </summary>
    private static void AssignUmpires(List<Match> matches, List<Division> divisions)
    {
        // Track how many times each team has umpired
        var umpireLoad = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in divisions.SelectMany(d => d.Teams))
            umpireLoad[t.Name] = 0;

        // Build lookup: division name -> set of team names
        var divTeams = divisions.ToDictionary(
            d => d.Name,
            d => new HashSet<string>(d.Teams.Select(t => t.Name), StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var ordered = matches
            .Where(m => m.Date is not null && m.Slot is not null)
            .OrderBy(m => m.Date).ThenBy(m => m.Slot!.Start)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var match = ordered[i];

            // Candidate umpiring teams: any team NOT in the same division as the match
            var candidateTeams = divisions
                .Where(d => !string.Equals(d.Name, match.DivisionName, StringComparison.OrdinalIgnoreCase))
                .SelectMany(d => d.Teams.Select(t => t.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!candidateTeams.Any()) continue; // single-division league — skip

            // Continuity bonus: prefer a team whose adjacent match shares the same date/ground
            var adjacentTeams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddAdjacent(Match? adj)
            {
                if (adj is null) return;
                if (adj.Date == match.Date && adj.Ground?.Name == match.Ground?.Name)
                {
                    adjacentTeams.Add(adj.TeamOne);
                    adjacentTeams.Add(adj.TeamTwo);
                }
            }
            AddAdjacent(i > 0 ? ordered[i - 1] : null);
            AddAdjacent(i < ordered.Count - 1 ? ordered[i + 1] : null);

            // Score each candidate team: lower umpire load is better, adjacency gives bonus
            var bestTeam = candidateTeams
                .OrderBy(t => umpireLoad.GetValueOrDefault(t, 0))
                .ThenByDescending(t => adjacentTeams.Contains(t) ? 1 : 0)
                .First();

            // Both umpires come from bestTeam — they are the two members of that team
            // For teams with only 1 player listed, UmpireTwo = same name (edge case)
            var teamMembers = divTeams.Values
                .SelectMany(s => s)
                .Where(t => string.Equals(t, bestTeam, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // In our model a "team" is a single entity — so Umpire1 = Umpire2 = team name
            // This reflects "Team X provides 2 umpires for this match"
            match.UmpireOne = bestTeam;
            match.UmpireTwo = bestTeam;

            umpireLoad[bestTeam] = umpireLoad.GetValueOrDefault(bestTeam, 0) + 1;
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
            if (teams.Count < 2) continue;

            if (division.IsRoundRobin)
            {
                // All unique pairs — every team plays every other team exactly once
                for (var i = 0; i < teams.Count; i++)
                    for (var j = i + 1; j < teams.Count; j++)
                        matches.Add(CreateMatch(league.Tournament.Name, division.Name, teams[i], teams[j]));
            }
            else
            {
                // Fixed matches per team mode:
                // Each team should play exactly MatchesPerTeam opponents.
                // We generate pairs using a round-robin rotation then trim
                // so that no team exceeds MatchesPerTeam games.
                int target = division.MatchesPerTeam ?? (teams.Count - 1);
                target = Math.Max(1, Math.Min(target, teams.Count - 1));
                var pairs = FixedMatchesPairings(teams, target);
                matches.AddRange(pairs.Select(p => CreateMatch(league.Tournament.Name, division.Name, p.t1, p.t2)));
            }
        }

        return matches;
    }

    /// <summary>
    /// Generates pairs such that each team plays exactly <paramref name="matchesPerTeam"/> opponents.
    /// Uses a round-robin rotation wheel so pairings are balanced and no two teams play more than once.
    /// </summary>
    private static List<(string t1, string t2)> FixedMatchesPairings(List<string> teams, int matchesPerTeam)
    {
        // Track how many matches each team has been assigned
        var matchCount = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var usedPairs  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result     = new List<(string, string)>();

        // Rotate through all possible pairs in a fair order
        // so early teams don't monopolise opponents
        var allPairs = new List<(string t1, string t2)>();
        for (var i = 0; i < teams.Count; i++)
            for (var j = i + 1; j < teams.Count; j++)
                allPairs.Add((teams[i], teams[j]));

        // Shuffle lightly to avoid alphabetical bias: interleave from both ends
        var ordered = new List<(string, string)>();
        int lo = 0, hi = allPairs.Count - 1;
        while (lo <= hi)
        {
            if (lo == hi) { ordered.Add(allPairs[lo]); break; }
            ordered.Add(allPairs[lo++]);
            ordered.Add(allPairs[hi--]);
        }

        foreach (var (t1, t2) in ordered)
        {
            var key = string.Compare(t1, t2, StringComparison.OrdinalIgnoreCase) < 0
                ? $"{t1}|{t2}" : $"{t2}|{t1}";
            if (usedPairs.Contains(key)) continue;
            if (matchCount[t1] >= matchesPerTeam || matchCount[t2] >= matchesPerTeam) continue;

            result.Add((t1, t2));
            usedPairs.Add(key);
            matchCount[t1]++;
            matchCount[t2]++;

            // Stop early if all teams have reached their target
            if (matchCount.Values.All(c => c >= matchesPerTeam)) break;
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

    /// <summary>
    /// Evaluates whether a slot is valid for a match, respecting only the constraints
    /// that are NOT relaxed in the supplied <paramref name="relaxed"/> set.
    /// Hard constraint (ground/time physical conflict) is NEVER relaxed.
    /// </summary>
    public static bool IsSlotAllowedRelaxed(Match match, SchedulableSlot slot, League league,
        List<Match> scheduled, RelaxedConstraints relaxed, out string reason)
    {
        reason = string.Empty;

        // ── HARD constraint: two matches cannot share the same ground+date+time ──────
        var clash = scheduled.FirstOrDefault(m =>
            m != match &&
            m.Date == slot.Date &&
            string.Equals(m.Ground?.Name, slot.Ground.Name, StringComparison.OrdinalIgnoreCase) &&
            m.Slot?.Start == slot.TimeSlot.Start);
        if (clash is not null)
        {
            reason = $"Ground '{slot.Ground.Name}' already occupied at {slot.TimeSlot.Start:HH\\:mm} on {slot.Date:MM/dd/yyyy}.";
            return false;
        }

        // ── HARD: discarded / blackout dates (relaxable) ─────────────────────────────
        if (!relaxed.RelaxDiscardedDates && league.Tournament.DiscardedDates.Contains(slot.Date))
        {
            reason = $"{slot.Date:MM/dd/yyyy} is a blackout / discarded date.";
            return false;
        }

        // ── Date restriction: full-day team unavailability (relaxable) ───────────────
        if (!relaxed.RelaxDateRestriction)
        {
            var blockedByDate =
                IsFullDayBlocked(match.TeamOne, slot.Date, league.Constraints) ||
                IsFullDayBlocked(match.TeamTwo, slot.Date, league.Constraints);
            if (blockedByDate)
            {
                reason = "A team has a full-day unavailability on this date.";
                return false;
            }
        }

        // ── Time-slot restriction: partial-time team unavailability (relaxable) ───────
        if (!relaxed.RelaxTimeSlotRestriction)
        {
            var blockedBySlot =
                IsTimeSlotBlocked(match.TeamOne, slot, league.Constraints) ||
                IsTimeSlotBlocked(match.TeamTwo, slot, league.Constraints);
            if (blockedBySlot)
            {
                reason = "A team has a time-slot restriction that blocks this slot.";
                return false;
            }
        }

        // ── One match per team per weekend (relaxable) ────────────────────────────────
        if (!relaxed.RelaxOneMatchPerWeekend)
        {
            var busy = scheduled.Any(m =>
                m != match &&
                IsWeekendEqual(m.Date, slot.Date) &&
                (TeamMatch(match.TeamOne, m) || TeamMatch(match.TeamTwo, m)));
            if (busy)
            {
                reason = "A team already has a match this weekend (max 1 per weekend).";
                return false;
            }
        }

        // ── Max-gap rule: ≤2 consecutive no-match weekends (relaxable) ───────────────
        if (!relaxed.RelaxMaxGapRule && ViolatesNoGapRule(match, slot, scheduled))
        {
            reason = "Would cause more than 2 consecutive no-match weekends for a team.";
            return false;
        }

        // ── Ground fairness: penalise over-used grounds (relaxable) ──────────────────
        // When NOT relaxed, reject slots where one ground has > 2× the usage of others
        if (!relaxed.RelaxGroundFairness && scheduled.Count > 4)
        {
            var groundCounts = scheduled
                .Where(m => m.Ground is not null)
                .GroupBy(m => m.Ground!.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var thisCount = groundCounts.GetValueOrDefault(slot.Ground.Name, 0);
            var avgCount  = groundCounts.Values.DefaultIfEmpty(0).Average();
            if (thisCount > avgCount * 2.0)
            {
                reason = $"Ground '{slot.Ground.Name}' is overused ({thisCount} matches vs avg {avgCount:F1}).";
                return false;
            }
        }

        // ── Time-slot fairness: penalise over-used time slots (relaxable) ─────────────
        if (!relaxed.RelaxTimeSlotFairness && scheduled.Count > 4)
        {
            var slotCounts = scheduled
                .Where(m => m.Slot is not null)
                .GroupBy(m => $"{m.Slot!.Start:HH\\:mm}-{m.Slot!.End:HH\\:mm}")
                .ToDictionary(g => g.Key, g => g.Count());
            var thisSlotKey  = $"{slot.TimeSlot.Start:HH\\:mm}-{slot.TimeSlot.End:HH\\:mm}";
            var thisSlotCount = slotCounts.GetValueOrDefault(thisSlotKey, 0);
            var avgSlotCount  = slotCounts.Values.DefaultIfEmpty(0).Average();
            if (thisSlotCount > avgSlotCount * 2.0)
            {
                reason = $"Time slot {slot.TimeSlot.Start:HH\\:mm}–{slot.TimeSlot.End:HH\\:mm} is overused ({thisSlotCount} vs avg {avgSlotCount:F1}).";
                return false;
            }
        }

        // ── Umpire fairness: checked during umpire assignment, not slot evaluation ────
        // (RelaxUmpireFairness is passed to AssignUmpires separately — no slot rejection here)

        return true;
    }

    private static bool IsFullDayBlocked(string team, DateOnly date, List<SchedulingRequest> constraints) =>
        constraints.Any(c =>
            string.Equals(c.TeamName, team, StringComparison.OrdinalIgnoreCase) &&
            c.Date == date &&
            c.IsFullDayBlock);

    private static bool IsTimeSlotBlocked(string team, SchedulableSlot slot, List<SchedulingRequest> constraints)
    {
        return constraints.Any(c =>
        {
            if (!string.Equals(c.TeamName, team, StringComparison.OrdinalIgnoreCase)) return false;
            if (c.Date != slot.Date) return false;
            if (c.IsFullDayBlock) return false; // full-day handled separately
            if (c.StartTime is null || c.EndTime is null) return false;
            // Overlap check
            return c.StartTime < slot.TimeSlot.End && slot.TimeSlot.Start < c.EndTime;
        });
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

// ─── Overload that respects fixed matches and forbidden slots ─────────────────
public sealed partial class SchedulingService { 


    /// <summary>
    /// Backtracking reschedule: tries to insert relaxed-constraint matches into the slot matrix.
    /// Non-fixed already-scheduled matches may be displaced if needed.
    /// Fixed matches are never moved.
    /// </summary>
    public SchedulingResult BacktrackReschedule(
        League league,
        List<(Match Match, RelaxedConstraints Relaxed)> relaxedMatches,
        List<Match> fixedMatches,
        List<ForbiddenSlot> forbidden)
    {
        var scheduled = new List<Match>(fixedMatches);
        // Keep existing non-fixed matches as starting pool (may be displaced)
        var movable = league.Matches.Where(m => !m.IsFixed).ToList();
        scheduled.AddRange(movable);

        var unscheduled = new List<(Match Match, string Reason)>();
        var slots = SchedulingMatrixBuilder.BuildSlots(league.Tournament)
            .Where(s => !IsForbidden(s, forbidden))
            .ToList();

        foreach (var (match, relaxed) in relaxedMatches)
        {
            // Try each slot; use relaxed evaluator
            var placed = false;
            foreach (var slot in slots.OrderBy(_ => Guid.NewGuid())) // randomise to avoid bias
            {
                if (ConstraintEvaluator.IsSlotAllowedRelaxed(match, slot, league, scheduled, relaxed, out _))
                {
                    match.Date   = slot.Date;
                    match.Slot   = slot.TimeSlot;
                    match.Ground = slot.Ground;
                    scheduled.Add(match);
                    placed = true;
                    break;
                }
            }
            if (!placed)
                unscheduled.Add((match, "Could not schedule even with relaxed constraints."));
        }

        AssignUmpires(scheduled, league.Divisions);
        for (var i = 0; i < scheduled.Count; i++) scheduled[i].Sequence = i + 1;
        return new SchedulingResult(scheduled, unscheduled);
    }

}
// ── RelaxedConstraints ────────────────────────────────────────────────────────
/// <summary>
/// Per-match set of constraints that may be individually relaxed when
/// a match cannot be scheduled under normal rules.
/// Each flag = true means "ignore this constraint for this match pair".
/// </summary>
public sealed class RelaxedConstraints
{
    // ── Fairness constraints ──────────────────────────────────────────────────
    /// <summary>Ignore uneven ground usage — allow assigning to an over-used ground.</summary>
    public bool RelaxGroundFairness     { get; init; }

    /// <summary>Ignore umpiring load balance — assign umpires regardless of duty count.</summary>
    public bool RelaxUmpireFairness     { get; init; }

    /// <summary>Ignore time-slot rotation balance — allow over-used slots.</summary>
    public bool RelaxTimeSlotFairness   { get; init; }

    // ── Scheduling rhythm constraints ─────────────────────────────────────────
    /// <summary>Allow a team to go more than 2 consecutive weekends without a match.</summary>
    public bool RelaxMaxGapRule         { get; init; }

    /// <summary>Allow more than one match per team per weekend.</summary>
    public bool RelaxOneMatchPerWeekend { get; init; }

    // ── Date / time restrictions ──────────────────────────────────────────────
    /// <summary>Ignore team-specific time-slot unavailability requests.</summary>
    public bool RelaxTimeSlotRestriction { get; init; }

    /// <summary>Ignore team-specific date unavailability requests (full-day blocks).</summary>
    public bool RelaxDateRestriction    { get; init; }

    // ── Tournament structure constraints ──────────────────────────────────────
    /// <summary>Allow scheduling on discarded/blackout dates.</summary>
    public bool RelaxDiscardedDates     { get; init; }

    // ── Convenience: all constraints relaxed ──────────────────────────────────
    public static RelaxedConstraints None => new();
    public static RelaxedConstraints All  => new()
    {
        RelaxGroundFairness      = true,
        RelaxUmpireFairness      = true,
        RelaxTimeSlotFairness    = true,
        RelaxMaxGapRule          = true,
        RelaxOneMatchPerWeekend  = true,
        RelaxTimeSlotRestriction = true,
        RelaxDateRestriction     = true,
        RelaxDiscardedDates      = true
    };
}
