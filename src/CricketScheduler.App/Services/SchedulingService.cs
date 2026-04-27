using CricketScheduler.App.Models;
using CricketScheduler.App.ViewModels;

namespace CricketScheduler.App.Services;

public sealed partial class SchedulingService
{
    /// <summary>
    /// Full scheduling pipeline:
    ///   Phase 1  — generate match pairs from division config
    ///   Phase 2  — build filtered slot universe: (date × ground × timeslot)
    ///              N grounds → N identifiable slots per timeslot per date
    ///   Phase 3  — assign date + ground + timeslot (backtracking, minimise unscheduled)
    ///   Phase 5  — rebalance ground assignments for fairness (date+time kept fixed)
    ///   Phase 6  — assign umpires
    /// </summary>
    public SchedulingResult Generate(League league, List<Match> fixedMatches, List<ForbiddenSlot> forbidden)
    {
        // ── Phase 1: match pair generation ────────────────────────────────────────────────
        var matchesToSchedule = MatchGenerator.GenerateMatches(league)
            .Where(m => !fixedMatches.Any(f =>
                string.Equals(f.TeamOne,      m.TeamOne,      StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.TeamTwo,      m.TeamTwo,      StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.DivisionName, m.DivisionName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // ── Phase 2: build slot universe (ground-aware) ───────────────────────────────────
        // N grounds × M timeslots × D dates = N×M×D distinct identifiable slots.
        // Globally forbidden slots (no division, no ground qualifier) are pre-filtered here;
        // division- and ground-specific forbidden slots are enforced per match in Phase 3.
        var slots = SchedulingMatrixBuilder.BuildSlots(league.Tournament)
            .Where(s => !IsForbidden(s, forbidden))
            .ToList();

        // ── Phase 3: assign date + ground + timeslot via backtracking ─────────────────────
        var result = ScheduleMatchesToSlots(matchesToSchedule, slots, fixedMatches, league, forbidden);
        league.Matches            = result.ScheduledMatches.ToList();
        league.UnscheduledMatches = result.UnscheduledMatches.Select(x => x.Match).ToList();

        // ── Phase 5: rebalance ground assignments for fairness ────────────────────────────
        // Date and timeslot are kept fixed; only the ground assignment is changed.
        AssignGrounds(league, forbidden);

        // ── Phase 6: assign umpires (non-fixed only) ──────────────────────────────────────
        var toUmpire = league.Matches.Where(m => !m.IsFixed).ToList();
        AssignUmpires(toUmpire, league.Divisions, allMatches: league.Matches);

        for (var i = 0; i < league.Matches.Count; i++) league.Matches[i].Sequence = i + 1;
        return new SchedulingResult(league.Matches, league.UnscheduledMatches
            .Select(m => (m, m.UnscheduledReason ?? "Unscheduled"))
            .ToList());
    }


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
            bool slotMatch   = f.TimeSlot  is null || f.TimeSlot.Start == slot.TimeSlot.Start;
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
            bool slotMatch   = f.TimeSlot  is null || f.TimeSlot.Start == slot.TimeSlot.Start;
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

            // Hard filter: never suggest a slot where a FIXED match already occupies this team's weekend
            // (fixed matches cannot be moved, so it would be an unresolvable constraint violation)
            if (fixedMatches.Any(m =>
                ConstraintEvaluator.IsWeekendEqual(m.Date, slot.Date) &&
                (ConstraintEvaluator.TeamMatch(match.TeamOne, m) ||
                 ConstraintEvaluator.TeamMatch(match.TeamTwo, m))))
                continue;

            // Strict mode (additionalFixed provided = displaced-match context): also filter
            // non-fixed same-weekend conflicts so only constraint-clean slots are suggested
            if (additionalFixed is not null && sameWeekendConflicts.Count > 0) continue;

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

        // Snapshot: each ordering mutates the same Match objects in-place.
        // We capture Date/Slot/Ground when a better result is found so we can
        // restore them after later (worse) orderings overwrite those fields.
        var bestSnapshot = new Dictionary<Match, (DateOnly? Date, TimeSlot? Slot, Ground? Ground)>(
            ReferenceEqualityComparer.Instance);

        foreach (var ordering in orderings)
        {
            var (sched, unsched) = TryScheduleOrdering(ordering.ToList(), allSlots, fixedMatches, league, forbidden);
            if (bestScheduled is null || sched.Count > bestScheduled.Count)
            {
                bestScheduled   = sched;
                bestUnscheduled = unsched;
                bestSnapshot.Clear();
                foreach (var m in sched)
                    bestSnapshot[m] = (m.Date, m.Slot, m.Ground);
            }
            // Perfect solution — stop early
            if (bestUnscheduled!.Count == 0) break;
        }

        // Restore match fields to the best-ordering values; subsequent orderings may have
        // overwritten them on the shared Match objects.
        foreach (var (m, (date, slot, ground)) in bestSnapshot)
        { m.Date = date; m.Slot = slot; m.Ground = ground; }

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
    /// Public entry point: re-assigns umpires for non-fixed matches while preserving fixed match umpires.
    /// When <paramref name="targetMatches"/> is supplied, only those non-fixed matches are cleared
    /// and reassigned; all other matches (including non-fixed ones) keep their current assignments
    /// and are pre-seeded into the tracking state so their load counts correctly.
    /// </summary>
    public void RescheduleUmpiring(League league, IReadOnlyCollection<Match>? targetMatches = null)
    {
        var toUmpire = targetMatches is null
            ? league.Matches.Where(m => !m.IsFixed).ToList()
            : targetMatches.Where(m => !m.IsFixed).ToList();

        // Clear umpires on the target matches before re-assigning
        foreach (var m in toUmpire)
        {
            m.UmpireOne = null;
            m.UmpireTwo = null;
        }

        AssignUmpires(toUmpire, league.Divisions, allMatches: league.Matches);
    }

    /// <summary>
    /// Reshuffles date/slot/ground assignments for all non-fixed matches to balance ground
    /// usage across teams, then re-runs umpiring assignment.
    ///
    /// Hard guarantees:
    ///   • Fixed matches are completely untouched.
    ///   • No two matches share the same ground + date + timeslot after redistribution.
    ///   • Team availability constraints (date blocks, time-slot restrictions) are respected.
    ///   • 1-match-per-team-per-weekend rule is preserved.
    ///
    /// Ground-fairness algorithm (tree-like, most-constrained first):
    ///   1. Process non-fixed matches in order of fewest valid candidate slots (most constrained
    ///      first) so unconstrained matches fill the gaps left by constrained ones.
    ///   2. For each match: temporarily remove it from the assignment pool, then find the
    ///      (date, timeslot, ground) combination in the same weekend with the lowest combined
    ///      ground-usage count for its two teams. A small day-change penalty (5) discourages
    ///      moving across Sat/Sun unless it gives a meaningfully better ground balance.
    ///   3. Re-add the match at its best slot and repeat for the next match.
    /// </summary>
    public void RescheduleGroundAndUmpiring(League league, List<ForbiddenSlot>? forbidden = null,
        IReadOnlyCollection<Match>? targetMatches = null)
    {
        // Phase 5 (full rebalance) + Phase 6
        AssignGrounds(league, forbidden, targetMatches);
        RescheduleUmpiring(league, targetMatches);
    }

    // ── Legacy ground+umpiring implementation kept for reference ─────────────────────────
    [Obsolete("Replaced by AssignGrounds + RescheduleUmpiring pipeline. Kept for reference only.")]
    private void LegacyRescheduleGroundAndUmpiring(League league, List<ForbiddenSlot>? forbidden = null)
    {
        forbidden ??= [];
        var allTournamentSlots = SchedulingMatrixBuilder.BuildSlots(league.Tournament).ToList();
        var fixedMatches = league.Matches.Where(m => m.IsFixed).ToList();
        var nonFixed = league.Matches
            .Where(m => !m.IsFixed && m.Date is not null && m.Slot is not null)
            .ToList();

        // occupied: "date|HH:mm|groundname" → Match — only fixed slots are pre-locked
        static string SlotKey(DateOnly d, TimeOnly t, string g) => $"{d}|{t:HH\\:mm}|{g}";
        var occupied = new Dictionary<string, Match>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in fixedMatches.Where(m => m.Ground is not null))
            occupied[SlotKey(m.Date!.Value, m.Slot!.Start, m.Ground!.Name)] = m;

        // groundUsage: (team, ground) → play count, seeded from fixed matches
        var groundUsage = new Dictionary<(string, string), int>(
            EqualityComparer<(string, string)>.Create(
                (a, b) => StringComparer.OrdinalIgnoreCase.Equals(a.Item1, b.Item1) &&
                           StringComparer.OrdinalIgnoreCase.Equals(a.Item2, b.Item2),
                o => StringComparer.OrdinalIgnoreCase.GetHashCode(o.Item1) ^
                     StringComparer.OrdinalIgnoreCase.GetHashCode(o.Item2)));

        int  GetUsage(string t, string g) => groundUsage.GetValueOrDefault((t, g), 0);
        void IncUsage(string t, string g) { var k = (t, g); groundUsage[k] = groundUsage.GetValueOrDefault(k, 0) + 1; }
        void DecUsage(string t, string g) { var k = (t, g); groundUsage[k] = Math.Max(0, groundUsage.GetValueOrDefault(k, 0) - 1); }

        foreach (var m in fixedMatches.Where(m => m.Ground is not null))
        { IncUsage(m.TeamOne, m.Ground!.Name); IncUsage(m.TeamTwo, m.Ground!.Name); }

        // Count valid candidate slots per match to determine processing order
        int CandidateCount(Match m)
        {
            var wk = ConstraintEvaluator.WeekendKey(m.Date!.Value);
            return allTournamentSlots.Count(s =>
                ConstraintEvaluator.WeekendKey(s.Date) == wk &&
                !IsForbiddenForMatch(s, m.DivisionName, forbidden) &&
                !ConstraintEvaluator.IsFullDayBlocked(m.TeamOne,  s.Date, league.Constraints) &&
                !ConstraintEvaluator.IsFullDayBlocked(m.TeamTwo,  s.Date, league.Constraints) &&
                !ConstraintEvaluator.IsTimeSlotBlocked(m.TeamOne, s, league.Constraints) &&
                !ConstraintEvaluator.IsTimeSlotBlocked(m.TeamTwo, s, league.Constraints));
        }

        foreach (var match in nonFixed.OrderBy(CandidateCount))
        {
            var origKey = match.Ground is not null
                ? SlotKey(match.Date!.Value, match.Slot!.Start, match.Ground.Name) : null;
            if (origKey is not null)
            {
                occupied.Remove(origKey);
                DecUsage(match.TeamOne, match.Ground!.Name);
                DecUsage(match.TeamTwo, match.Ground!.Name);
            }

            var weekendKey = ConstraintEvaluator.WeekendKey(match.Date!.Value);
            (SchedulableSlot Slot, double Score)? best = null;

            foreach (var slot in allTournamentSlots)
            {
                if (ConstraintEvaluator.WeekendKey(slot.Date) != weekendKey) continue;
                if (occupied.ContainsKey(SlotKey(slot.Date, slot.TimeSlot.Start, slot.Ground.Name))) continue;
                if (IsForbiddenForMatch(slot, match.DivisionName, forbidden)) continue;
                if (ConstraintEvaluator.IsFullDayBlocked(match.TeamOne,  slot.Date, league.Constraints)) continue;
                if (ConstraintEvaluator.IsFullDayBlocked(match.TeamTwo,  slot.Date, league.Constraints)) continue;
                if (ConstraintEvaluator.IsTimeSlotBlocked(match.TeamOne, slot, league.Constraints)) continue;
                if (ConstraintEvaluator.IsTimeSlotBlocked(match.TeamTwo, slot, league.Constraints)) continue;

                // 1-per-weekend: no committed match has the same team on the same weekend
                if (occupied.Values.Any(om =>
                    ConstraintEvaluator.IsWeekendEqual(om.Date, slot.Date) &&
                    (ConstraintEvaluator.TeamMatch(match.TeamOne, om) ||
                     ConstraintEvaluator.TeamMatch(match.TeamTwo, om)))) continue;

                double score = GetUsage(match.TeamOne, slot.Ground.Name)
                             + GetUsage(match.TeamTwo, slot.Ground.Name)
                             + (slot.Date != match.Date!.Value ? 5.0 : 0.0);

                if (best is null || score < best.Value.Score)
                    best = (slot, score);
            }

            if (best is null)
            {
                if (origKey is not null)
                {
                    occupied[origKey] = match;
                    IncUsage(match.TeamOne, match.Ground!.Name);
                    IncUsage(match.TeamTwo, match.Ground!.Name);
                }
                continue;
            }

            match.Date  = best.Value.Slot.Date;
            match.Slot  = best.Value.Slot.TimeSlot;
            match.Ground = best.Value.Slot.Ground;
            var newKey = SlotKey(match.Date!.Value, match.Slot!.Start, match.Ground!.Name);
            occupied[newKey] = match;
            IncUsage(match.TeamOne, match.Ground.Name);
            IncUsage(match.TeamTwo, match.Ground.Name);
        }

        RescheduleUmpiring(league);
    }

    /// <summary>
    /// Assigns umpiring teams to each match in <paramref name="matchesToUmpire"/> using:
    /// Priority 1 — team with a match in an ADJACENT slot on the same date + ground
    ///              (physically at the ground already)
    /// Priority 2 — team with NO match on the same WEEKEND
    ///              (least travel burden; weekend = Sat+Sun pair)
    /// Priority 3 — any eligible team (lowest cumulative umpire load — fairness fallback)
    ///
    /// Hard rules (always enforced):
    ///   • A team NEVER umpires in its own division.
    ///   • A team NEVER umpires when it is playing (same date + slot, any ground).
    ///   • A team NEVER umpires at a ground where it is NOT playing on that day.
    ///   • A team NEVER exceeds ceil(matchesPlaying / 2) total umpiring assignments.
    ///   • A team NEVER umpires more than once per weekend.
    ///
    /// Soft rule:
    ///   • Deprioritize teams that umpired within the last 7 days (prefer 1-week gap).
    ///
    /// Fixed-match umpire assignments are pre-seeded into tracking so they count
    /// toward the per-team load cap and per-weekend limits.
    /// </summary>
    private static void AssignUmpires(
        List<Match> matchesToUmpire,
        List<Division> divisions,
        List<Match>? allMatches = null)
    {
        allMatches ??= matchesToUmpire;

        // Compute how many matches each team is playing (for the 50%-cap hard rule)
        var matchesPlaying = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in divisions.SelectMany(d => d.Teams))
            matchesPlaying[t.Name] = 0;
        foreach (var m in allMatches)
        {
            matchesPlaying[m.TeamOne] = matchesPlaying.GetValueOrDefault(m.TeamOne, 0) + 1;
            matchesPlaying[m.TeamTwo] = matchesPlaying.GetValueOrDefault(m.TeamTwo, 0) + 1;
        }

        // HARD: max umpire load = ceil(matchesPlaying / 2)
        var maxUmpireLoad = matchesPlaying.ToDictionary(
            kv => kv.Key,
            kv => (int)Math.Ceiling(kv.Value / 2.0),
            StringComparer.OrdinalIgnoreCase);

        var umpireLoad     = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var umpireWeekends = new Dictionary<string, HashSet<DateTime>>(StringComparer.OrdinalIgnoreCase);
        var lastUmpireDate = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in divisions.SelectMany(d => d.Teams))
        {
            umpireLoad[t.Name]     = 0;
            umpireWeekends[t.Name] = [];
        }

        // Pre-seed from all matches NOT being (re)assigned that already have umpire assignments.
        // In partial mode this includes fixed matches AND non-target non-fixed matches so their
        // existing umpire load counts toward caps and weekend limits.
        var targetSet = new HashSet<Match>(matchesToUmpire, ReferenceEqualityComparer.Instance);
        foreach (var m in allMatches.Where(m => !targetSet.Contains(m) && m.Date is not null && m.UmpireOne is not null)
                                     .OrderBy(m => m.Date))
        {
            var u = m.UmpireOne!;
            if (!umpireLoad.ContainsKey(u)) continue;
            umpireLoad[u]++;
            umpireWeekends[u].Add(ConstraintEvaluator.WeekendKey(m.Date!.Value));
            lastUmpireDate[u] = m.Date!.Value;
        }

        var ordered = matchesToUmpire
            .Where(m => m.Date is not null && m.Slot is not null)
            .OrderBy(m => m.Date).ThenBy(m => m.Slot!.Start)
            .ToList();

        var byDate = allMatches
            .Where(m => m.Date is not null)
            .GroupBy(m => m.Date!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

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

            // Hard rule 2: cannot umpire while playing in same slot (any ground)
            var playingNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in dayMatches.Where(m => m.Slot?.Start == match.Slot!.Start))
            { playingNow.Add(m.TeamOne); playingNow.Add(m.TeamTwo); }
            eligible = eligible.Where(t => !playingNow.Contains(t)).ToList();
            if (eligible.Count == 0) continue;

            // Hard rule 3: cannot umpire at a ground where NOT playing that day
            var playingAtDifferentGround = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in dayMatches.Where(m =>
                !string.Equals(m.Ground?.Name, match.Ground?.Name, StringComparison.OrdinalIgnoreCase)))
            { playingAtDifferentGround.Add(m.TeamOne); playingAtDifferentGround.Add(m.TeamTwo); }
            eligible = eligible.Where(t => !playingAtDifferentGround.Contains(t)).ToList();
            if (eligible.Count == 0) continue;

            // Hard rule 4 (new): cannot exceed ceil(matchesPlaying/2) total umpiring load
            eligible = eligible.Where(t =>
                umpireLoad.GetValueOrDefault(t, 0) < maxUmpireLoad.GetValueOrDefault(t, int.MaxValue)).ToList();
            if (eligible.Count == 0) continue;

            // Hard rule 5 (new): at most 1 umpiring assignment per weekend
            var matchWeekend = ConstraintEvaluator.WeekendKey(match.Date!.Value);
            eligible = eligible.Where(t =>
                !umpireWeekends.TryGetValue(t, out var wu) || !wu.Contains(matchWeekend)).ToList();
            if (eligible.Count == 0) continue;

            // Priority 1: adjacent slot (prev/next) on same date + same ground
            var adjacentTeams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in dayMatches.Where(m =>
                string.Equals(m.Ground?.Name, match.Ground?.Name, StringComparison.OrdinalIgnoreCase)
                && m.Slot is not null && m != match))
            {
                if (m.Slot!.End == match.Slot!.Start || m.Slot.Start == match.Slot.End)
                { adjacentTeams.Add(m.TeamOne); adjacentTeams.Add(m.TeamTwo); }
            }

            // Priority 2: teams NOT playing this weekend (Sat+Sun pair, not ISO calendar week)
            var teamsPlayingThisWeekend = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in allMatches.Where(m =>
                m.Date is not null && ConstraintEvaluator.IsWeekendEqual(m.Date, match.Date!.Value)))
            { teamsPlayingThisWeekend.Add(m.TeamOne); teamsPlayingThisWeekend.Add(m.TeamTwo); }

            // Soft: deprioritize teams that umpired within the last 7 days
            bool RecentlyUmpired(string t) =>
                lastUmpireDate.TryGetValue(t, out var last) &&
                (match.Date!.Value.ToDateTime(TimeOnly.MinValue) - last.ToDateTime(TimeOnly.MinValue)).TotalDays < 7;

            var p1 = eligible.Where(t => adjacentTeams.Contains(t))
                              .OrderBy(t => RecentlyUmpired(t) ? 1 : 0)
                              .ThenBy(t => umpireLoad.GetValueOrDefault(t, 0)).ToList();
            var p2 = eligible.Where(t => !teamsPlayingThisWeekend.Contains(t))
                              .OrderBy(t => RecentlyUmpired(t) ? 1 : 0)
                              .ThenBy(t => umpireLoad.GetValueOrDefault(t, 0)).ToList();
            var p3 = eligible.OrderBy(t => RecentlyUmpired(t) ? 1 : 0)
                              .ThenBy(t => umpireLoad.GetValueOrDefault(t, 0)).ToList();

            string bestTeam;
            if      (p1.Count > 0) bestTeam = p1.First();
            else if (p2.Count > 0) bestTeam = p2.First();
            else if (p3.Count > 0) bestTeam = p3.First();
            else continue;

            match.UmpireOne = bestTeam;
            match.UmpireTwo = bestTeam;
            umpireLoad[bestTeam] = umpireLoad.GetValueOrDefault(bestTeam, 0) + 1;
            if (!umpireWeekends.ContainsKey(bestTeam)) umpireWeekends[bestTeam] = [];
            umpireWeekends[bestTeam].Add(matchWeekend);
            lastUmpireDate[bestTeam] = match.Date!.Value;
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

/// <summary>Date + time-slot pair used in Phase 3 (date/time assignment). Ground is not yet known.</summary>
internal sealed record DateTimeSlot(DateOnly Date, TimeSlot TimeSlot);

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

    /// <summary>
    /// Builds the universe of (date, timeslot) pairs for Phase 3 scheduling.
    /// Ground is not included — each entry represents a block of time, not a specific pitch.
    /// Discarded dates are already excluded; forbidden-slot filtering is done by the caller.
    /// </summary>
    public static List<DateTimeSlot> BuildDateTimeSlots(Tournament tournament)
    {
        var seen   = new HashSet<(DateOnly, TimeOnly)>();
        var result = new List<DateTimeSlot>();
        for (var date = tournament.StartDate; date <= tournament.EndDate; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday) continue;
            if (tournament.DiscardedDates.Contains(date)) continue;
            foreach (var slot in tournament.TimeSlots)
                if (seen.Add((date, slot.Start)))
                    result.Add(new DateTimeSlot(date, slot));
        }
        return result;
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
            string.Equals(m.Ground?.Name, slot.Ground.Name, StringComparison.OrdinalIgnoreCase) &&
            m.Slot?.Start == slot.TimeSlot.Start);
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
                .GroupBy(m => $"{m.Slot!.Start:HH\\:mm}")
                .ToDictionary(g => g.Key, g => g.Count());
            var thisSlotKey   = $"{slot.TimeSlot.Start:HH\\:mm}";
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

    // Canonical overload — SchedulableSlot delegates to DateOnly variant.
    internal static bool ViolatesNoGapRule(Match pending, SchedulableSlot candidate, List<Match> scheduled)
        => ViolatesNoGapRule(pending, candidate.Date, scheduled);

    /// <summary>
    /// Returns true if adding a match on <paramref name="candidateDate"/> would create a gap of
    /// more than 2 consecutive no-match weekends for either team.
    /// </summary>
    internal static bool ViolatesNoGapRule(Match pending, DateOnly candidateDate, List<Match> scheduled)
    {
        var teams = new[] { pending.TeamOne, pending.TeamTwo };
        foreach (var team in teams)
        {
            var existingWeekends = scheduled
                .Where(m => TeamMatch(team, m) && m.Date is not null)
                .Select(m => WeekendKey(m.Date!.Value))
                .Distinct().OrderBy(d => d).ToList();

            var withCandidate = existingWeekends
                .Append(WeekendKey(candidateDate))
                .Distinct().OrderBy(d => d).ToList();

            if (withCandidate.Count < 2) continue;

            var maxGap = 0;
            for (var i = 1; i < withCandidate.Count; i++)
            {
                var diff = (int)((withCandidate[i] - withCandidate[i - 1]).TotalDays / 7) - 1;
                maxGap = Math.Max(maxGap, diff);
            }
            if (maxGap > 2) return true;
        }
        return false;
    }

    /// <summary>
    /// Checks whether a (date, timeslot) slot is valid for scheduling a match.
    /// Used in Phase 3 where grounds are not yet assigned.
    /// <paramref name="groundCapacity"/> = number of grounds available (slot capacity limit).
    /// </summary>
    public static bool IsDateTimeSlotAllowed(
        Match match, DateTimeSlot slot, League league,
        List<Match> scheduled, int groundCapacity, out string reason)
    {
        reason = string.Empty;

        // Capacity: at most groundCapacity matches may share this (date, timeslot)
        int used = scheduled.Count(m => m.Date == slot.Date && m.Slot?.Start == slot.TimeSlot.Start);
        if (used >= groundCapacity)
        { reason = $"Slot at capacity ({groundCapacity} grounds)."; return false; }

        // 1 match per team per weekend
        if (scheduled.Any(m => IsWeekendEqual(m.Date, slot.Date) &&
                                (TeamMatch(match.TeamOne, m) || TeamMatch(match.TeamTwo, m))))
        { reason = "Team already has a match this weekend."; return false; }

        // Full-day availability block
        if (IsFullDayBlocked(match.TeamOne, slot.Date, league.Constraints) ||
            IsFullDayBlocked(match.TeamTwo, slot.Date, league.Constraints))
        { reason = "Full-day availability block."; return false; }

        // Partial-time availability block
        if (IsTimeSlotBlockedDatetime(match.TeamOne, slot, league.Constraints) ||
            IsTimeSlotBlockedDatetime(match.TeamTwo, slot, league.Constraints))
        { reason = "Time-slot availability block."; return false; }

        // Max-gap rule (≤2 consecutive no-match weekends)
        if (ViolatesNoGapRule(match, slot.Date, scheduled))
        { reason = "Would violate max 2 consecutive no-match weekends."; return false; }

        return true;
    }

    private static bool IsTimeSlotBlockedDatetime(string team, DateTimeSlot slot, List<SchedulingRequest> constraints) =>
        constraints.Any(c =>
            string.Equals(c.TeamName, team, StringComparison.OrdinalIgnoreCase) &&
            c.Date == slot.Date && !c.IsFullDayBlock &&
            c.StartTime is not null && c.EndTime is not null &&
            c.StartTime < slot.TimeSlot.End && slot.TimeSlot.Start < c.EndTime);

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
        var timeUse = scheduled.Count(m => m.Slot?.Start == slot.TimeSlot.Start);
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

// ── Schedule / Umpiring verification results ──────────────────────────────────

public sealed class ScheduleVerificationResult
{
    public bool IsValid { get; init; }
    public List<(Match Match, string Reason)> Violations { get; init; } = [];
}

public sealed class UmpiringVerificationResult
{
    public bool IsValid { get; init; }
    public List<(Match Match, string Reason)> Violations { get; init; } = [];
}

// ── Verification methods (partial class extension) ────────────────────────────
public sealed partial class SchedulingService
{
    /// <summary>
    /// Verifies the scheduled matches for three hard-constraint violations:
    ///   1. Two matches sharing the same ground + date + time slot.
    ///   2. A team playing more than one match on the same weekend.
    ///      (Multiple matches between the same pair across different weekends is valid
    ///       by design for divisions with FixedPairings / high MatchesPerTeam.)
    ///   3. A match placed on a forbidden slot.
    /// Fixed matches are never flagged — they act as immovable anchors. Any conflict
    /// between a fixed and non-fixed match is always attributed to the non-fixed match.
    /// Returns every violating non-fixed match.
    /// </summary>
    public ScheduleVerificationResult VerifySchedule(League league, List<ForbiddenSlot> forbidden)
    {
        var violations = new List<(Match Match, string Reason)>();
        var flagged    = new HashSet<Match>(ReferenceEqualityComparer.Instance);

        // Process by sequence so "keep the earlier match" is deterministic.
        var scheduled = league.Matches
            .Where(m => m.Date is not null && m.Slot is not null && m.Ground is not null)
            .OrderBy(m => m.Sequence)
            .ToList();

        // ── 1. Slot conflicts (same ground + date + time) ─────────────────────
        // Fixed matches are pre-seeded as occupants so they are never displaced;
        // only non-fixed matches can be flagged for a slot conflict.
        var slotOccupant = new Dictionary<string, Match>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in scheduled.Where(m => m.IsFixed))
            slotOccupant[$"{match.Date}|{match.Slot!.Start:HH\\:mm}|{match.Ground!.Name}"] = match;

        foreach (var match in scheduled.Where(m => !m.IsFixed))
        {
            var key = $"{match.Date}|{match.Slot!.Start:HH\\:mm}|{match.Ground!.Name}";
            if (slotOccupant.TryGetValue(key, out var occupant))
            {
                flagged.Add(match);
                violations.Add((match,
                    $"Slot conflict: ground '{match.Ground.Name}' at {match.Slot.Start:HH\\:mm} on {match.Date:MM/dd/yyyy} " +
                    $"already occupied by match #{occupant.Sequence} ({occupant.TeamOne} vs {occupant.TeamTwo})"));
            }
            else
            {
                slotOccupant[key] = match;
            }
        }

        // ── 2. Team with >1 match on same weekend ─────────────────────────────
        // Note: same two teams can legitimately play each other multiple times across
        // different weekends (division FixedPairings), but never on the same weekend.
        // Fixed matches are pre-seeded so conflicts are always attributed to the non-fixed match.
        var teamWeekendOccupant = new Dictionary<string, Match>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in scheduled.Where(m => m.IsFixed))
        {
            var weekend = ConstraintEvaluator.WeekendKey(match.Date!.Value).ToString("yyyy-MM-dd");
            foreach (var team in new[] { match.TeamOne, match.TeamTwo })
                teamWeekendOccupant.TryAdd($"{team}|{weekend}", match);
        }

        foreach (var match in scheduled.Where(m => !m.IsFixed && !flagged.Contains(m)))
        {
            var weekend = ConstraintEvaluator.WeekendKey(match.Date!.Value).ToString("yyyy-MM-dd");
            foreach (var team in new[] { match.TeamOne, match.TeamTwo })
            {
                var key = $"{team}|{weekend}";
                if (teamWeekendOccupant.TryGetValue(key, out var existing))
                {
                    if (!flagged.Contains(match))
                    {
                        flagged.Add(match);
                        violations.Add((match,
                            $"Team '{team}' already has match #{existing.Sequence} " +
                            $"({existing.TeamOne} vs {existing.TeamTwo}) on the same weekend"));
                    }
                }
                else
                {
                    teamWeekendOccupant[key] = match;
                }
            }
        }

        // ── 3. Forbidden slots ────────────────────────────────────────────────
        // Fixed matches may intentionally occupy forbidden slots; skip them entirely.
        foreach (var match in scheduled.Where(m => !m.IsFixed && !flagged.Contains(m)))
        {
            foreach (var f in forbidden)
            {
                bool divOk  = f.Division   is null || string.Equals(f.Division,   match.DivisionName, StringComparison.OrdinalIgnoreCase);
                bool dateOk = f.Date       is null || f.Date == match.Date;
                bool gndOk  = f.GroundName is null || string.Equals(f.GroundName, match.Ground!.Name,  StringComparison.OrdinalIgnoreCase);
                bool slotOk = f.TimeSlot   is null || f.TimeSlot.Start == match.Slot!.Start;
                if (divOk && dateOk && gndOk && slotOk)
                {
                    flagged.Add(match);
                    violations.Add((match, $"Scheduled on forbidden slot ({f.Display})"));
                    break;
                }
            }
        }

        return new ScheduleVerificationResult { IsValid = violations.Count == 0, Violations = violations };
    }

    /// <summary>
    /// Verifies umpiring assignments against five hard rules:
    ///   1. A team must not umpire in its own division.
    ///   2. A team must not umpire at a different ground than where it plays that weekend.
    ///   3. A team must not umpire in the same time slot as its own match.
    ///   4. A team must not umpire more than once per weekend.
    ///   5. A team's total umpiring duties must not exceed ⌈matchesPlaying / 2⌉.
    /// Fixed matches are excluded from violation checking (their assignments are intentionally locked),
    /// but their umpire loads still count toward the caps for non-fixed matches.
    /// Returns every non-fixed match whose UmpireOne assignment violates at least one rule.
    /// </summary>
    public UmpiringVerificationResult VerifyUmpiring(League league)
    {
        var violations = new List<(Match Match, string Reason)>();
        var matches    = league.Matches.Where(m => m.Date is not null && m.Slot is not null).ToList();

        // Build team → division lookup
        var teamDivision = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var div in league.Divisions)
            foreach (var t in div.Teams)
                teamDivision[t.Name] = div.Name;

        // Compute matches-played count per team (for the ⌈n/2⌉ cap)
        var matchesPlaying = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in matches)
        {
            matchesPlaying[m.TeamOne] = matchesPlaying.GetValueOrDefault(m.TeamOne, 0) + 1;
            matchesPlaying[m.TeamTwo] = matchesPlaying.GetValueOrDefault(m.TeamTwo, 0) + 1;
        }

        // Total umpiring load per team
        var umpireLoad = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in matches.Where(x => x.UmpireOne is not null))
            umpireLoad[m.UmpireOne!] = umpireLoad.GetValueOrDefault(m.UmpireOne!, 0) + 1;

        // Per-umpire-per-weekend: track to detect rule 4 (>1 assignment per weekend)
        var umpireWeekendMatches = new Dictionary<string, List<Match>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in matches.Where(x => x.UmpireOne is not null && x.Date is not null))
        {
            var wk = $"{m.UmpireOne!}|{ConstraintEvaluator.WeekendKey(m.Date!.Value):yyyy-MM-dd}";
            if (!umpireWeekendMatches.TryGetValue(wk, out var list))
                umpireWeekendMatches[wk] = list = [];
            list.Add(m);
        }

        foreach (var match in matches.Where(m => m.UmpireOne is not null && !m.IsFixed))
        {
            var umpire  = match.UmpireOne!;
            var reasons = new List<string>();

            // Rule 1: own division
            if (teamDivision.TryGetValue(umpire, out var umpDiv) &&
                string.Equals(umpDiv, match.DivisionName, StringComparison.OrdinalIgnoreCase))
                reasons.Add("umpiring own division");

            // Locate umpire's own playing match on the same weekend
            var ownMatch = matches.FirstOrDefault(m => m != match && m.Date is not null &&
                ConstraintEvaluator.IsWeekendEqual(m.Date, match.Date!.Value) &&
                ConstraintEvaluator.TeamMatch(umpire, m));

            if (ownMatch is not null)
            {
                // Rule 3: same date + time slot as own match
                if (ownMatch.Date == match.Date && ownMatch.Slot?.Start == match.Slot?.Start)
                    reasons.Add("umpiring in the same slot as own match");
                // Rule 2: different ground from own match on the same weekend
                else if (!string.Equals(ownMatch.Ground?.Name, match.Ground?.Name, StringComparison.OrdinalIgnoreCase))
                    reasons.Add($"umpiring at '{match.Ground?.Name ?? "?"}' but own match is at '{ownMatch.Ground?.Name ?? "?"}'");
            }

            // Rule 4: >1 umpiring duty per weekend — flag the later match(es), keep the first
            var wkKey = $"{umpire}|{ConstraintEvaluator.WeekendKey(match.Date!.Value):yyyy-MM-dd}";
            if (umpireWeekendMatches.TryGetValue(wkKey, out var wkList) && wkList.Count > 1)
            {
                var first = wkList.OrderBy(m => m.Date).ThenBy(m => m.Slot?.Start).First();
                if (match != first)
                    reasons.Add("umpiring more than once this weekend");
            }

            // Rule 5: total load cap
            var cap = (int)Math.Ceiling(matchesPlaying.GetValueOrDefault(umpire, 0) / 2.0);
            if (umpireLoad.GetValueOrDefault(umpire, 0) > cap)
                reasons.Add($"total umpiring duties ({umpireLoad[umpire]}) exceed cap ⌈{matchesPlaying.GetValueOrDefault(umpire, 0)}/2⌉ = {cap}");

            if (reasons.Count > 0)
                violations.Add((match, string.Join("; ", reasons)));
        }

        return new UmpiringVerificationResult { IsValid = violations.Count == 0, Violations = violations };
    }

    /// <summary>
    /// Verifies that every match pair defined by the current division settings exists in
    /// either league.Matches (scheduled) or league.UnscheduledMatches. Any expected pair
    /// not accounted for is created and appended to league.UnscheduledMatches with a
    /// descriptive reason. Handles fixed-pairing divisions where the same pair can appear
    /// multiple times — each expected occurrence must be satisfied by a distinct actual match.
    /// Returns the count of pairs restored.
    /// </summary>
    public int RestoreMissingPairs(League league)
    {
        var expected = MatchGenerator.GenerateMatches(league);

        // Build a multiset of actual matches using a canonical, case-insensitive key so
        // that (TeamOne, TeamTwo) order and casing differences do not cause false misses.
        var remaining = new Dictionary<(string, string, string), int>();
        foreach (var m in league.Matches.Concat(league.UnscheduledMatches))
        {
            var key = PairKey(m.TeamOne, m.TeamTwo, m.DivisionName);
            remaining[key] = remaining.GetValueOrDefault(key, 0) + 1;
        }

        int restored = 0;
        foreach (var m in expected)
        {
            var key = PairKey(m.TeamOne, m.TeamTwo, m.DivisionName);
            if (remaining.GetValueOrDefault(key, 0) > 0)
            {
                remaining[key]--;
            }
            else
            {
                m.UnscheduledReason = "Missing pair — restored by pair completion check";
                league.UnscheduledMatches.Add(m);
                restored++;
            }
        }

        return restored;
    }

    // Canonical key: alphabetically smaller team first, both names upper-cased so
    // (TeamOne, TeamTwo) order and name casing are both normalised.
    private static (string, string, string) PairKey(string t1, string t2, string div)
    {
        var a = t1.ToUpperInvariant();
        var b = t2.ToUpperInvariant();
        return string.Compare(a, b, StringComparison.Ordinal) <= 0
            ? (a, b, div.ToUpperInvariant())
            : (b, a, div.ToUpperInvariant());
    }
}
