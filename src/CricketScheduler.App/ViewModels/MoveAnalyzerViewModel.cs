using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CricketScheduler.App.Models;
using CricketScheduler.App.Services;
using System.Collections.ObjectModel;

namespace CricketScheduler.App.ViewModels;

/// <summary>
/// Self-contained ViewModel for one level of the solitaire move-analyzer popup chain.
/// Each instance represents: "I want to move THIS match — here are the options,
/// and here is how the displaced matches will be affected."
/// If the user picks a slot that displaces another match they cannot immediately
/// re-place, a new MoveAnalyzerViewModel is pushed onto the chain for that match.
/// </summary>
public sealed partial class MoveAnalyzerViewModel : ObservableObject
{
    private readonly League _league;
    private readonly SchedulingService _svc;
    private readonly List<ForbiddenSlot> _forbidden;

    /// <summary>Set by MoveAnalyzerWindow after construction — called when user commits a move.</summary>
    public Action? OnCommitAction { get; set; }
    /// <summary>Set by MoveAnalyzerWindow after construction — called to open a child popup.</summary>
    public Action<MoveAnalyzerViewModel>? OnPushChildAction { get; set; }

    // ── Match being moved at this level ──────────────────────────────────────
    public Match MatchToMove { get; }
    public string MatchTitle => $"{MatchToMove.TeamOne}  vs  {MatchToMove.TeamTwo}";
    public string MatchDetail =>
        $"{MatchToMove.DivisionName}  ·  " +
        $"{MatchToMove.Date:ddd MM/dd/yyyy}  " +
        $"{MatchToMove.Slot?.Start:HH:mm}–{MatchToMove.Slot?.End:HH:mm}  " +
        $"@ {MatchToMove.Ground?.Name ?? "—"}";

    // ── Depth in the chain (for popup title) ─────────────────────────────────
    public int Depth { get; }
    public string WindowTitle => Depth == 0
        ? $"Move Analyzer — Match #{MatchToMove.Sequence}"
        : $"Move Analyzer (Level {Depth + 1}) — Resolve displaced match #{MatchToMove.Sequence}";

    // ── Upper grid: all possible target slots ─────────────────────────────────
    public ObservableCollection<SlotOptionRow> SlotOptions { get; } = [];

    [ObservableProperty] private SlotOptionRow? selectedSlot;

    // ── Lower grid: how displaced matches will be affected ───────────────────
    public ObservableCollection<DisplacedMatchRow> DisplacedMatches { get; } = [];

    // ── Status / instructions ─────────────────────────────────────────────────
    [ObservableProperty] private string statusMessage = "Select a target slot above to see affected matches below.";
    [ObservableProperty] private bool canCommit;

    public MoveAnalyzerViewModel(
        Match matchToMove,
        League league,
        SchedulingService svc,
        List<ForbiddenSlot> forbidden,
        int depth)
    {
        MatchToMove  = matchToMove;
        _league      = league;
        _svc         = svc;
        _forbidden   = forbidden;
        Depth        = depth;
        LoadSlotOptions();
    }

    private void LoadSlotOptions()
    {
        SlotOptions.Clear();
        var suggestions = _svc.SuggestMoves(_league, MatchToMove, _forbidden);
        foreach (var s in suggestions)
        {
            SlotOptions.Add(new SlotOptionRow
            {
                Date             = s.Date,
                Slot             = s.Slot,
                Ground           = s.Ground,
                DateDisplay      = s.Date.ToString("ddd MM/dd/yyyy"),
                TimeRange        = $"{s.Slot.Start:HH:mm}–{s.Slot.End:HH:mm}",
                GroundName       = s.Ground.Name,
                AffectedCount    = s.AffectedMatchCount,
                FairnessScore    = s.FairnessScore,
                IsRecommended    = s.IsRecommended,
                AffectedMatches  = s.AffectedMatchList,
                Summary          = s.AffectedMatchCount == 0
                    ? "✅ Free slot — no conflicts"
                    : $"⚠ {s.AffectedMatchCount} match(es) need to move"
            });
        }
        StatusMessage = SlotOptions.Count == 0
            ? "❌ No valid target slots found. Try relaxing constraints."
            : $"{SlotOptions.Count} slot(s) available. Select one to analyse impact.";
    }

    partial void OnSelectedSlotChanged(SlotOptionRow? value)
    {
        DisplacedMatches.Clear();
        CanCommit = false;
        if (value is null) return;

        if (value.AffectedCount == 0)
        {
            StatusMessage = "✅ This is a free slot — you can commit the move immediately.";
            CanCommit = true;
            return;
        }

        // Show displaced matches and for each, find where they COULD go
        foreach (var m in value.AffectedMatches)
        {
            var options = _svc.SuggestMoves(_league, m, _forbidden);
            var best    = options.FirstOrDefault(o => o.AffectedMatchCount == 0);
            DisplacedMatches.Add(new DisplacedMatchRow
            {
                Match           = m,
                MatchTitle      = $"#{m.Sequence}  {m.TeamOne} vs {m.TeamTwo}",
                CurrentSlot     = $"{m.Date:MM/dd/yyyy} {m.Slot?.Start:HH:mm} @ {m.Ground?.Name}",
                CanAutoResolve  = best is not null,
                AutoResolveTo   = best is null ? "No free slot — needs sub-analysis"
                    : $"→ {best.Date:MM/dd/yyyy} {best.Slot.Start:HH:mm} @ {best.Ground.Name}",
                BestSuggestion  = best,
                AllOptions      = options
            });
        }

        bool allAutoResolvable = DisplacedMatches.All(r => r.CanAutoResolve);
        if (allAutoResolvable)
        {
            StatusMessage = $"✅ All {DisplacedMatches.Count} displaced match(es) have a free slot. " +
                            $"You can commit — they will be auto-resolved.";
            CanCommit = true;
        }
        else
        {
            int needsWork = DisplacedMatches.Count(r => !r.CanAutoResolve);
            StatusMessage = $"⚠ {needsWork} displaced match(es) have no free slot. " +
                            $"Click 'Resolve' on each to open a sub-analyzer.";
            CanCommit = false;
        }
    }

    /// <summary>Open a sub-analyzer popup for a displaced match that cannot be auto-resolved.</summary>
    [RelayCommand]
    private void ResolveDisplaced(DisplacedMatchRow? row)
    {
        if (row is null) return;
        var child = new MoveAnalyzerViewModel(row.Match, _league, _svc, _forbidden, Depth + 1);
        OnPushChildAction?.Invoke(child);
    }

    /// <summary>
    /// Called by the window after a child-level move is committed.
    /// Reloads slot options so the parent grid reflects the child's applied move,
    /// then re-selects the previously selected target slot (which may now show
    /// zero displaced matches if the child resolved the conflict).
    /// </summary>
    public void RefreshAfterChildCommit()
    {
        var prev = SelectedSlot;
        LoadSlotOptions();
        if (prev is not null)
            SelectedSlot = SlotOptions.FirstOrDefault(s =>
                s.Date == prev.Date &&
                s.Slot.Start == prev.Slot.Start &&
                string.Equals(s.GroundName, prev.GroundName, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private async Task CommitMove()
    {
        if (SelectedSlot is null || !CanCommit) return;

        foreach (var displaced in DisplacedMatches.Where(r => r.CanAutoResolve && r.BestSuggestion is not null))
            ApplyMove(displaced.Match, displaced.BestSuggestion!);

        ApplyMove(MatchToMove, SelectedSlot);
        OnCommitAction?.Invoke();
    }

    private static void ApplyMove(Match match, MoveSlotSuggestion suggestion)
    {
        match.Date   = suggestion.Date;
        match.Slot   = suggestion.Slot;
        match.Ground = suggestion.Ground;
    }

    private static void ApplyMove(Match match, SlotOptionRow slot)
    {
        match.Date   = slot.Date;
        match.Slot   = slot.Slot;
        match.Ground = slot.Ground;
    }
}

// ── SlotOptionRow — upper grid row ───────────────────────────────────────────
public sealed class SlotOptionRow
{
    public DateOnly  Date            { get; init; }
    public TimeSlot  Slot            { get; init; } = null!;
    public Ground    Ground          { get; init; } = null!;
    public string    DateDisplay     { get; init; } = string.Empty;
    public string    TimeRange       { get; init; } = string.Empty;
    public string    GroundName      { get; init; } = string.Empty;
    public int       AffectedCount   { get; init; }
    public double    FairnessScore   { get; init; }
    public bool      IsRecommended   { get; init; }
    public string    Summary         { get; init; } = string.Empty;
    public List<Match> AffectedMatches { get; init; } = [];
}

// ── DisplacedMatchRow — lower grid row ───────────────────────────────────────
public sealed class DisplacedMatchRow
{
    public Match   Match           { get; init; } = null!;
    public string  MatchTitle      { get; init; } = string.Empty;
    public string  CurrentSlot     { get; init; } = string.Empty;
    public bool    CanAutoResolve  { get; init; }
    public string  AutoResolveTo   { get; init; } = string.Empty;
    public MoveSlotSuggestion? BestSuggestion { get; init; }
    public List<MoveSlotSuggestion> AllOptions { get; init; } = [];
}
