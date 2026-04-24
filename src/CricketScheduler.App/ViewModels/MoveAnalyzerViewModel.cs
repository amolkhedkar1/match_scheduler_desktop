using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CricketScheduler.App.Models;
using CricketScheduler.App.Services;
using System.Collections.ObjectModel;

namespace CricketScheduler.App.ViewModels;

/// <summary>
/// Self-contained ViewModel for one level of the recursive Move Analyzer.
/// Each instance represents: "I want to place THIS match — here are all candidate slots
/// with their conflict counts, and here are the matches affected by the selected slot."
///
/// Constraint relaxation toggles dynamically affect slot generation and validation.
/// Commit is only enabled when the selected slot has ConflictCount == 0.
/// Unschedule (Bump) on an affected match is a real state mutation that recomputes slots.
/// Child Move Analyzer instances receive the parent's tentative placement as context.
/// </summary>
public sealed partial class MoveAnalyzerViewModel : ObservableObject // partial required for [ObservableProperty] codegen
{
    private readonly Match _matchToMove;
    private readonly League _league;
    private readonly SchedulingService _svc;
    private readonly List<ForbiddenSlot> _forbidden;
    // Extra matches treated as fixed context when computing slots for _matchToMove.
    // For a child analyzer: parent's match tentatively placed at parent's selected slot.
    private readonly IReadOnlyList<Match>? _additionalContext;

    // ── Callbacks set by MoveAnalyzerWindow ─────────────────────────────────────
    /// <summary>Called when user commits a move (closes window with DialogResult=true).</summary>
    public Action? OnCommitAction { get; set; }
    /// <summary>Called to open a child Move Analyzer popup for a recursive analysis.</summary>
    public Action<MoveAnalyzerViewModel>? OnPushChildAction { get; set; }

    // ── Match banner ─────────────────────────────────────────────────────────────
    public string MatchTitle => $"{_matchToMove.TeamOne}  vs  {_matchToMove.TeamTwo}";
    public string MatchDetail =>
        $"{_matchToMove.DivisionName}  ·  " +
        (_matchToMove.Date.HasValue
            ? $"{_matchToMove.Date:ddd MM/dd/yyyy}  {_matchToMove.Slot?.Start:HH:mm}–{_matchToMove.Slot?.End:HH:mm}  @ {_matchToMove.Ground?.Name ?? "—"}"
            : "Unscheduled");

    public int Depth { get; }
    public string WindowTitle => Depth == 0
        ? $"Move Analyzer — {_matchToMove.TeamOne} vs {_matchToMove.TeamTwo}"
        : $"Move Analyzer (Level {Depth + 1}) — Resolve: {_matchToMove.TeamOne} vs {_matchToMove.TeamTwo}";

    // ── Constraint relaxation toggles (one per RelaxedConstraints flag) ─────────
    [ObservableProperty] private bool relaxGroundFairness;
    [ObservableProperty] private bool relaxUmpireFairness;
    [ObservableProperty] private bool relaxTimeSlotFairness;
    [ObservableProperty] private bool relaxMaxGapRule;
    [ObservableProperty] private bool relaxOneMatchPerWeekend;
    [ObservableProperty] private bool relaxTimeSlotRestriction;
    [ObservableProperty] private bool relaxDateRestriction;
    [ObservableProperty] private bool relaxDiscardedDates;

    // ── Potential Slots Grid (primary) ────────────────────────────────────────
    public ObservableCollection<PotentialSlotRow> PotentialSlots { get; } = [];
    [ObservableProperty] private PotentialSlotRow? selectedPotentialSlot;

    // ── Affected Matches Grid (secondary) ─────────────────────────────────────
    public ObservableCollection<AffectedMatchRow> AffectedMatches { get; } = [];

    // ── Status / commit state ─────────────────────────────────────────────────
    [ObservableProperty] private string statusMessage = "Select a target slot above to see affected matches.";
    [ObservableProperty] private bool canCommit;

    public MoveAnalyzerViewModel(
        Match matchToMove,
        League league,
        SchedulingService svc,
        List<ForbiddenSlot> forbidden,
        int depth,
        IReadOnlyList<Match>? additionalContext = null)
    {
        _matchToMove       = matchToMove;
        _league            = league;
        _svc               = svc;
        _forbidden         = forbidden;
        Depth              = depth;
        _additionalContext = additionalContext;
        LoadPotentialSlots();
    }

    // ── Reload slot list when any toggle changes ──────────────────────────────
    partial void OnRelaxGroundFairnessChanged(bool value)       => LoadPotentialSlots();
    partial void OnRelaxUmpireFairnessChanged(bool value)       => LoadPotentialSlots();
    partial void OnRelaxTimeSlotFairnessChanged(bool value)     => LoadPotentialSlots();
    partial void OnRelaxMaxGapRuleChanged(bool value)           => LoadPotentialSlots();
    partial void OnRelaxOneMatchPerWeekendChanged(bool value)   => LoadPotentialSlots();
    partial void OnRelaxTimeSlotRestrictionChanged(bool value)  => LoadPotentialSlots();
    partial void OnRelaxDateRestrictionChanged(bool value)      => LoadPotentialSlots();
    partial void OnRelaxDiscardedDatesChanged(bool value)       => LoadPotentialSlots();

    private RelaxedConstraints BuildRelaxed() => new()
    {
        RelaxGroundFairness      = RelaxGroundFairness,
        RelaxUmpireFairness      = RelaxUmpireFairness,
        RelaxTimeSlotFairness    = RelaxTimeSlotFairness,
        RelaxMaxGapRule          = RelaxMaxGapRule,
        RelaxOneMatchPerWeekend  = RelaxOneMatchPerWeekend,
        RelaxTimeSlotRestriction = RelaxTimeSlotRestriction,
        RelaxDateRestriction     = RelaxDateRestriction,
        RelaxDiscardedDates      = RelaxDiscardedDates,
    };

    private void LoadPotentialSlots()
    {
        var prev = SelectedPotentialSlot;
        PotentialSlots.Clear();
        AffectedMatches.Clear();
        CanCommit = false;

        var relaxed = BuildRelaxed();
        var suggestions = _svc.SuggestMoves(
            _league, _matchToMove, _forbidden,
            relaxed: relaxed,
            additionalFixed: _additionalContext);

        foreach (var s in suggestions)
        {
            PotentialSlots.Add(new PotentialSlotRow
            {
                Date            = s.Date,
                Slot            = s.Slot,
                Ground          = s.Ground,
                DateDisplay     = s.Date.ToString("ddd MM/dd/yyyy"),
                TimeRange       = $"{s.Slot.Start:HH:mm}–{s.Slot.End:HH:mm}",
                GroundName      = s.Ground.Name,
                ConflictCount   = s.AffectedMatchCount,
                AffectedMatches = s.AffectedMatchList,
                FairnessScore   = s.FairnessScore,
            });
        }

        StatusMessage = PotentialSlots.Count == 0
            ? "No valid slots found. Try relaxing constraints above."
            : $"{PotentialSlots.Count} slot(s) available. Select one to see impact.";

        // Re-select previously selected slot if still present
        if (prev is not null)
        {
            SelectedPotentialSlot = PotentialSlots.FirstOrDefault(r =>
                r.Date == prev.Date &&
                r.Slot.Start == prev.Slot.Start &&
                string.Equals(r.GroundName, prev.GroundName, StringComparison.OrdinalIgnoreCase));
        }
    }

    partial void OnSelectedPotentialSlotChanged(PotentialSlotRow? value)
    {
        AffectedMatches.Clear();
        CanCommit = false;

        if (value is null) return;

        if (value.ConflictCount == 0)
        {
            StatusMessage = "Free slot — no conflicts. You can commit immediately.";
            CanCommit = true;
            return;
        }

        foreach (var m in value.AffectedMatches)
        {
            AffectedMatches.Add(new AffectedMatchRow
            {
                Match             = m,
                MatchTitle        = $"#{m.Sequence}  {m.TeamOne} vs {m.TeamTwo}  ({m.DivisionName})",
                CurrentAssignment = $"{m.Date:MM/dd/yyyy} {m.Slot?.Start:HH:mm} @ {m.Ground?.Name ?? "—"}",
            });
        }

        StatusMessage = $"{value.ConflictCount} conflict(s). Resolve via Move Analyzer or Unschedule each below, then re-select a slot with 0 conflicts to commit.";
    }

    // ── Open recursive Move Analyzer for an affected match ────────────────────
    [RelayCommand]
    private void OpenChildAnalyzer(AffectedMatchRow? row)
    {
        if (row is null || SelectedPotentialSlot is null) return;

        // Build the child's context: parent's additional context + parent's match
        // tentatively placed at the selected slot (treated as fixed so child won't suggest it).
        var virtualParent = new Match
        {
            TournamentName = _matchToMove.TournamentName,
            DivisionName   = _matchToMove.DivisionName,
            MatchType      = _matchToMove.MatchType,
            TeamOne        = _matchToMove.TeamOne,
            TeamTwo        = _matchToMove.TeamTwo,
            Date           = SelectedPotentialSlot.Date,
            Slot           = SelectedPotentialSlot.Slot,
            Ground         = SelectedPotentialSlot.Ground,
            IsFixed        = true,
        };

        var childContext = (_additionalContext ?? []).Append(virtualParent).ToList();
        var child = new MoveAnalyzerViewModel(row.Match, _league, _svc, _forbidden, Depth + 1, childContext);
        OnPushChildAction?.Invoke(child);
    }

    // ── Unschedule (Bump) an affected match — real state mutation ─────────────
    [RelayCommand]
    private void UnscheduleMatch(AffectedMatchRow? row)
    {
        if (row is null || row.Match.IsFixed) return;

        // Mutate real state
        _league.Matches.Remove(row.Match);
        row.Match.Date  = null;
        row.Match.Slot  = null;
        row.Match.Ground = null;
        row.Match.UnscheduledReason = "Bumped by Move Analyzer";
        if (!_league.UnscheduledMatches.Contains(row.Match))
            _league.UnscheduledMatches.Add(row.Match);

        StatusMessage = $"Match '{row.Match.TeamOne} vs {row.Match.TeamTwo}' unscheduled (bumped). Slots recomputed.";
        LoadPotentialSlots();
    }

    // ── Commit move — only enabled when ConflictCount == 0 ───────────────────
    [RelayCommand]
    private void CommitMove()
    {
        if (SelectedPotentialSlot is null || !CanCommit) return;

        // Apply the slot assignment
        _matchToMove.Date   = SelectedPotentialSlot.Date;
        _matchToMove.Slot   = SelectedPotentialSlot.Slot;
        _matchToMove.Ground = SelectedPotentialSlot.Ground;

        // If this was an unscheduled match, move it to the scheduled list
        if (_league.UnscheduledMatches.Contains(_matchToMove))
        {
            _league.UnscheduledMatches.Remove(_matchToMove);
            _matchToMove.Sequence = (_league.Matches.MaxBy(m => m.Sequence)?.Sequence ?? 0) + 1;
            _league.Matches.Add(_matchToMove);
        }

        OnCommitAction?.Invoke();
    }

    /// <summary>
    /// Called after a child Move Analyzer closes (commit or cancel with unschedule changes).
    /// Reloads slots so this level reflects any real-state changes made by the child.
    /// </summary>
    public void RefreshAfterChildAction()
    {
        LoadPotentialSlots();
    }
}

// ── PotentialSlotRow — Potential Slots Grid row ───────────────────────────────
public sealed class PotentialSlotRow
{
    public DateOnly  Date            { get; init; }
    public TimeSlot  Slot            { get; init; } = null!;
    public Ground    Ground          { get; init; } = null!;
    public string    DateDisplay     { get; init; } = string.Empty;
    public string    TimeRange       { get; init; } = string.Empty;
    public string    GroundName      { get; init; } = string.Empty;
    public int       ConflictCount   { get; init; }
    public double    FairnessScore   { get; init; }
    public List<Match> AffectedMatches { get; init; } = [];
}

// ── AffectedMatchRow — Affected Matches Grid row ──────────────────────────────
public sealed class AffectedMatchRow
{
    public Match  Match             { get; init; } = null!;
    public string MatchTitle        { get; init; } = string.Empty;
    public string CurrentAssignment { get; init; } = string.Empty;
    /// <summary>Fixed matches cannot be unscheduled — hides the Unschedule button.</summary>
    public bool   CanUnschedule     => !Match.IsFixed;
}
