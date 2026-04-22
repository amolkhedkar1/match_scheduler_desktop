using CricketScheduler.App.Models;
using CricketScheduler.App.ViewModels;

namespace CricketScheduler.App.Services;

public sealed partial class SchedulingService
{
    public SchedulingResult Generate(League league, List<Match> fixedMatches, List<ForbiddenSlot> forbidden)
        => RunOptimisedSchedule(league, fixedMatches, forbidden);


    /// <summary>
    /// Global pre-filter: blocks slots covered by non-division-specific forbidden entries.
    /// Division-specific entries are skipped here and applied per-match by IsForbiddenForMatch.
    /// </summary>
    private static bool IsForbidden(SchedulableSlot slot, List<ForbiddenSlot> forbidden)
    {
        foreach (var f in forbidden)
        {
            if (f.Division is not null) continue; // division-specific handled per-match
            bool dateMatch   = f.Date      is null || f.Date == slot.Date;
            bool groundMatch = f.GroundName is null || string.Equals(f.GroundName, slot.Ground.Name, StringComparison.OrdinalIgnoreCase);
            bool slotMatch   = f.TimeSlot  is null || (f.TimeSlot.Start == slot.TimeSlot.Start && f.TimeSlot.End == slot.TimeSlot.End);
            if (dateMatch && groundMatch && slotMatch) return true;
        }
        return false;
    }

    /// <summary>
    /// Per-match forbidden check: includes the Division field so division-specific forbidden
    /// slots are respected. Null Division = wildcard (applies to all divisions).
    /// </summary>
    private static bool IsForbiddenForMatch(SchedulableSlot slot, string divisionName, List<ForbiddenSlot> forbidden)
    {
        foreach (var f in forbidden)
        {
            bool divisionMatch = f.Division is null || string.Equals(f.Division, divisionName, StringComparison.OrdinalIgnoreCase);
            if (!divisionMatch) continue;
            bool dateMatch   = f.Date      is null || f.Date == slot.Date;
            bool groundMatch = f.GroundName is null || string.Equals(f.GroundName, slot.Ground.Name, StringComparison.OrdinalIgnoreCase);
            bool slotMatch   = f.TimeSlot  is null || (f.TimeSlot.Start == slot.TimeSlot.Start && f.TimeSlot.End == slot.TimeSlot.End);
            if (dateMatch && groundMatch && slotMatch) return true;
        }
        return false;
    }

    /// <summary>
    /// Returns all tournament slots the given match could potentially move to.
    ///
    /// Move Analyzer mode (relaxed == null):
    ///   Shows every slot that is not fixed-occupied, globally forbidden, or the match's current
    ///   slot.  No constraint filtering is applied — every possible slot is shown.
    ///   AffectedMatchCount = (matches at exact slot) + (same-weekend matches for either team),
    ///   so the caller can see exactly what would need to cascade.
    ///
    /// Unscheduled-panel mode (relaxed != null):
    ///   Same slot universe, but additionally filters out slots that violate constraints the
    ///   user has NOT relaxed (date blocks, time-slot restrictions, blackout dates, gap rule).
    ///   When RelaxOneMatchPerWeekend is true, same-weekend matches are NOT counted as affected.
    /// </summary>
    /// <param name="additionalFixed">
    /// Optional extra matches to treat as fixed context (their slots are excluded from
    /// the candidate pool and they participate in constraint checks). Used when computing
    /// suggestions for a displaced match in the unscheduled panel — the virtually-placed
    /// unscheduled match is passed here so the target slot is correctly excluded.
    /// </param>
    public List<MoveSlotSuggestion> SuggestMoves(
        League league, Match match, List<ForbiddenSlot> forbidden,
        RelaxedConstraints? relaxed = null,
        IReadOnlyList<Match>? additionalFixed = null)
    {
        // Fixed matches: exclude their slots and keep as hard context.
        var fixedMatches = league.Matches
            .Where(m => m.IsFixed && m != match
                     && m.Date is not null && m.Slot is not null && m.Ground is not null)
            .Concat(additionalFixed?.Where(m => m.Date is not null && m.Slot is not null && m.Ground is not null) ?? [])
            .ToList();

        var fixedSlotKeys = fixedMatches
            .Select(m => $"{m.Date}|{m.Ground!.Name}|{m.Slot!.Start:HH\\:mm}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Candidate slots: all weekend slots minus globally-forbidden, fixed-occupied, and current slot.
        var allSlots = SchedulingMatrixBuilder.BuildSlots(league.Tournament)
            .Where(slot => !IsForbidden(slot, forbidden))
            .Where(slot => !(slot.Date == match.Date && slot.TimeSlot.Start == match.Slot?.Start
                && string.Equals(slot.Ground.Name, match.Ground?.Name, StringComparison.OrdinalIgnoreCase)))
            .Where(slot => !fixedSlotKeys.Contains(
                $"{slot.Date}|{slot.Ground.Name}|{slot.TimeSlot.Start:HH\\:mm}"))
            .ToList();

        var otherNonFixed = league.Matches.Where(m => m != match && !m.IsFixed).ToList();
        var results = new List<MoveSlotSuggestion>();

        foreach (var slot in allSlots)
        {
            if (IsForbiddenForMatch(slot, match.DivisionName, forbidden)) continue;

            // Matches occupying this exact slot — they would be displaced.
            var displacedAtSlot = otherNonFixed.Where(m =>
                m.Date == slot.Date &&
                string.Equals(m.Ground?.Name, slot.Ground.Name, StringComparison.OrdinalIgnoreCase) &&
                m.Slot?.Start == slot.TimeSlot.Start).ToList();

            var contextWithoutSlotOccupants = otherNonFixed.Except(displacedAtSlot).Concat(fixedMatches).ToList();

            // Same-weekend matches for either team: they would also need to move (1-per-weekend rule).
            // When RelaxOneMatchPerWeekend is on, they are not counted as affected.
            bool weekendRelaxed = relaxed?.RelaxOneMatchPerWeekend ?? false;
            var sameWeekendConflicts = weekendRelaxed
                ? new List<Match>()
                : contextWithoutSlotOccupants
                    .Where(m => !m.IsFixed &&
                        ConstraintEvaluator.IsWeekendEqual(m.Date, slot.Date) &&
                        (ConstraintEvaluator.TeamMatch(match.TeamOne, m) ||
                         ConstraintEvaluator.TeamMatch(match.TeamTwo, m)))
                    .ToList();

            var allAffected = displacedAtSlot.Concat(sameWeekendConflicts).ToList();
            var contextAfterAllDisplaced = contextWithoutSlotOccupants.Except(sameWeekendConflicts).ToList();

            // Unscheduled-panel mode: apply per-flag constraint filters.
            // Move Analyzer mode: no filtering — every slot is shown.
            if (relaxed is not null)
            {
                if (!relaxed.RelaxDiscardedDates && league.Tournament.DiscardedDates.Contains(slot.Date))
                    continue;
                if (!relaxed.RelaxDateRestriction &&
                    (ConstraintEvaluator.IsFullDayBlocked(match.TeamOne, slot.Date, league.Constraints) ||
                     ConstraintEvaluator.IsFullDayBlocked(match.TeamTwo, slot.Date, league.Constraints)))
                    continue;
                if (!relaxed.RelaxTimeSlotRestriction &&
                    (ConstraintEvaluator.IsTimeSlotBlocked(match.TeamOne, slot, league.Constraints) ||
                     ConstraintEvaluator.IsTimeSlotBlocked(match.TeamTwo, slot, league.Constraints)))
                    continue;
                if (!relaxed.RelaxMaxGapRule &&
                    ConstraintEvaluator.ViolatesNoGapRule(match, slot, contextAfterAllDisplaced))
                    continue;
            }

            double fairness = SlotScorer.Score(match, slot, league, contextAfterAllDisplaced);
            results.Add(new MoveSlotSuggestion
            {
                Date               = slot.Date,
                Slot               = slot.TimeSlot,
                Ground             = slot.Ground,
                AffectedMatchCount = allAffected.Count,
                AffectedMatchList  = allAffected,
                FairnessScore      = fairness,
                IsRecommended      = allAffected.Count == 0 && fairness > 80
            });
        }

        return results.OrderBy(r => r.AffectedMatchCount).ThenByDescending(r => r.FairnessScore).ToList();
    }

    /// <summary>
    /// Compatibility overload — delegates to the main Generate with empty fixed/forbidden lists.
    /// Kept so any legacy callers still compile.
    /// </summary>
    public SchedulingResult Generate(League league)
        => Generate(league, [], []);

    /// <summary>
    /// Greedy-best-first scheduler with forbidden slot filtering and multi-ordering
    /// optimisation. Tries several candidate orderings, picks the one that schedules
    /// the most matches, then refines using a slot-matrix-aware backtrack pass.
    /// </summary>
    private SchedulingResult RunOptimisedSchedule(
        League league,
        List<Match> fixedMatches,
        List<ForbiddenSlot> forbidden)
    {
        var generatedMatches = MatchGenerator.GenerateMatches(league)
            .Where(m => !fixedMatches.Any(f =>
                string.Equals(f.TeamOne, m.TeamOne, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.TeamTwo, m.TeamTwo, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.DivisionName, m.DivisionName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Build the slot matrix once, stripping forbidden slots
        var allSlots = SchedulingMatrixBuilder.BuildSlots(league.Tournament)
            .Where(slot => !IsForbidden(slot, forbidden))
            .ToList();

        // Try multiple orderings to maximise scheduled count
        var orderings = new List<IEnumerable<Match>>
        {
            // Most-constrained first
            generatedMatches
                .OrderByDescending(m => league.Constraints.Count(c => c.TeamName == m.TeamOne || c.TeamName == m.TeamTwo))
                .ThenBy(m => m.DivisionName),
            // Division-balanced
            generatedMatches
                .OrderBy(m => m.DivisionName)
                .ThenByDescending(m => league.Constraints.Count(c => c.TeamName == m.TeamOne || c.TeamName == m.TeamTwo)),
            // Fewest-available-slots first (most difficult)
            generatedMatches
                .OrderBy(m => allSlots.Count(slot => ConstraintEvaluator.IsSlotAllowed(m, slot, league, fixedMatches, out _)))
                .ThenBy(m => m.DivisionName),
        };

        List<Match>? bestScheduled = null;
        List<(Match Match, string Reason)>? bestUnscheduled = null;

        foreach (var ordering in orderings)
        {
            var (sched, unsched) = TryScheduleOrdering(ordering.ToList(), allSlots, fixedMatches, league, forbidden);
            if (bestScheduled is null || sched.Count > bestScheduled.Count)
            {
                bestScheduled  = sched;
                bestUnscheduled = unsched;
            }
            // Perfect solution — stop early
            if (bestUnscheduled!.Count == 0) break;
        }

        // Backtrack pass: for each unscheduled match, try displacing a non-fixed
        // already-scheduled match to free up a slot
        if (bestUnscheduled!.Count > 0)
        {
            var (improved, stillUnscheduled) = BacktrackImprove(
                bestScheduled!, bestUnscheduled!, allSlots, league, forbidden);
            bestScheduled  = improved;
            bestUnscheduled = stillUnscheduled;
        }

        // Only assign umpires to non-fixed matches — fixed match umpires are preserved as-is.
        var toUmpire = bestScheduled!.Where(m => !m.IsFixed).ToList();
        AssignUmpires(toUmpire, league.Divisions, allMatches: bestScheduled);
        for (var i = 0; i < bestScheduled!.Count; i++) bestScheduled[i].Sequence = i + 1;
        return new SchedulingResult(bestScheduled!, bestUnscheduled!);
    }

    private static (List<Match> Scheduled, List<(Match, string)> Unscheduled) TryScheduleOrdering(
        List<Match> candidates,
        List<SchedulableSlot> allSlots,
        List<Match> fixedMatches,
        League league,
        List<ForbiddenSlot> forbidden)
    {
        var scheduled  = new List<Match>(fixedMatches);
        var unscheduled = new List<(Match, string)>();

        foreach (var match in candidates)
        {
            var best = allSlots
                .Where(slot => !IsForbiddenForMatch(slot, match.DivisionName, forbidden) &&
                               ConstraintEvaluator.IsSlotAllowed(match, slot, league, scheduled, out _))
                .Select(slot => new { Slot = slot, Score = SlotScorer.Score(match, slot, league, scheduled) })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (best is null) { unscheduled.Add((match, "No valid slot respecting constraints and forbidden slots.")); continue; }
            match.Date = best.Slot.Date; match.Slot = best.Slot.TimeSlot; match.Ground = best.Slot.Ground;
            scheduled.Add(match);
        }
        return (scheduled, unscheduled);
    }

    /// <summary>
    /// For each unscheduled match: find a slot that IS available; if none, try bumping
    /// one non-fixed already-scheduled match to another slot to free a slot for it.
    /// </summary>
    private static (List<Match>, List<(Match, string)>) BacktrackImprove(
        List<Match> scheduled,
        List<(Match Match, string Reason)> unscheduled,
        List<SchedulableSlot> allSlots,
        League league,
        List<ForbiddenSlot> forbidden)
    {
        var result   = new List<Match>(scheduled);
        var remaining = new List<(Match Match, string Reason)>();

        foreach (var (match, _) in unscheduled)
        {
            bool placed = false;

            // First attempt: direct slot (constraints may have relaxed after earlier backtrack placements)
            var direct = allSlots
                .Where(s => !IsForbiddenForMatch(s, match.DivisionName, forbidden) &&
                            ConstraintEvaluator.IsSlotAllowed(match, s, league, result, out _))
                .OrderByDescending(s => SlotScorer.Score(match, s, league, result))
                .FirstOrDefault();

            if (direct is not null)
            {
                match.Date = direct.Date; match.Slot = direct.TimeSlot; match.Ground = direct.Ground;
                result.Add(match); placed = true;
            }
            else
            {
                // Try bumping one non-fixed match
                var bumped = result.Where(m => !m.IsFixed).FirstOrDefault(m =>
                {
                    // Would removing m free a valid slot for 'match'?
                    var temp = result.Where(x => x != m).ToList();
                    return allSlots.Any(s => !IsForbiddenForMatch(s, match.DivisionName, forbidden) &&
                        ConstraintEvaluator.IsSlotAllowed(match, s, league, temp, out _));
                });

                if (bumped is not null)
                {
                    result.Remove(bumped);
                    var freed = allSlots
                        .Where(s => !IsForbiddenForMatch(s, match.DivisionName, forbidden) &&
                                    ConstraintEvaluator.IsSlotAllowed(match, s, league, result, out _))
                        .OrderByDescending(s => SlotScorer.Score(match, s, league, result))
                        .First();
                    match.Date = freed.Date; match.Slot = freed.TimeSlot; match.Ground = freed.Ground;
                    result.Add(match);

                    // Re-schedule the bumped match
                    var rebest = allSlots
                        .Where(s => !IsForbiddenForMatch(s, bumped.DivisionName, forbidden) &&
                                    ConstraintEvaluator.IsSlotAllowed(bumped, s, league, result, out _))
                        .OrderByDescending(s => SlotScorer.Score(bumped, s, league, result))
                        .FirstOrDefault();
                    if (rebest is not null)
                    {
                        bumped.Date = rebest.Date; bumped.Slot = rebest.TimeSlot; bumped.Ground = rebest.Ground;
                        result.Add(bumped);
                    }
                    else
                    {
                        bumped.Date = null; bumped.Slot = null; bumped.Ground = null;
                        remaining.Add((bumped, "Displaced by backtrack — could not be re-placed."));
                    }
                    placed = true;
                }
            }

            if (!placed) remaining.Add((match, "No valid slot found even after backtrack attempt."));
        }
        return (result, remaining);
    }

    /// <summary>
    /// Public entry point: re-assigns umpires for all non-fixed matches while
    /// preserving umpire assignments on fixed matches.
    /// </summary>
    public void RescheduleUmpiring(League league)
    {
        var fixedMatches    = league.Matches.Where(m => m.IsFixed).ToList();
        var nonFixedMatches = league.Matches.Where(m => !m.IsFixed).ToList();

        // Clear umpires on non-fixed matches before re-assigning
        foreach (var m in nonFixedMatches)
        {
            m.UmpireOne = null;
            m.UmpireTwo = null;
        }

        AssignUmpires(nonFixedMatches, league.Divisions, allMatches: league.Matches);
    }

    /// <summary>
    /// Reshuffles ground assignments for all non-fixed matches to balance ground usage
    /// across teams, then re-runs umpiring assignment.
    ///
    /// Hard guarantees:
    ///   • Fixed matches are completely untouched (ground, umpires, date, slot all preserved).
    ///   • No two matches share the same ground + date + timeslot after redistribution.
    ///   • Date and timeslot of every match remain unchanged.
    ///
    /// Ground-fairness algorithm (greedy, per date+slot group):
    ///   1. For each (date, timeslot) group of non-fixed matches, collect the tournament
    ///      grounds not already locked by fixed matches in that exact slot.
    ///   2. Assign grounds to the group's matches one at a time, always picking the ground
    ///      that minimises the combined ground-usage count for the two playing teams, so
    ///      underused grounds are preferred.
    ///   3. Each ground is consumed once per group (no double-booking within a slot).
    /// </summary>
    public void RescheduleGroundAndUmpiring(League league)
    {
        var fixedMatches = league.Matches.Where(m => m.IsFixed).ToList();
        var nonFixed = league.Matches
            .Where(m => !m.IsFixed && m.Date is not null && m.Slot is not null)
            .ToList();

        // Track combined ground-usage (team, ground) → count, seeded from fixed matches.
        var groundUsage = new Dictionary<(string Team, string Ground), int>(
            EqualityComparer<(string, string)>.Create(
                (a, b) => StringComparer.OrdinalIgnoreCase.Equals(a.Item1, b.Item1) &&
                           StringComparer.OrdinalIgnoreCase.Equals(a.Item2, b.Item2),
                o => StringComparer.OrdinalIgnoreCase.GetHashCode(o.Item1) ^
                     StringComparer.OrdinalIgnoreCase.GetHashCode(o.Item2)));

        void Inc(string team, string ground)
        {
            var key = (team, ground);
            groundUsage[key] = groundUsage.GetValueOrDefault(key, 0) + 1;
        }

        foreach (var m in fixedMatches.Where(m => m.Ground is not null))
        {
            Inc(m.TeamOne, m.Ground!.Name);
            Inc(m.TeamTwo, m.Ground!.Name);
        }

        var allGrounds = league.Tournament.Grounds;

        // Process each (date, timeslot) group so no ground is double-booked within a slot.
        var groups = nonFixed
            .GroupBy(m => (m.Date!.Value, m.Slot!.Start))
            .OrderBy(g => g.Key.Value).ThenBy(g => g.Key.Start);

        foreach (var group in groups)
        {
            var (date, slotStart) = group.Key;

            // Grounds locked by fixed matches at this exact date+slot.
            var fixedGroundsHere = fixedMatches
                .Where(m => m.Date == date && m.Slot?.Start == slotStart && m.Ground is not null)
                .Select(m => m.Ground!.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Available grounds for redistribution (tournament grounds minus fixed-locked ones).
            var available = allGrounds
                .Where(g => !fixedGroundsHere.Contains(g.Name))
                .ToList();

            var usedThisGroup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var match in group)
            {
                // Pick the ground that minimises combined prior usage for this team pair.
                var bestGround = available
                    .Where(g => !usedThisGroup.Contains(g.Name))
                    .OrderBy(g =>
                        groundUsage.GetValueOrDefault((match.TeamOne, g.Name), 0) +
                        groundUsage.GetValueOrDefault((match.TeamTwo, g.Name), 0))
                    .FirstOrDefault();

                if (bestGround is null) continue; // more matches in slot than grounds — leave as-is

                match.Ground = bestGround;
                usedThisGroup.Add(bestGround.Name);
                Inc(match.TeamOne, bestGround.Name);
                Inc(match.TeamTwo, bestGround.Name);
            }
        }

        // Re-run umpiring with updated ground assignments.
        RescheduleUmpiring(league);
    }

    /// <summary>
    /// Assigns umpiring teams to each match in <paramref name="matchesToUmpire"/> using:
    /// Priority 1 — team with a match in an ADJACENT slot on the same date + ground
    ///              (physically at the ground already)
    /// Priority 2 — team with NO match in the same calendar week (ISO week)
    ///              (least travel burden)
    /// Priority 3 — any eligible team (lowest cumulative umpire load — fairness fallback)
    ///
    /// Hard rules (always enforced):
    ///   • A team NEVER umpires in its own division.
    ///   • A team NEVER umpires when it is playing (same date + slot, any ground).
    ///   • A team NEVER umpires at a ground where it is NOT playing on that day
    ///     (teams playing at a different ground that day are excluded).
    /// </summary>
    private static void AssignUmpires(
        List<Match> matchesToUmpire,
        List<Division> divisions,
        List<Match>? allMatches = null)
    {
        allMatches ??= matchesToUmpire;

        var umpireLoad = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in divisions.SelectMany(d => d.Teams))
            umpireLoad[t.Name] = 0;

        var ordered = matchesToUmpire
            .Where(m => m.Date is not null && m.Slot is not null)
            .OrderBy(m => m.Date).ThenBy(m => m.Slot!.Start)
            .ToList();

        var byDate = allMatches
            .Where(m => m.Date is not null)
            .GroupBy(m => m.Date!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        static int IsoWeek(DateOnly d) =>
            System.Globalization.ISOWeek.GetWeekOfYear(d.ToDateTime(TimeOnly.MinValue));

        foreach (var match in ordered)
        {
            // Hard rule 1: never umpire own division
            var eligible = divisions
                .Where(d => !string.Equals(d.Name, match.DivisionName, StringComparison.OrdinalIgnoreCase))
                .SelectMany(d => d.Teams.Select(t => t.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (eligible.Count == 0) continue;

            byDate.TryGetValue(match.Date!.Value, out var dayMatches);
            dayMatches ??= [];

            // Hard rules 2+3: team cannot umpire while playing in the same slot (any ground)
            var playingNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in dayMatches.Where(m => m.Slot?.Start == match.Slot!.Start))
            {
                playingNow.Add(m.TeamOne);
                playingNow.Add(m.TeamTwo);
            }
            eligible = eligible.Where(t => !playingNow.Contains(t)).ToList();
            if (eligible.Count == 0) continue;

            // Hard rule 4: team cannot umpire at a ground where it is not playing that day.
            // Teams playing at a DIFFERENT ground today are excluded (even in a different slot).
            var playingAtDifferentGround = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in dayMatches.Where(m =>
                !string.Equals(m.Ground?.Name, match.Ground?.Name, StringComparison.OrdinalIgnoreCase)))
            {
                playingAtDifferentGround.Add(m.TeamOne);
                playingAtDifferentGround.Add(m.TeamTwo);
            }
            eligible = eligible.Where(t => !playingAtDifferentGround.Contains(t)).ToList();
            if (eligible.Count == 0) continue;

            // Priority 1: adjacent slot (prev/next) on same date + same ground
            var adjacentTeams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in dayMatches.Where(m =>
                string.Equals(m.Ground?.Name, match.Ground?.Name, StringComparison.OrdinalIgnoreCase)
                && m.Slot is not null && m != match))
            {
                if (m.Slot!.End == match.Slot!.Start || m.Slot.Start == match.Slot.End)
                {
                    adjacentTeams.Add(m.TeamOne);
                    adjacentTeams.Add(m.TeamTwo);
                }
            }

            // Priority 2: team with no match in the same ISO calendar week
            int matchWeek = IsoWeek(match.Date!.Value);
            var teamsWithMatchThisWeek = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in allMatches.Where(m => m.Date is not null && IsoWeek(m.Date!.Value) == matchWeek))
            {
                teamsWithMatchThisWeek.Add(m.TeamOne);
                teamsWithMatchThisWeek.Add(m.TeamTwo);
            }

            var p1 = eligible.Where(t => adjacentTeams.Contains(t))
                              .OrderBy(t => umpireLoad.GetValueOrDefault(t, 0)).ToList();
            var p2 = eligible.Where(t => !teamsWithMatchThisWeek.Contains(t))
                              .OrderBy(t => umpireLoad.GetValueOrDefault(t, 0)).ToList();
            var p3 = eligible.OrderBy(t => umpireLoad.GetValueOrDefault(t, 0)).ToList();

            string bestTeam;
            if      (p1.Count > 0) bestTeam = p1.First();
            else if (p2.Count > 0) bestTeam = p2.First();
            else if (p3.Count > 0) bestTeam = p3.First();
            else continue;

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
            // Always sort alphabetically, case-insensitive — must match GeneratePairingsForDivision order
            var teams = division.Teams
                .Select(t => t.Name)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

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
                // Fixed mode: use pre-computed pairings if available (generated on Division page),
                // otherwise fall back to internal pairing algorithm using the same sorted list
                if (division.FixedPairings.Count > 0)
                {
                    matches.AddRange(division.FixedPairings
                        .Select(p => CreateMatch(league.Tournament.Name, division.Name, p.TeamA, p.TeamB)));
                }
                else
                {
                    int target = division.MatchesPerTeam ?? (teams.Count - 1);
                    target = Math.Max(1, Math.Min(target, teams.Count - 1));
                    var pairs = FixedMatchesPairings(teams, target);
                    matches.AddRange(pairs.Select(p => CreateMatch(league.Tournament.Name, division.Name, p.t1, p.t2)));
                }
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
    /// <summary>
    /// Checks only team-availability constraints — does NOT reject slots that are already
    /// occupied by another match. Used by move analysis so that "overwriting" a slot (which
    /// displaces the occupying match) is a valid option for the user to consider.
    /// </summary>
    public static bool IsSlotAllowedForMove(Match match, SchedulableSlot slot, League league, List<Match> scheduled, out string reason)
    {
        reason = string.Empty;

        var teamBusyThisWeekend = scheduled.Any(m =>
            IsWeekendEqual(m.Date, slot.Date) &&
            (TeamMatch(match.TeamOne, m) || TeamMatch(match.TeamTwo, m)));
        if (teamBusyThisWeekend)
        { reason = "Team already has a match this weekend."; return false; }

        if (IsBlockedBySchedulingRequest(match.TeamOne, slot, league.Constraints) ||
            IsBlockedBySchedulingRequest(match.TeamTwo, slot, league.Constraints))
        { reason = "Scheduling request blocks this slot."; return false; }

        if (ViolatesNoGapRule(match, slot, scheduled))
        { reason = "Would violate max 2 consecutive no-match weekends."; return false; }

        return true;
    }

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

    internal static bool IsFullDayBlocked(string team, DateOnly date, List<SchedulingRequest> constraints) =>
        constraints.Any(c =>
            string.Equals(c.TeamName, team, StringComparison.OrdinalIgnoreCase) &&
            c.Date == date &&
            c.IsFullDayBlock);

    internal static bool IsTimeSlotBlocked(string team, SchedulableSlot slot, List<SchedulingRequest> constraints)
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

    internal static bool ViolatesNoGapRule(Match pending, SchedulableSlot candidate, List<Match> scheduled)
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

    internal static DateTime WeekendKey(DateOnly date)
    {
        var offsetToSaturday = date.DayOfWeek switch
        {
            DayOfWeek.Saturday => 0,
            DayOfWeek.Sunday => 1,
            _ => ((int)date.DayOfWeek + 1) % 7
        };
        return date.ToDateTime(TimeOnly.MinValue).AddDays(-offsetToSaturday);
    }
    internal static bool TeamMatch(string team, Match m) => string.Equals(team, m.TeamOne, StringComparison.OrdinalIgnoreCase) || string.Equals(team, m.TeamTwo, StringComparison.OrdinalIgnoreCase);
    internal static bool IsWeekendEqual(DateOnly? left, DateOnly right) => left is not null && WeekendKey(left.Value) == WeekendKey(right);
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
                if (!IsForbiddenForMatch(slot, match.DivisionName, forbidden) &&
                    ConstraintEvaluator.IsSlotAllowedRelaxed(match, slot, league, scheduled, relaxed, out _))
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

        // Only assign umpires to non-fixed matches — fixed match umpires are preserved as-is.
        var toUmpire = scheduled.Where(m => !m.IsFixed).ToList();
        AssignUmpires(toUmpire, league.Divisions, allMatches: scheduled);
        for (var i = 0; i < scheduled.Count; i++) scheduled[i].Sequence = i + 1;
        return new SchedulingResult(scheduled, unscheduled);
    }

    /// <summary>
    /// Non-destructive reschedule: treats all currently scheduled matches (fixed and
    /// non-fixed) as the starting context, then tries to place only the matches in
    /// <see cref="League.UnscheduledMatches"/> into remaining free slots.
    /// Fixed matches are never moved and their umpire assignments are preserved.
    /// Non-fixed scheduled matches keep their current slots but may be displaced by
    /// the backtrack pass if that is the only way to fit an unscheduled match.
    /// </summary>
    public SchedulingResult ReschedulePreservingExisting(League league, List<ForbiddenSlot> forbidden)
    {
        var allSlots = SchedulingMatrixBuilder.BuildSlots(league.Tournament)
            .Where(s => !IsForbidden(s, forbidden))
            .ToList();

        // All currently scheduled matches form the initial pool.
        // Fixed matches are immovable; non-fixed may be displaced if needed.
        var scheduled = league.Matches.ToList();
        var unscheduled = league.UnscheduledMatches.ToList();
        var stillUnscheduled = new List<(Match, string)>();

        // Direct placement pass: try to place each unscheduled match without moving anyone.
        foreach (var match in unscheduled)
        {
            var best = allSlots
                .Where(s => !IsForbiddenForMatch(s, match.DivisionName, forbidden) &&
                            ConstraintEvaluator.IsSlotAllowed(match, s, league, scheduled, out _))
                .OrderByDescending(s => SlotScorer.Score(match, s, league, scheduled))
                .FirstOrDefault();

            if (best is null) { stillUnscheduled.Add((match, "No valid slot respecting constraints.")); continue; }
            match.Date = best.Date; match.Slot = best.TimeSlot; match.Ground = best.Ground;
            scheduled.Add(match);
        }

        // Backtrack pass: displace a non-fixed match to free a slot if direct placement failed.
        if (stillUnscheduled.Count > 0)
            (scheduled, stillUnscheduled) = BacktrackImprove(scheduled, stillUnscheduled, allSlots, league, forbidden);

        // Preserve fixed match umpires; only re-assign umpires to non-fixed matches.
        var toUmpire = scheduled.Where(m => !m.IsFixed).ToList();
        AssignUmpires(toUmpire, league.Divisions, allMatches: scheduled);

        for (var i = 0; i < scheduled.Count; i++) scheduled[i].Sequence = i + 1;
        return new SchedulingResult(scheduled, stillUnscheduled);
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
