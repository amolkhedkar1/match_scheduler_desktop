using CricketScheduler.App.Models;

namespace CricketScheduler.App.Services;

/// <summary>
/// Modular scheduling pipeline — independent, loosely coupled routines callable on their own.
///
/// Phase 3 — ScheduleMatchesToSlots   : assign date + ground + timeslot via backtracking
/// Phase 5 — AssignGrounds            : rebalance ground assignments (date+time kept fixed)
///
/// Each routine is a public method so it can be exercised independently or composed into
/// larger workflows (Generate, Reschedule, RescheduleGroundAndUmpiring, etc.).
/// </summary>
public sealed partial class SchedulingService
{
    // ═══════════════════════════════════════════════════════════════════════════════════
    // PHASE 3 — Schedule Matches to Slots
    // Input  : match pairs (no date/ground/time) + filtered slot universe (SchedulableSlot)
    // Output : every match assigned a date + ground + timeslot; remainder → UnscheduledMatches
    // Rules  : hard scheduling rules + constraint.csv; ground fairness NOT enforced here
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Assigns a date, ground and timeslot to every match in <paramref name="matchesToSchedule"/>
    /// using three-ordering greedy search followed by a backtrack-improve pass.
    /// Fixed matches are passed as immovable context — they are included in the returned
    /// ScheduledMatches list but are never re-assigned.
    /// </summary>
    internal SchedulingResult ScheduleMatchesToSlots(
        List<Match>           matchesToSchedule,
        List<SchedulableSlot> slots,
        List<Match>           fixedMatches,
        League                league,
        List<ForbiddenSlot>   forbidden)
    {
        // Three candidate orderings — try each; keep the one that schedules the most matches.
        var orderings = new List<IEnumerable<Match>>
        {
            // Most-constrained team first
            matchesToSchedule
                .OrderByDescending(m => league.Constraints.Count(c =>
                    string.Equals(c.TeamName, m.TeamOne, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.TeamName, m.TeamTwo, StringComparison.OrdinalIgnoreCase)))
                .ThenBy(m => m.DivisionName),

            // Division-balanced
            matchesToSchedule
                .OrderBy(m => m.DivisionName)
                .ThenByDescending(m => league.Constraints.Count(c =>
                    string.Equals(c.TeamName, m.TeamOne, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.TeamName, m.TeamTwo, StringComparison.OrdinalIgnoreCase))),

            // Fewest-available-slots first (hardest match placed first)
            matchesToSchedule
                .OrderBy(m => slots.Count(s =>
                    !IsForbiddenForMatch(s, m.DivisionName, forbidden) &&
                    ConstraintEvaluator.IsSlotAllowed(m, s, league, fixedMatches, out _)))
                .ThenBy(m => m.DivisionName),
        };

        List<Match>?               bestScheduled   = null;
        List<(Match, string)>?     bestUnscheduled = null;
        var bestSnapshot = new Dictionary<Match, (DateOnly? Date, TimeSlot? Slot, Ground? Ground)>(
            ReferenceEqualityComparer.Instance);

        foreach (var ordering in orderings)
        {
            var (sched, unsched) = TrySlotOrdering(ordering.ToList(), slots, fixedMatches, league, forbidden);
            if (bestScheduled is null || sched.Count > bestScheduled.Count)
            {
                bestScheduled   = sched;
                bestUnscheduled = unsched;
                bestSnapshot.Clear();
                foreach (var m in sched.Where(x => !x.IsFixed))
                    bestSnapshot[m] = (m.Date, m.Slot, m.Ground);
            }
            if (bestUnscheduled!.Count == 0) break;
        }

        // Restore match fields to the best-ordering values (later orderings may have overwritten them).
        foreach (var (m, (date, slot, ground)) in bestSnapshot)
        { m.Date = date; m.Slot = slot; m.Ground = ground; }

        // Backtrack-improve pass: try to place remaining unscheduled matches by displacing
        // a non-fixed match temporarily.
        if (bestUnscheduled!.Count > 0)
        {
            var (improved, stillUnscheduled) =
                BacktrackImproveSlots(bestScheduled!, bestUnscheduled!, slots, league, forbidden);
            bestScheduled   = improved;
            bestUnscheduled = stillUnscheduled;
        }

        return new SchedulingResult(bestScheduled!, bestUnscheduled!);
    }

    // ── greedy pass for one ordering ─────────────────────────────────────────────────

    private static (List<Match> Scheduled, List<(Match, string)> Unscheduled) TrySlotOrdering(
        List<Match>           candidates,
        List<SchedulableSlot> slots,
        List<Match>           fixedMatches,
        League                league,
        List<ForbiddenSlot>   forbidden)
    {
        var scheduled   = new List<Match>(fixedMatches);
        var unscheduled = new List<(Match, string)>();

        foreach (var match in candidates)
        {
            var best = slots
                .Where(s => !IsForbiddenForMatch(s, match.DivisionName, forbidden) &&
                             ConstraintEvaluator.IsSlotAllowed(match, s, league, scheduled, out _))
                .Select(s => (Slot: s, Score: SlotScorer.Score(match, s, league, scheduled)))
                .OrderByDescending(x => x.Score)
                .Cast<(SchedulableSlot Slot, int Score)?>()
                .FirstOrDefault();

            if (best is null)
            {
                unscheduled.Add((match, "No valid slot respecting hard rules and constraints."));
                continue;
            }

            match.Date  = best.Value.Slot.Date;
            match.Slot  = best.Value.Slot.TimeSlot;
            match.Ground = best.Value.Slot.Ground;
            scheduled.Add(match);
        }

        return (scheduled, unscheduled);
    }

    // ── backtrack-improve pass ────────────────────────────────────────────────────────

    private static (List<Match>, List<(Match, string)>) BacktrackImproveSlots(
        List<Match>                       scheduled,
        List<(Match Match, string Reason)> unscheduled,
        List<SchedulableSlot>              allSlots,
        League                             league,
        List<ForbiddenSlot>                forbidden)
    {
        var result    = new List<Match>(scheduled);
        var remaining = new List<(Match Match, string Reason)>();

        foreach (var (match, _) in unscheduled)
        {
            bool placed = false;

            // Direct placement first (constraints may have relaxed after earlier placements).
            var direct = allSlots
                .Where(s => !IsForbiddenForMatch(s, match.DivisionName, forbidden) &&
                             ConstraintEvaluator.IsSlotAllowed(match, s, league, result, out _))
                .OrderByDescending(s => SlotScorer.Score(match, s, league, result))
                .FirstOrDefault();

            if (direct is not null)
            {
                match.Date  = direct.Date;
                match.Slot  = direct.TimeSlot;
                match.Ground = direct.Ground;
                result.Add(match);
                placed = true;
            }
            else
            {
                // Try displacing one non-fixed match to free a slot.
                var bumped = result.Where(m => !m.IsFixed).FirstOrDefault(m =>
                {
                    var temp = result.Where(x => x != m).ToList();
                    return allSlots.Any(s =>
                        !IsForbiddenForMatch(s, match.DivisionName, forbidden) &&
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

                    match.Date  = freed.Date;
                    match.Slot  = freed.TimeSlot;
                    match.Ground = freed.Ground;
                    result.Add(match);

                    var rebest = allSlots
                        .Where(s => !IsForbiddenForMatch(s, bumped.DivisionName, forbidden) &&
                                     ConstraintEvaluator.IsSlotAllowed(bumped, s, league, result, out _))
                        .OrderByDescending(s => SlotScorer.Score(bumped, s, league, result))
                        .FirstOrDefault();

                    if (rebest is not null)
                    {
                        bumped.Date  = rebest.Date;
                        bumped.Slot  = rebest.TimeSlot;
                        bumped.Ground = rebest.Ground;
                        result.Add(bumped);
                    }
                    else
                    {
                        bumped.Date  = null;
                        bumped.Slot  = null;
                        bumped.Ground = null;
                        remaining.Add((bumped, "Displaced by backtrack — could not be re-placed."));
                    }

                    placed = true;
                }
            }

            if (!placed)
                remaining.Add((match, "No valid slot found even after backtrack attempt."));
        }

        return (result, remaining);
    }


    // ═══════════════════════════════════════════════════════════════════════════════════
    // PHASE 5 — Ground Assignment
    // Input  : matches already have Date + Timeslot assigned (from Phase 3)
    // Output : every non-fixed match gets a Ground; date and timeslot are NOT changed
    // Rules  : no two matches share the same (date, timeslot, ground);
    //          ground-specific forbidden slots respected; fairness scoring applied
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Assigns a ground to every non-fixed scheduled match.
    /// Date and timeslot assigned in Phase 3 are preserved.
    /// Uses recursive backtracking ordered by most-constrained match first.
    /// Fixed match grounds are never touched.
    /// </summary>
    public void AssignGrounds(League league, List<ForbiddenSlot>? forbidden = null)
    {
        forbidden ??= [];

        var fixedMatches = league.Matches.Where(m => m.IsFixed).ToList();
        var nonFixed     = league.Matches
            .Where(m => !m.IsFixed && m.Date is not null && m.Slot is not null)
            .ToList();

        var state = new GroundAssignmentState(league.Tournament.Grounds, forbidden);

        // Seed occupied slots and usage counts from fixed matches.
        foreach (var m in fixedMatches.Where(m => m.Ground is not null))
        {
            state.SetOccupied(m.Date!.Value, m.Slot!.Start, m.Ground!.Name);
            state.Inc(m.TeamOne, m.Ground.Name);
            state.Inc(m.TeamTwo, m.Ground.Name);
        }

        // Clear existing ground assignments on non-fixed matches (full rebalance).
        foreach (var m in nonFixed) m.Ground = null;

        // Sort: most-constrained first (fewest valid ground candidates).
        var sorted = nonFixed
            .OrderBy(m => state.CandidateGrounds(m, m.DivisionName).Count)
            .ThenByDescending(m => league.Constraints.Count(c =>
                string.Equals(c.TeamName, m.TeamOne, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.TeamName, m.TeamTwo, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        BacktrackAssignGrounds(sorted, 0, state);
    }

    private static bool BacktrackAssignGrounds(
        List<Match>           matches,
        int                   index,
        GroundAssignmentState state)
    {
        if (index == matches.Count) return true;

        var match      = matches[index];
        var candidates = state.CandidateGrounds(match, match.DivisionName);

        if (candidates.Count == 0)
        {
            // No ground available for this match — leave Ground = null and continue.
            // (Can occur if every ground at this slot is taken by fixed matches or forbidden.)
            return BacktrackAssignGrounds(matches, index + 1, state);
        }

        // Order by lowest combined ground-usage for the two playing teams (fairness).
        var ordered = candidates
            .OrderBy(g => state.GetUsage(match.TeamOne, g.Name) + state.GetUsage(match.TeamTwo, g.Name))
            .ToList();

        foreach (var ground in ordered)
        {
            // Assign
            match.Ground = ground;
            state.SetOccupied(match.Date!.Value, match.Slot!.Start, ground.Name);
            state.Inc(match.TeamOne, ground.Name);
            state.Inc(match.TeamTwo, ground.Name);

            if (BacktrackAssignGrounds(matches, index + 1, state))
                return true;

            // Rollback
            match.Ground = null;
            state.ClearOccupied(match.Date!.Value, match.Slot!.Start, ground.Name);
            state.Dec(match.TeamOne, ground.Name);
            state.Dec(match.TeamTwo, ground.Name);
        }

        // All candidates tried and failed — skip this match (leave Ground = null) and
        // continue rather than blocking the rest of the assignments.
        return BacktrackAssignGrounds(matches, index + 1, state);
    }


    // ═══════════════════════════════════════════════════════════════════════════════════
    // Ground assignment state — encapsulates occupied-slot tracking and usage counts
    // ═══════════════════════════════════════════════════════════════════════════════════

    private sealed class GroundAssignmentState
    {
        private readonly List<Ground>       _grounds;
        private readonly List<ForbiddenSlot> _forbidden;
        private readonly HashSet<string>    _occupied  = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string Team, string Ground), int> _usage = new(
            EqualityComparer<(string, string)>.Create(
                (a, b) => StringComparer.OrdinalIgnoreCase.Equals(a.Item1, b.Item1) &&
                           StringComparer.OrdinalIgnoreCase.Equals(a.Item2, b.Item2),
                o => StringComparer.OrdinalIgnoreCase.GetHashCode(o.Item1) ^
                     StringComparer.OrdinalIgnoreCase.GetHashCode(o.Item2)));

        public GroundAssignmentState(List<Ground> grounds, List<ForbiddenSlot> forbidden)
        {
            _grounds  = grounds;
            _forbidden = forbidden;
        }

        private static string Key(DateOnly d, TimeOnly t, string g) => $"{d}|{t:HH\\:mm}|{g}";

        public void SetOccupied(DateOnly d, TimeOnly t, string g)   => _occupied.Add(Key(d, t, g));
        public void ClearOccupied(DateOnly d, TimeOnly t, string g) => _occupied.Remove(Key(d, t, g));
        public bool IsOccupied(DateOnly d, TimeOnly t, string g)    => _occupied.Contains(Key(d, t, g));

        public int  GetUsage(string team, string ground) => _usage.GetValueOrDefault((team, ground), 0);
        public void Inc(string team, string ground) { var k = (team, ground); _usage[k] = _usage.GetValueOrDefault(k, 0) + 1; }
        public void Dec(string team, string ground) { var k = (team, ground); _usage[k] = Math.Max(0, _usage.GetValueOrDefault(k, 0) - 1); }

        /// <summary>Returns grounds valid for <paramref name="match"/> at its already-assigned (date, timeslot).</summary>
        public List<Ground> CandidateGrounds(Match match, string divisionName)
        {
            if (match.Date is null || match.Slot is null) return [];

            return _grounds.Where(g =>
            {
                // Not already occupied at this (date, timeslot)
                if (IsOccupied(match.Date.Value, match.Slot.Start, g.Name)) return false;

                // Not blocked by a ground-specific forbidden slot
                foreach (var f in _forbidden)
                {
                    if (f.GroundName is null) continue;
                    if (!string.Equals(f.GroundName, g.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    bool divOk  = f.Division  is null || string.Equals(f.Division,  divisionName,        StringComparison.OrdinalIgnoreCase);
                    bool dateOk = f.Date       is null || f.Date       == match.Date;
                    bool slotOk = f.TimeSlot  is null || f.TimeSlot.Start == match.Slot.Start;
                    if (divOk && dateOk && slotOk) return false;
                }
                return true;
            }).ToList();
        }
    }
}
