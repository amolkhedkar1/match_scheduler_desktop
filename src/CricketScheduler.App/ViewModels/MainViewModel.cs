using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CricketScheduler.App.Models;
using CricketScheduler.App.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

// InputDialog lives in CricketScheduler.App (root namespace)

namespace CricketScheduler.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly LeagueService _leagueService;
    private readonly SchedulingService _schedulingService;
    private readonly ExportService _exportService;
    private readonly string _leaguesRoot;

    // ── League ──────────────────────────────────────────────────────────────
    [ObservableProperty] private string? selectedLeagueName;
    [ObservableProperty] private League? currentLeague;
    [ObservableProperty] private string statusMessage = "No league loaded";

    // ── Tournament form ──────────────────────────────────────────────────────
    [ObservableProperty] private string tournamentName = string.Empty;
    [ObservableProperty] private DateTime? tournamentStartDate;
    [ObservableProperty] private DateTime? tournamentEndDate;
    [ObservableProperty] private DateTime? newDiscardedDate;
    [ObservableProperty] private string? selectedDiscardedDate;
    [ObservableProperty] private string newGroundName = string.Empty;
    [ObservableProperty] private string? selectedGroundName;
    [ObservableProperty] private string newSlotStart = string.Empty;
    [ObservableProperty] private string newSlotEnd = string.Empty;
    [ObservableProperty] private string? selectedTimeSlotDisplay;

    // ── Division form ────────────────────────────────────────────────────────
    [ObservableProperty] private Division? selectedDivision;
    [ObservableProperty] private string newDivisionName = string.Empty;
    [ObservableProperty] private bool newDivisionIsRoundRobin = true;
    [ObservableProperty] private string newDivisionMatchesPerTeam = string.Empty;
    [ObservableProperty] private Team? selectedTeam;
    [ObservableProperty] private string newTeamName = string.Empty;

    // ── Edit selected division scheduling mode ─────────────────────────────
    [ObservableProperty] private bool editDivisionIsRoundRobin = true;
    [ObservableProperty] private string editDivisionMatchesPerTeam = string.Empty;

    /// <summary>True when selected division is Fixed (not Round Robin) — drives pairings panel visibility.</summary>
    public bool SelectedDivisionIsFixed => SelectedDivision is { IsRoundRobin: false };

    public ObservableCollection<PairingRow> SelectedDivisionPairings { get; } = [];

    // ── Request form ─────────────────────────────────────────────────────────
    [ObservableProperty] private string newRequestTeam = string.Empty;
    [ObservableProperty] private DateTime? newRequestDate;
    [ObservableProperty] private string? newRequestTimeSlot;   // selected from existing slots e.g. "07:00 - 09:00"
    [ObservableProperty] private SchedulingRequestRow? selectedRequest;

    // ── Scheduler filters ────────────────────────────────────────────────────
    [ObservableProperty] private string? filterDivision;
    [ObservableProperty] private string? filterTeam;
    [ObservableProperty] private string? filterGround;
    [ObservableProperty] private ScheduleRowViewModel? selectedMatch;
    [ObservableProperty] private UnscheduledMatchRow? selectedUnscheduledMatch;

    // ── Manual scheduling inputs (outside-grid panel for unscheduled matches) ──
    [ObservableProperty] private string manualScheduleDate = string.Empty;
    [ObservableProperty] private string? manualScheduleTimeSlot;
    [ObservableProperty] private string? manualScheduleGround;
    [ObservableProperty] private string? manualScheduleUmpire;

    // ── Forbidden slots form ─────────────────────────────────────────────────
    [ObservableProperty] private DateTime? newForbiddenDate;
    [ObservableProperty] private string? newForbiddenGround;
    [ObservableProperty] private string? newForbiddenTimeSlot;
    [ObservableProperty] private string? newForbiddenDivision;
    [ObservableProperty] private ForbiddenSlot? selectedForbiddenSlot;

    // ── Move match panel ─────────────────────────────────────────────────────
    [ObservableProperty] private ScheduleRowViewModel? matchToMove;
    [ObservableProperty] private string moveAnalysis = string.Empty;
    [ObservableProperty] private bool showMovePanel;

    // ── Unscheduled slot selection + affected-match resolution ───────────────
    [ObservableProperty] private MoveOptionViewModel? selectedUnscheduledMoveOption;

    // ── Scheduler text search filter ─────────────────────────────────────────
    [ObservableProperty] private string scheduleSearchText = string.Empty;

    // ── Request filter ───────────────────────────────────────────────────────
    [ObservableProperty] private string requestSearchText = string.Empty;

    // ── Division filter ──────────────────────────────────────────────────────
    [ObservableProperty] private string divisionSearchText = string.Empty;

    // ── Collections ──────────────────────────────────────────────────────────
    public ObservableCollection<string> LeagueNames { get; } = [];
    public ObservableCollection<string> DiscardedDates { get; } = [];
    public ObservableCollection<string> GroundNames { get; } = [];
    public ObservableCollection<string> TimeSlotDisplays { get; } = [];
    public ObservableCollection<Division> Divisions { get; } = [];
    public ObservableCollection<Team> TeamsInSelectedDivision { get; } = [];
    public ObservableCollection<SchedulingRequestRow> SchedulingRequests { get; } = [];
    public ObservableCollection<ScheduleRowViewModel> ScheduledMatches { get; } = [];
    public ObservableCollection<ScheduleRowViewModel> FilteredScheduledMatches { get; } = [];
    public ObservableCollection<string> AllTeamNames { get; } = [];
    public ObservableCollection<string> FilterDivisions { get; } = [];
    public ObservableCollection<string> FilterTeams { get; } = [];
    public ObservableCollection<string> FilterGrounds { get; } = [];
    public ObservableCollection<ForbiddenSlot> ForbiddenSlots { get; } = [];
    public ObservableCollection<MoveOptionViewModel> MoveOptions { get; } = [];
    public ObservableCollection<MoveOptionViewModel> UnscheduledMoveOptions { get; } = [];
    public ObservableCollection<AffectedSlotMatchRow> AffectedMatchesForSelectedSlot { get; } = [];
    public bool HasAffectedMatches => AffectedMatchesForSelectedSlot.Count > 0;
    public ObservableCollection<UnscheduledMatchRow> UnscheduledMatches { get; } = [];
    public ObservableCollection<SchedulingRequestRow> FilteredRequests { get; } = [];
    public ObservableCollection<Division> FilteredDivisions { get; } = [];

    public StatisticsViewModel StatisticsVM { get; } = new();

    public string SelectedDivisionName => SelectedDivision?.Name ?? "(select a division)";
    public string ScheduleStats => ScheduledMatches.Count == 0 ? "No matches scheduled" : $"{FilteredScheduledMatches.Count} of {ScheduledMatches.Count} matches shown";

    public MainViewModel(LeagueService leagueService, SchedulingService schedulingService, ExportService exportService, string leaguesRoot)
    {
        _leagueService = leagueService;
        _schedulingService = schedulingService;
        _exportService = exportService;
        _leaguesRoot = leaguesRoot;

        RefreshLeagueNames();
        EnsureSampleLeagueAndLoadAsync().GetAwaiter().GetResult();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // League commands
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Set by MainWindow so dialogs can be modal to the main window.</summary>
    public Window? OwnerWindow { get; set; }

    [RelayCommand]
    private void CreateLeague()
    {
        var dlg = new InputDialog("New League", "Enter league name:") { Owner = OwnerWindow };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result))
            return;
        var name = dlg.Result.Trim();
        _leagueService.CreateLeague(name);
        RefreshLeagueNames();
        SelectedLeagueName = name;
        StatusMessage = $"League '{name}' created. Fill in details and save.";

        // Create a blank league in memory so the tabs are editable immediately
        CurrentLeague = new League
        {
            Name = name,
            Tournament = new Tournament
            {
                Name = name,
                StartDate = DateOnly.FromDateTime(DateTime.Today),
                EndDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(3))
            }
        };
        PopulateFormsFromLeague();
    }

    [RelayCommand]
    private async Task OpenLeague()
    {
        if (string.IsNullOrWhiteSpace(SelectedLeagueName)) return;
        CurrentLeague = await _leagueService.LoadLeagueAsync(SelectedLeagueName);
        if (CurrentLeague is null)
        {
            StatusMessage = "Could not load league (missing tournament.csv?).";
            return;
        }
        PopulateFormsFromLeague();
        StatusMessage = $"Loaded: {SelectedLeagueName}";
    }

    [RelayCommand]
    private void DeleteLeague()
    {
        if (string.IsNullOrWhiteSpace(SelectedLeagueName)) return;
        if (MessageBox.Show($"Delete league '{SelectedLeagueName}'?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        _leagueService.DeleteLeague(SelectedLeagueName);
        SelectedLeagueName = null;
        CurrentLeague = null;
        ClearAllForms();
        RefreshLeagueNames();
        StatusMessage = "League deleted.";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tournament commands
    // ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddDiscardedDate()
    {
        if (NewDiscardedDate is null) { StatusMessage = "Please select a date."; return; }
        var s = DateOnly.FromDateTime(NewDiscardedDate.Value).ToString("yyyy-MM-dd");
        if (!DiscardedDates.Contains(s)) DiscardedDates.Add(s);
        NewDiscardedDate = null;
    }

    [RelayCommand]
    private void RemoveDiscardedDate()
    {
        if (SelectedDiscardedDate is not null)
            DiscardedDates.Remove(SelectedDiscardedDate);
    }

    [RelayCommand]
    private void AddGround()
    {
        if (string.IsNullOrWhiteSpace(NewGroundName)) return;
        if (!GroundNames.Contains(NewGroundName.Trim()))
            GroundNames.Add(NewGroundName.Trim());
        NewGroundName = string.Empty;
    }

    [RelayCommand]
    private void RemoveGround()
    {
        if (SelectedGroundName is not null)
            GroundNames.Remove(SelectedGroundName);
    }

    [RelayCommand]
    private void AddTimeSlot()
    {
        if (!TimeOnly.TryParse(NewSlotStart, out var start) || !TimeOnly.TryParse(NewSlotEnd, out var end))
        { StatusMessage = "Invalid time format (HH:mm)."; return; }
        var display = $"{start:HH:mm} - {end:HH:mm}";
        if (!TimeSlotDisplays.Contains(display))
            TimeSlotDisplays.Add(display);
        NewSlotStart = NewSlotEnd = string.Empty;
    }

    [RelayCommand]
    private void RemoveTimeSlot()
    {
        if (SelectedTimeSlotDisplay is not null)
            TimeSlotDisplays.Remove(SelectedTimeSlotDisplay);
    }

    [RelayCommand]
    private async Task SaveTournament()
    {
        if (CurrentLeague is null) { StatusMessage = "Open or create a league first."; return; }
        if (TournamentStartDate is null || TournamentEndDate is null)
        { StatusMessage = "Please select Start and End dates."; return; }
        var start = DateOnly.FromDateTime(TournamentStartDate.Value);
        var end = DateOnly.FromDateTime(TournamentEndDate.Value);

        var discarded = DiscardedDates.Select(DateOnly.Parse).ToList();
        var grounds = GroundNames.Select(g => new Ground { Name = g }).ToList();
        var slots = TimeSlotDisplays.Select(ParseSlotDisplay).ToList();

        var updatedTournament = new Tournament
        {
            Name = TournamentName,
            StartDate = start,
            EndDate = end,
            DiscardedDates = discarded,
            Grounds = grounds,
            TimeSlots = slots
        };

        var updated = new League
        {
            Name = CurrentLeague.Name,
            Tournament = updatedTournament,
            Divisions = CurrentLeague.Divisions,
            Constraints = CurrentLeague.Constraints,
            Matches = CurrentLeague.Matches
        };
        CurrentLeague = updated;

        await _leagueService.SaveLeagueAsync(CurrentLeague);
        StatusMessage = "Tournament saved.";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Division commands
    // ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddDivision()
    {
        if (string.IsNullOrWhiteSpace(NewDivisionName)) return;
        if (Divisions.Any(d => d.Name == NewDivisionName.Trim())) return;
        int? mpt = int.TryParse(NewDivisionMatchesPerTeam, out var n) ? n : null;
        Divisions.Add(new Division
        {
            Name = NewDivisionName.Trim(),
            IsRoundRobin = NewDivisionIsRoundRobin,
            MatchesPerTeam = mpt
        });
        NewDivisionName = string.Empty;
        RefreshDivisionFilters();
    }

    [RelayCommand]
    private void RemoveDivision()
    {
        if (SelectedDivision is null) return;
        Divisions.Remove(SelectedDivision);
        SelectedDivision = null;
        TeamsInSelectedDivision.Clear();
        RefreshDivisionFilters();
    }

    [RelayCommand]
    private void UpdateDivisionMode()
    {
        if (SelectedDivision is null) return;
        SelectedDivision.IsRoundRobin = EditDivisionIsRoundRobin;
        SelectedDivision.MatchesPerTeam = int.TryParse(EditDivisionMatchesPerTeam, out var n) ? n : null;

        // If switched to fixed mode, auto-generate pairings
        if (!SelectedDivision.IsRoundRobin && SelectedDivision.Teams.Count >= 2)
            GeneratePairingsForDivision(SelectedDivision);

        // Force list refresh so ModeSummary updates
        // IMPORTANT: capture reference BEFORE remove so we can re-select it
        var div = SelectedDivision;
        var idx = Divisions.IndexOf(div);
        if (idx >= 0) { Divisions.RemoveAt(idx); Divisions.Insert(idx, div); }
        SyncFilteredDivisions();
        // Re-select (RemoveAt temporarily nullifies SelectedDivision)
        SelectedDivision = div;
        OnPropertyChanged(nameof(SelectedDivisionIsFixed));
        RefreshPairingsPanel();
        StatusMessage = $"Division '{div.Name}' mode updated: {div.ModeSummary}";
    }

    partial void OnSelectedDivisionChanged(Division? value)
    {
        OnPropertyChanged(nameof(SelectedDivisionName));
        OnPropertyChanged(nameof(SelectedDivisionIsFixed));
        TeamsInSelectedDivision.Clear();
        if (value is null) { SelectedDivisionPairings.Clear(); return; }
        foreach (var t in value.Teams)
            TeamsInSelectedDivision.Add(t);

        // Populate edit fields for the selected division
        EditDivisionIsRoundRobin = value.IsRoundRobin;
        EditDivisionMatchesPerTeam = value.MatchesPerTeam?.ToString() ?? string.Empty;
        RefreshPairingsPanel();
    }

    private void RefreshPairingsPanel()
    {
        SelectedDivisionPairings.Clear();
        if (SelectedDivision is null || SelectedDivision.IsRoundRobin) return;
        var i = 1;
        foreach (var (a, b) in SelectedDivision.FixedPairings)
            SelectedDivisionPairings.Add(new PairingRow { Index = i++, TeamA = a, TeamB = b });
    }

    [RelayCommand]
    private void GeneratePairings()
    {
        if (SelectedDivision is null || SelectedDivision.IsRoundRobin) return;
        GeneratePairingsForDivision(SelectedDivision);
        RefreshPairingsPanel();
        // Refresh ModeSummary in list
        var div = SelectedDivision;
        var idx = Divisions.IndexOf(div);
        if (idx >= 0) { Divisions.RemoveAt(idx); Divisions.Insert(idx, div); }
        SyncFilteredDivisions();
        SelectedDivision = div;
        StatusMessage = $"Generated {div.FixedPairings.Count} pairings for '{div.Name}'.";
    }

    /// <summary>
    /// Balanced fixed pairing using the skip algorithm:
    /// Sort teams A-Z. Pair team[i] with team[n-1-i] so each plays MatchesPerTeam opponents
    /// cycling through rounds until quota reached.
    /// Example: 10 teams, 8 matches → T1 skips T10, T2 skips T9, ... T5 skips T6.
    /// </summary>
    private static void GeneratePairingsForDivision(Division division)
    {
        // Sort alphabetically, case-insensitive — determines the fixed pairing order
        var teams = division.Teams
            .Select(t => t.Name)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int n = teams.Count;
        if (n < 2) { division.FixedPairings.Clear(); return; }

        int target = division.MatchesPerTeam ?? (n - 1);
        target = Math.Max(1, Math.Min(target, n - 1));

        var matchCount = teams.ToDictionary(t => t, _ => 0, StringComparer.OrdinalIgnoreCase);
        var usedPairs  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result     = new List<(string, string)>();

        // skip = how many opponents each team does NOT play from the far end of the sorted list.
        // e.g. 10 teams, 8 matches → skip=1: T[0] skips only T[9], T[1] skips only T[8], etc.
        //      10 teams, 6 matches → skip=3: T[0] skips T[9],T[8],T[7]; T[1] skips T[8],T[7],T[6]; etc.
        int skip = Math.Max(0, n - target - 1);

        // Build forbidden set: team[i] cannot play team[j] where j is one of the
        // `skip` teams counting inward from the mirror position (n-1-i).
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < n; i++)
        {
            for (int s = 0; s < skip; s++)
            {
                int j = (n - 1 - i) - s;   // mirror position, then s steps inward
                if (j > i && j < n)
                {
                    var fkey = string.Compare(teams[i], teams[j], StringComparison.OrdinalIgnoreCase) < 0
                        ? $"{teams[i]}|{teams[j]}" : $"{teams[j]}|{teams[i]}";
                    forbidden.Add(fkey);
                }
            }
        }

        // Greedily assign valid pairs in alphabetical order (i < j),
        // skipping forbidden pairs and stopping each team once it reaches `target`.
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (matchCount[teams[i]] >= target) break;        // team[i] is full — move to next i
                if (matchCount[teams[j]] >= target) continue;     // team[j] is full — try next j

                var key = string.Compare(teams[i], teams[j], StringComparison.OrdinalIgnoreCase) < 0
                    ? $"{teams[i]}|{teams[j]}" : $"{teams[j]}|{teams[i]}";

                if (forbidden.Contains(key) || usedPairs.Contains(key)) continue;

                result.Add((teams[i], teams[j]));
                usedPairs.Add(key);
                matchCount[teams[i]]++;
                matchCount[teams[j]]++;
            }
        }

        division.FixedPairings = result;
    }

    [RelayCommand]
    private void AddTeam()
    {
        if (SelectedDivision is null || string.IsNullOrWhiteSpace(NewTeamName)) return;
        if (SelectedDivision.Teams.Any(t => t.Name == NewTeamName.Trim())) return;
        var team = new Team { Name = NewTeamName.Trim(), DivisionName = SelectedDivision.Name };
        SelectedDivision.Teams.Add(team);
        TeamsInSelectedDivision.Add(team);
        NewTeamName = string.Empty;
        RefreshAllTeamNames();
    }

    [RelayCommand]
    private void RemoveTeam()
    {
        if (SelectedDivision is null || SelectedTeam is null) return;
        SelectedDivision.Teams.Remove(SelectedTeam);
        TeamsInSelectedDivision.Remove(SelectedTeam);
        RefreshAllTeamNames();
    }

    [RelayCommand]
    private async Task SaveDivisions()
    {
        if (CurrentLeague is null) { StatusMessage = "Open or create a league first."; return; }
        CurrentLeague.Divisions = Divisions.ToList();
        await _leagueService.SaveLeagueAsync(CurrentLeague);
        StatusMessage = "Divisions saved.";
    }

    [RelayCommand]
    private void ImportDivisions()
    {
        var dlg = new OpenFileDialog { Filter = "CSV files|*.csv|All files|*.*", Title = "Import Divisions CSV" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var lines = File.ReadAllLines(dlg.FileName).Skip(1); // skip header
            foreach (var line in lines)
            {
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                var divName = parts[0].Trim();
                var teamName = parts[1].Trim();
                var div = Divisions.FirstOrDefault(d => d.Name == divName);
                if (div is null)
                {
                    div = new Division { Name = divName, IsRoundRobin = true };
                    Divisions.Add(div);
                }
                if (!div.Teams.Any(t => t.Name == teamName))
                    div.Teams.Add(new Team { Name = teamName, DivisionName = divName });
            }
            RefreshDivisionFilters();
            RefreshAllTeamNames();
            StatusMessage = "Divisions imported.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Import Error"); }
    }

    /// <summary>
    /// Append (merge) divisions/teams from a second CSV file without wiping existing data.
    /// Same format as ImportDivisions: DivisionName, TeamName, [IsRoundRobin], [MatchesPerTeam]
    /// </summary>
    [RelayCommand]
    private void AppendDivisions()
    {
        var dlg = new OpenFileDialog { Filter = "CSV files|*.csv|All files|*.*", Title = "Append Divisions CSV" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var lines = File.ReadAllLines(dlg.FileName).Skip(1);
            int added = 0;
            foreach (var line in lines)
            {
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                var divName  = parts[0].Trim();
                var teamName = parts[1].Trim();
                if (string.IsNullOrWhiteSpace(divName) || string.IsNullOrWhiteSpace(teamName)) continue;

                var div = Divisions.FirstOrDefault(d => d.Name.Equals(divName, StringComparison.OrdinalIgnoreCase));
                if (div is null)
                {
                    bool rr = parts.Length < 3 || !bool.TryParse(parts[2].Trim(), out var b) || b;
                    int? mpt = parts.Length >= 4 && int.TryParse(parts[3].Trim(), out var m) ? m : null;
                    div = new Division { Name = divName, IsRoundRobin = rr, MatchesPerTeam = mpt };
                    Divisions.Add(div);
                }
                if (!div.Teams.Any(t => t.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase)))
                {
                    div.Teams.Add(new Team { Name = teamName, DivisionName = divName });
                    added++;
                }
            }
            SyncFilteredDivisions();
            RefreshDivisionFilters();
            RefreshAllTeamNames();
            StatusMessage = $"Appended: {added} team(s) merged from CSV.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Append Error"); }
    }

    [RelayCommand]
    private void AddRequest()
    {
        if (string.IsNullOrWhiteSpace(NewRequestTeam) || NewRequestDate is null)
        { StatusMessage = "Team and Date are required."; return; }
        var date = DateOnly.FromDateTime(NewRequestDate.Value);

        // Parse optional time slot from selected slot display e.g. "07:00 - 09:00"
        TimeOnly? start = null, end = null;
        if (!string.IsNullOrWhiteSpace(NewRequestTimeSlot))
        {
            var slot = ParseSlotDisplay(NewRequestTimeSlot);
            start = slot.Start;
            end = slot.End;
        }

        var req = new SchedulingRequest { TeamName = NewRequestTeam.Trim(), Date = date, StartTime = start, EndTime = end };
        SchedulingRequests.Add(new SchedulingRequestRow(req));
        NewRequestTeam = string.Empty;
        NewRequestDate = null;
        NewRequestTimeSlot = null;
    }

    [RelayCommand]
    private void RemoveRequest()
    {
        if (SelectedRequest is not null)
            SchedulingRequests.Remove(SelectedRequest);
    }

    [RelayCommand]
    private async Task SaveRequests()
    {
        if (CurrentLeague is null) { StatusMessage = "Open or create a league first."; return; }
        CurrentLeague.Constraints = SchedulingRequests.Select(r => r.Request).ToList();
        await _leagueService.SaveLeagueAsync(CurrentLeague);
        StatusMessage = "Scheduling requests saved.";
    }

    [RelayCommand]
    private void ImportRequests()
    {
        var dlg = new OpenFileDialog { Filter = "CSV files|*.csv|All files|*.*", Title = "Import Requests CSV" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var lines = File.ReadAllLines(dlg.FileName).Skip(1);
            foreach (var line in lines)
            {
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                var team = parts[0].Trim();
                if (!DateOnly.TryParse(parts[1].Trim(), out var date)) continue;
                TimeOnly? start = null, end = null;
                if (parts.Length > 2 && TimeOnly.TryParse(parts[2].Trim(), out var s)) start = s;
                if (parts.Length > 3 && TimeOnly.TryParse(parts[3].Trim(), out var e)) end = e;
                var req = new SchedulingRequest { TeamName = team, Date = date, StartTime = start, EndTime = end };
                SchedulingRequests.Add(new SchedulingRequestRow(req));
            }
            StatusMessage = "Requests imported.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Import Error"); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scheduler commands
    // ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task GenerateSchedule()
    {
        if (CurrentLeague is null) { MessageBox.Show("Open a league first.", "Scheduler"); return; }

        CurrentLeague.Divisions = Divisions.ToList();
        CurrentLeague.Constraints = SchedulingRequests.Select(r => r.Request).ToList();

        foreach (var div in CurrentLeague.Divisions.Where(d => !d.IsRoundRobin && d.Teams.Count >= 2))
        {
            if (div.FixedPairings.Count == 0)
                GeneratePairingsForDivision(div);
        }

        // Save relaxation state from the previous run so they survive the fresh generate.
        var savedRelaxations = UnscheduledMatches.ToDictionary(
            r => (r.TeamOne, r.TeamTwo, r.Division),
            r => CaptureRelaxations(r));

        var fixedMatches = CurrentLeague.Matches.Where(m => m.IsFixed).ToList();
        var result = _schedulingService.Generate(CurrentLeague, fixedMatches, ForbiddenSlots.ToList());
        CurrentLeague.Matches = result.ScheduledMatches.ToList();

        // If any previously-unscheduled matches had relaxations set, automatically run the
        // backtrack pass with those relaxations before reporting results.
        if (savedRelaxations.Values.Any(HasAnyRelaxation) && result.UnscheduledMatches.Count > 0)
        {
            var relaxedList = result.UnscheduledMatches
                .Select(x =>
                {
                    var key = (x.Match.TeamOne, x.Match.TeamTwo, x.Match.DivisionName);
                    var relax = savedRelaxations.TryGetValue(key, out var saved) ? saved : new RelaxedConstraints();
                    return (x.Match, relax);
                })
                .ToList();

            var btResult = _schedulingService.BacktrackReschedule(
                CurrentLeague, relaxedList, fixedMatches, ForbiddenSlots.ToList());

            CurrentLeague.Matches = btResult.ScheduledMatches.ToList();
            result = btResult;
        }

        await _leagueService.SaveLeagueAsync(CurrentLeague);
        await _exportService.ExportScheduleAsync(CurrentLeague.Matches, GetScheduleOutputPath(CurrentLeague.Name));
        RenderSchedule();
        RefreshFilterOptions();
        PopulateUnscheduled(result.UnscheduledMatches);

        // Restore relaxation flags on any matches still unscheduled so the user's choices persist.
        foreach (var row in UnscheduledMatches)
        {
            var key = (row.TeamOne, row.TeamTwo, row.Division);
            if (savedRelaxations.TryGetValue(key, out var saved))
                ApplyRelaxations(row, saved);
        }

        StatusMessage = $"Generated {result.ScheduledMatches.Count} matches. {result.UnscheduledMatches.Count} unscheduled.";
    }

    private static RelaxedConstraints CaptureRelaxations(UnscheduledMatchRow r) => new()
    {
        RelaxGroundFairness      = r.RelaxGroundFairness,
        RelaxUmpireFairness      = r.RelaxUmpireFairness,
        RelaxTimeSlotFairness    = r.RelaxTimeSlotFairness,
        RelaxMaxGapRule          = r.RelaxMaxGapRule,
        RelaxOneMatchPerWeekend  = r.RelaxOneMatchPerWeekend,
        RelaxTimeSlotRestriction = r.RelaxTimeSlotRestriction,
        RelaxDateRestriction     = r.RelaxDateRestriction,
        RelaxDiscardedDates      = r.RelaxDiscardedDates
    };

    private static bool HasAnyRelaxation(RelaxedConstraints r) =>
        r.RelaxGroundFairness || r.RelaxUmpireFairness || r.RelaxTimeSlotFairness ||
        r.RelaxMaxGapRule || r.RelaxOneMatchPerWeekend || r.RelaxTimeSlotRestriction ||
        r.RelaxDateRestriction || r.RelaxDiscardedDates;

    private static void ApplyRelaxations(UnscheduledMatchRow row, RelaxedConstraints saved)
    {
        row.RelaxGroundFairness      = saved.RelaxGroundFairness;
        row.RelaxUmpireFairness      = saved.RelaxUmpireFairness;
        row.RelaxTimeSlotFairness    = saved.RelaxTimeSlotFairness;
        row.RelaxMaxGapRule          = saved.RelaxMaxGapRule;
        row.RelaxOneMatchPerWeekend  = saved.RelaxOneMatchPerWeekend;
        row.RelaxTimeSlotRestriction = saved.RelaxTimeSlotRestriction;
        row.RelaxDateRestriction     = saved.RelaxDateRestriction;
        row.RelaxDiscardedDates      = saved.RelaxDiscardedDates;
    }

    [RelayCommand]
    private async Task ExportSchedule()
    {
        if (CurrentLeague is null) return;
        // Always write to a timestamped file in /outputs — never overwrite data folder
        var output = AppPaths.TimestampedExportPath($"schedule_{CurrentLeague.Name}");
        await _exportService.ExportScheduleAsync(CurrentLeague.Matches, output);
        StatusMessage = $"Exported to: {System.IO.Path.GetFileName(output)}";
        MessageBox.Show($"Exported to:\n{output}", "Export Complete");
    }

    [RelayCommand]
    private void ImportMatches()
    {
        var dlg = new OpenFileDialog { Filter = "CSV files|*.csv|All files|*.*", Title = "Import Matches CSV" };
        if (dlg.ShowDialog() != true) return;
        StatusMessage = "Import not yet wired — use Generate Schedule instead.";
        MessageBox.Show("Match import will overlay existing schedule data. Use Generate Schedule to build from scratch.", "Import Matches");
    }

    // ── Filters ──────────────────────────────────────────────────────────────

    partial void OnFilterDivisionChanged(string? value) => ApplyFilters();
    partial void OnFilterTeamChanged(string? value) => ApplyFilters();
    partial void OnFilterGroundChanged(string? value) => ApplyFilters();

    [RelayCommand]
    private void ClearFilters()
    {
        FilterDivision = null;
        FilterTeam = null;
        FilterGround = null;
        ScheduleSearchText = string.Empty;
    }

    private void ApplyFilters()
    {
        FilteredScheduledMatches.Clear();
        var q = ScheduleSearchText?.ToLower() ?? string.Empty;
        foreach (var row in ScheduledMatches)
        {
            if (FilterDivision is not null && FilterDivision != "(All)" && row.DivisionName != FilterDivision) continue;
            if (FilterTeam is not null && FilterTeam != "(All)" && row.TeamOne != FilterTeam && row.TeamTwo != FilterTeam) continue;
            if (FilterGround is not null && FilterGround != "(All)" && row.GroundName != FilterGround) continue;
            if (!string.IsNullOrEmpty(q))
            {
                bool hit = row.TeamOne.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || row.TeamTwo.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || row.DivisionName.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || row.GroundName.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || row.DateDisplay.Contains(q, StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
            }
            FilteredScheduledMatches.Add(row);
        }
        OnPropertyChanged(nameof(ScheduleStats));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshLeagueNames()
    {
        LeagueNames.Clear();
        foreach (var name in _leagueService.GetLeagueNames())
            LeagueNames.Add(name);
    }

    private async Task EnsureSampleLeagueAndLoadAsync()
    {
        const string sample = "SampleLeague";
        if (!LeagueNames.Contains(sample))
            _leagueService.CreateLeague(sample);
        RefreshLeagueNames();
        SelectedLeagueName = LeagueNames.Contains(sample) ? sample : LeagueNames.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(SelectedLeagueName))
        {
            CurrentLeague = await _leagueService.LoadLeagueAsync(SelectedLeagueName);
            if (CurrentLeague is not null) PopulateFormsFromLeague();
        }
    }

    private void PopulateFormsFromLeague()
    {
        if (CurrentLeague is null) { ClearAllForms(); return; }

        // Tournament
        TournamentName = CurrentLeague.Tournament.Name;
        TournamentStartDate = CurrentLeague.Tournament.StartDate.ToDateTime(TimeOnly.MinValue);
        TournamentEndDate = CurrentLeague.Tournament.EndDate.ToDateTime(TimeOnly.MinValue);
        DiscardedDates.Clear();
        foreach (var d in CurrentLeague.Tournament.DiscardedDates)
            DiscardedDates.Add(d.ToString("yyyy-MM-dd"));
        GroundNames.Clear();
        foreach (var g in CurrentLeague.Tournament.Grounds)
            GroundNames.Add(g.Name);
        TimeSlotDisplays.Clear();
        foreach (var s in CurrentLeague.Tournament.TimeSlots)
            TimeSlotDisplays.Add($"{s.Start:HH:mm} - {s.End:HH:mm}");

        // Divisions
        Divisions.Clear();
        foreach (var div in CurrentLeague.Divisions)
            Divisions.Add(div);
        TeamsInSelectedDivision.Clear();
        SelectedDivision = null;

        // Requests
        SchedulingRequests.Clear();
        foreach (var r in CurrentLeague.Constraints)
            SchedulingRequests.Add(new SchedulingRequestRow(r));

        // Schedule
        RenderSchedule();
        RefreshFilterOptions();
        RefreshAllTeamNames();
        RefreshDivisionFilters();
        SyncFilteredRequests();
        SyncFilteredDivisions();

        // Restore unscheduled matches saved from previous session
        UnscheduledMatches.Clear();
        foreach (var m in CurrentLeague.UnscheduledMatches)
            UnscheduledMatches.Add(new UnscheduledMatchRow(m, m.UnscheduledReason ?? "Previously unscheduled"));

        StatusMessage = $"Loaded: {CurrentLeague.Name} — {CurrentLeague.Divisions.Count} divisions, " +
                        $"{CurrentLeague.Matches.Count} matches, {CurrentLeague.UnscheduledMatches.Count} unscheduled";
    }

    private void ClearAllForms()
    {
        TournamentName = string.Empty;
        TournamentStartDate = TournamentEndDate = null;
        DiscardedDates.Clear(); GroundNames.Clear(); TimeSlotDisplays.Clear();
        Divisions.Clear(); TeamsInSelectedDivision.Clear();
        SchedulingRequests.Clear(); ScheduledMatches.Clear(); FilteredScheduledMatches.Clear();
        FilterDivisions.Clear(); FilterTeams.Clear(); FilterGrounds.Clear();
        AllTeamNames.Clear();
        ForbiddenSlots.Clear();
        FilteredRequests.Clear();
        FilteredDivisions.Clear();
        MoveOptions.Clear();
        UnscheduledMoveOptions.Clear();
        StatisticsVM.Clear();
        StatusMessage = "No league loaded.";
    }

    private void RenderSchedule()
    {
        ScheduledMatches.Clear();
        if (CurrentLeague is null) return;
        foreach (var match in CurrentLeague.Matches.OrderBy(m => m.Sequence))
        {
            ScheduledMatches.Add(new ScheduleRowViewModel
            {
                Sequence = match.Sequence,
                DivisionName = match.DivisionName,
                DateDisplay = match.Date?.ToString("MM/dd/yyyy") ?? string.Empty,
                TimeRange = match.Slot is null ? string.Empty : $"{match.Slot.Start:hh\\:mm tt} - {match.Slot.End:hh\\:mm tt}",
                TeamOne = match.TeamOne,
                TeamTwo = match.TeamTwo,
                GroundName = match.Ground?.Name ?? string.Empty,
                UmpireOne = match.UmpireOne ?? string.Empty,
                UmpireTwo = match.UmpireTwo ?? string.Empty,
                IsFixed = match.IsFixed,
                SourceMatch = match
            });
        }
        ApplyFilters();
        OnPropertyChanged(nameof(ScheduleStats));
        StatisticsVM.RefreshStatistics(CurrentLeague?.Matches ?? []);
    }

    private void RefreshAllTeamNames()
    {
        AllTeamNames.Clear();
        foreach (var t in Divisions.SelectMany(d => d.Teams).Select(t => t.Name).Distinct().OrderBy(n => n))
            AllTeamNames.Add(t);
    }

    private void RefreshDivisionFilters()
    {
        FilterDivisions.Clear();
        FilterDivisions.Add("(All)");
        foreach (var d in Divisions.Select(d => d.Name))
            FilterDivisions.Add(d);
    }

    private void RefreshFilterOptions()
    {
        FilterTeams.Clear();
        FilterTeams.Add("(All)");
        foreach (var t in ScheduledMatches.SelectMany(m => new[] { m.TeamOne, m.TeamTwo }).Distinct().OrderBy(n => n))
            FilterTeams.Add(t);

        FilterGrounds.Clear();
        FilterGrounds.Add("(All)");
        foreach (var g in ScheduledMatches.Where(m => !string.IsNullOrEmpty(m.GroundName)).Select(m => m.GroundName).Distinct().OrderBy(n => n))
            FilterGrounds.Add(g);

        FilterDivisions.Clear();
        FilterDivisions.Add("(All)");
        foreach (var d in ScheduledMatches.Select(m => m.DivisionName).Distinct().OrderBy(n => n))
            FilterDivisions.Add(d);
    }


    // ─────────────────────────────────────────────────────────────────────────
    // Forbidden slots commands
    // ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddForbiddenSlot()
    {
        TimeSlot? slot = null;
        if (!string.IsNullOrWhiteSpace(NewForbiddenTimeSlot))
            slot = ParseSlotDisplay(NewForbiddenTimeSlot);

        var fs = new ForbiddenSlot
        {
            Date = NewForbiddenDate.HasValue ? DateOnly.FromDateTime(NewForbiddenDate.Value) : null,
            GroundName = string.IsNullOrWhiteSpace(NewForbiddenGround) ? null : NewForbiddenGround.Trim(),
            TimeSlot = slot,
            Division = string.IsNullOrWhiteSpace(NewForbiddenDivision) ? null : NewForbiddenDivision.Trim()
        };
        ForbiddenSlots.Add(fs);
        NewForbiddenDate = null; NewForbiddenGround = null; NewForbiddenTimeSlot = null; NewForbiddenDivision = null;
    }

    [RelayCommand]
    private void RemoveForbiddenSlot()
    {
        if (SelectedForbiddenSlot is not null)
            ForbiddenSlots.Remove(SelectedForbiddenSlot);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mark/unmark fixed
    // ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleFixedSelected(DataGrid? grid)
    {
        if (grid is null) return;
        var selected = grid.SelectedItems.OfType<ScheduleRowViewModel>().ToList();
        if (!selected.Any()) return;
        bool setTo = !selected.All(r => r.IsFixed);
        foreach (var row in selected)
        {
            row.IsFixed = setTo;
            if (row.SourceMatch is not null) row.SourceMatch.IsFixed = setTo;
        }
        StatusMessage = $"{selected.Count} match(es) marked as {(setTo ? "Fixed" : "Not Fixed")}.";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reschedule (respects fixed matches)
    // ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Reschedule()
    {
        if (CurrentLeague is null) { MessageBox.Show("Open a league first."); return; }

        CurrentLeague.Divisions = Divisions.ToList();
        CurrentLeague.Constraints = SchedulingRequests.Select(r => r.Request).ToList();

        // Non-destructive reschedule: all currently scheduled matches (fixed + non-fixed)
        // stay in their slots as the starting context. Only the unscheduled matches are newly
        // placed. Fixed match umpires are preserved. Non-fixed umpires are reassigned.
        var result = _schedulingService.ReschedulePreservingExisting(
            CurrentLeague, ForbiddenSlots.ToList());

        CurrentLeague.Matches = result.ScheduledMatches.ToList();
        CurrentLeague.UnscheduledMatches = result.UnscheduledMatches.Select(x => x.Match).ToList();
        await _leagueService.SaveLeagueAsync(CurrentLeague);
        await _exportService.ExportScheduleAsync(CurrentLeague.Matches, GetScheduleOutputPath(CurrentLeague.Name));
        RenderSchedule();
        RefreshFilterOptions();
        PopulateUnscheduled(result.UnscheduledMatches);

        int fixedCount = result.ScheduledMatches.Count(m => m.IsFixed);
        StatusMessage = $"Rescheduled: {result.ScheduledMatches.Count} matches ({fixedCount} fixed preserved). " +
                        $"{result.UnscheduledMatches.Count} unscheduled.";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Move match analysis — opens solitaire-style popup
    // ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenMoveAnalyzer(ScheduleRowViewModel? row)
    {
        if (row?.SourceMatch is null || CurrentLeague is null) return;

        var rootVm = new MoveAnalyzerViewModel(
            matchToMove: row.SourceMatch,
            league:      CurrentLeague,
            svc:         _schedulingService,
            forbidden:   ForbiddenSlots.ToList(),
            depth:       0);

        var window = new Views.MoveAnalyzerWindow(
            rootVm,
            owner: System.Windows.Application.Current.MainWindow);

        // MoveAnalyzerWindow.OnCommitAction closes the window with DialogResult=true.
        // We save and re-render here so the grid updates immediately after commit.
        if (window.ShowDialog() == true)
            await FinaliseMove();
    }

    private async Task FinaliseMove()
    {
        if (CurrentLeague is null) return;
        await _leagueService.SaveLeagueAsync(CurrentLeague);
        RenderSchedule();
        StatusMessage = "Move(s) applied and saved.";
    }

    /// <summary>
    /// Opens Move Analyzer for an affected match in the Unscheduled slot-selection panel.
    /// On commit the match is moved in-place; the row is marked resolved so
    /// ScheduleSelectedUnscheduled can proceed.
    ///
    /// The unscheduled match is passed as "virtually placed" additional-fixed context so that
    /// the slot suggestions for the displaced match correctly exclude the target slot (where
    /// the unscheduled match will be placed) and respect the unscheduled match's team
    /// constraints (e.g. one-match-per-weekend for the unscheduled match's teams).
    /// </summary>
    [RelayCommand]
    private void OpenMoveAnalyzerForAffectedMatch(AffectedSlotMatchRow? row)
    {
        if (row is null || CurrentLeague is null) return;
        if (SelectedUnscheduledMatch is null || SelectedUnscheduledMoveOption?.SlotKey is not MoveSlotSuggestion targetSlot)
        {
            // Fallback: open without context if state is unexpectedly missing
            OpenMoveAnalyzerForAffectedMatchCore(row, additionalFixed: null);
            return;
        }

        // Create a virtual copy of the unscheduled match placed at the chosen target slot.
        // Treating it as fixed causes SuggestMoves to exclude that slot from candidates for
        // the displaced match and to count it in weekend-conflict checks for both teams.
        var virtualMatch = new Match
        {
            TournamentName = SelectedUnscheduledMatch.Match.TournamentName,
            DivisionName   = SelectedUnscheduledMatch.Match.DivisionName,
            MatchType      = SelectedUnscheduledMatch.Match.MatchType,
            TeamOne        = SelectedUnscheduledMatch.Match.TeamOne,
            TeamTwo        = SelectedUnscheduledMatch.Match.TeamTwo,
            Date           = targetSlot.Date,
            Slot           = targetSlot.Slot,
            Ground         = targetSlot.Ground,
            IsFixed        = true  // treated as an immovable anchor in SuggestMoves
        };

        OpenMoveAnalyzerForAffectedMatchCore(row, additionalFixed: [virtualMatch]);
    }

    private void OpenMoveAnalyzerForAffectedMatchCore(AffectedSlotMatchRow row, IReadOnlyList<Match>? additionalFixed)
    {
        if (CurrentLeague is null) return;

        var vm = new MoveAnalyzerViewModel(
            matchToMove:     row.Match,
            league:          CurrentLeague,
            svc:             _schedulingService,
            forbidden:       ForbiddenSlots.ToList(),
            depth:           0,
            additionalFixed: additionalFixed);

        var window = new Views.MoveAnalyzerWindow(vm, owner: OwnerWindow ?? System.Windows.Application.Current.MainWindow);

        // MoveAnalyzerWindow commits the move directly to Match objects; DialogResult=true means committed.
        if (window.ShowDialog() == true)
        {
            row.IsManuallyResolved = true;
            // Re-evaluate remaining non-resolved rows in case the move freed their auto-resolve slot.
            RefreshAffectedMatchAutoResolve();
            StatusMessage = $"Match #{row.Match.Sequence} resolved — click 'Schedule Match' when all are resolved.";
        }
    }

    /// <summary>Re-checks auto-resolve status for rows that haven't been manually resolved yet.</summary>
    private void RefreshAffectedMatchAutoResolve()
    {
        if (CurrentLeague is null) return;
        foreach (var row in AffectedMatchesForSelectedSlot.Where(r => !r.IsManuallyResolved && !r.CanAutoResolve))
        {
            var options = _schedulingService.SuggestMoves(CurrentLeague, row.Match, ForbiddenSlots.ToList());
            var best    = options.FirstOrDefault(o => o.AffectedMatchCount == 0);
            if (best is not null)
            {
                // Replace the row with an updated one (CanAutoResolve is init-only, so we swap it)
                int idx = AffectedMatchesForSelectedSlot.IndexOf(row);
                AffectedMatchesForSelectedSlot[idx] = new AffectedSlotMatchRow
                {
                    Match             = row.Match,
                    MatchTitle        = row.MatchTitle,
                    CurrentSlot       = row.CurrentSlot,
                    CanAutoResolve    = true,
                    ResolutionDisplay = $"✅ → {best.Date:MM/dd/yyyy} {best.Slot.Start:HH:mm} @ {best.Ground.Name}",
                    BestSuggestion    = best
                };
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reschedule umpiring
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-assigns umpires for all non-fixed matches using the priority rules:
    ///   1. Adjacent slot on same date + ground (team physically at the ground)
    ///   2. Team with no match in the same calendar week (least travel burden)
    ///   3. Any eligible team by lowest umpire load (fairness fallback)
    /// Hard rules: never same division; never umpiring when playing same slot;
    /// never umpiring at a ground where the team is not playing that day.
    /// Fixed matches retain their existing umpire assignments unchanged.
    /// </summary>
    [RelayCommand]
    private async Task RescheduleUmpiring()
    {
        if (CurrentLeague is null) { StatusMessage = "Open a league first."; return; }

        _schedulingService.RescheduleUmpiring(CurrentLeague);

        await _leagueService.SaveLeagueAsync(CurrentLeague);
        RenderSchedule();
        StatusMessage = $"Umpiring rescheduled for {CurrentLeague.Matches.Count(m => !m.IsFixed)} matches " +
                        $"({CurrentLeague.Matches.Count(m => m.IsFixed)} fixed preserved).";
    }

    /// <summary>
    /// Reshuffles ground assignments across all non-fixed matches to balance usage among
    /// teams, then re-runs umpiring. Fixed matches are completely untouched.
    /// Hard constraint: no two matches share the same ground + date + time slot.
    /// </summary>
    [RelayCommand]
    private async Task RescheduleGroundAndUmpiring()
    {
        if (CurrentLeague is null) { StatusMessage = "Open a league first."; return; }

        _schedulingService.RescheduleGroundAndUmpiring(CurrentLeague);

        await _leagueService.SaveLeagueAsync(CurrentLeague);
        RenderSchedule();
        int nonFixed = CurrentLeague.Matches.Count(m => !m.IsFixed);
        int fixed_   = CurrentLeague.Matches.Count(m => m.IsFixed);
        StatusMessage = $"Ground & umpiring rescheduled for {nonFixed} non-fixed matches " +
                        $"({fixed_} fixed preserved).";
    }

    // Keep old relay commands as no-ops so any stale XAML bindings don't crash
    [RelayCommand] private void AnalyzeMove(ScheduleRowViewModel? _) { }
    [RelayCommand] private void CancelMove() { ShowMovePanel = false; }
    [RelayCommand] private Task ApplyMove(MoveOptionViewModel? _) => Task.CompletedTask;

    // ─────────────────────────────────────────────────────────────────────────
    // Multi-row delete commands (all grids)
    // ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void DeleteSelectedMatches(DataGrid? grid)
    {
        if (grid is null || CurrentLeague is null) return;
        var toDelete = grid.SelectedItems.OfType<ScheduleRowViewModel>().ToList();
        if (!toDelete.Any()) return;
        if (MessageBox.Show($"Delete {toDelete.Count} match(es)?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        foreach (var row in toDelete)
        {
            FilteredScheduledMatches.Remove(row);
            ScheduledMatches.Remove(row);
            if (row.SourceMatch is not null)
                CurrentLeague.Matches.Remove(row.SourceMatch);
        }
        OnPropertyChanged(nameof(ScheduleStats));
    }

    /// <summary>
    /// Move selected scheduled matches back to the Unscheduled grid, clearing their
    /// date/slot/ground so the scheduler can re-try them later.
    /// </summary>
    [RelayCommand]
    private void UnscheduleSelectedMatches(DataGrid? grid)
    {
        if (grid is null || CurrentLeague is null) return;
        var toUnschedule = grid.SelectedItems.OfType<ScheduleRowViewModel>().ToList();
        if (!toUnschedule.Any()) { StatusMessage = "Select one or more matches to unschedule."; return; }

        foreach (var row in toUnschedule)
        {
            FilteredScheduledMatches.Remove(row);
            ScheduledMatches.Remove(row);
            if (row.SourceMatch is null) continue;

            CurrentLeague.Matches.Remove(row.SourceMatch);

            // Clear scheduling assignment so the match is treated as fresh
            row.SourceMatch.Date   = null;
            row.SourceMatch.Slot   = null;
            row.SourceMatch.Ground = null;
            row.SourceMatch.UnscheduledReason = "Manually unscheduled";

            CurrentLeague.UnscheduledMatches.Add(row.SourceMatch);
            UnscheduledMatches.Add(new UnscheduledMatchRow(row.SourceMatch, "Manually unscheduled"));
        }

        OnPropertyChanged(nameof(ScheduleStats));
        StatusMessage = $"{toUnschedule.Count} match(es) moved to Unscheduled.";
    }

    /// <summary>
    /// Persist the current in-memory schedule (both scheduled and unscheduled) to the
    /// league's CSV files without regenerating anything.
    /// </summary>
    [RelayCommand]
    private async Task SaveSchedule()
    {
        if (CurrentLeague is null) { StatusMessage = "Open a league first."; return; }
        CurrentLeague.Matches = ScheduledMatches
            .Where(r => r.SourceMatch is not null)
            .Select(r => r.SourceMatch!)
            .ToList();
        CurrentLeague.UnscheduledMatches = UnscheduledMatches.Select(r => r.Match).ToList();
        await _leagueService.SaveLeagueAsync(CurrentLeague);
        StatusMessage = $"Schedule saved: {CurrentLeague.Matches.Count} scheduled, " +
                        $"{CurrentLeague.UnscheduledMatches.Count} unscheduled.";
    }

    [RelayCommand]
    private void DeleteSelectedRequests(DataGrid? grid)
    {
        if (grid is null) return;
        var toDelete = grid.SelectedItems.OfType<SchedulingRequestRow>().ToList();
        if (!toDelete.Any()) return;
        if (MessageBox.Show($"Delete {toDelete.Count} request(s)?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        foreach (var row in toDelete) SchedulingRequests.Remove(row);
        SyncFilteredRequests();
    }

    [RelayCommand]
    private void DeleteSelectedDivisionTeams(DataGrid? grid)
    {
        if (grid is null || SelectedDivision is null) return;
        var toDelete = grid.SelectedItems.OfType<Team>().ToList();
        if (!toDelete.Any()) return;
        foreach (var t in toDelete) { SelectedDivision.Teams.Remove(t); TeamsInSelectedDivision.Remove(t); }
        RefreshAllTeamNames();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Copy / Paste (schedule grid)
    // ─────────────────────────────────────────────────────────────────────────

    private List<ScheduleRowViewModel> _clipboard = [];

    [RelayCommand]
    private void CopySelectedMatches(DataGrid? grid)
    {
        if (grid is null) return;
        _clipboard = grid.SelectedItems.OfType<ScheduleRowViewModel>().ToList();
        StatusMessage = $"{_clipboard.Count} match(es) copied to clipboard.";
    }

    [RelayCommand]
    private void PasteMatches()
    {
        if (!_clipboard.Any() || CurrentLeague is null) return;
        foreach (var row in _clipboard)
        {
            if (row.SourceMatch is null) continue;
            var clone = new Match
            {
                TournamentName = row.SourceMatch.TournamentName,
                DivisionName   = row.SourceMatch.DivisionName,
                MatchType      = row.SourceMatch.MatchType,
                TeamOne        = row.SourceMatch.TeamOne,
                TeamTwo        = row.SourceMatch.TeamTwo,
                Date           = row.SourceMatch.Date,
                Slot           = row.SourceMatch.Slot,
                Ground         = row.SourceMatch.Ground
            };
            clone.Sequence = (CurrentLeague.Matches.MaxBy(m => m.Sequence)?.Sequence ?? 0) + 1;
            CurrentLeague.Matches.Add(clone);
        }
        RenderSchedule();
        StatusMessage = $"{_clipboard.Count} match(es) pasted.";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Search / filter helpers across all tabs
    // ─────────────────────────────────────────────────────────────────────────

    partial void OnScheduleSearchTextChanged(string value) => ApplyFilters();
    partial void OnRequestSearchTextChanged(string value) => SyncFilteredRequests();
    partial void OnDivisionSearchTextChanged(string value) => SyncFilteredDivisions();

    private void SyncFilteredRequests()
    {
        FilteredRequests.Clear();
        var q = RequestSearchText.ToLower();
        foreach (var r in SchedulingRequests)
        {
            if (string.IsNullOrEmpty(q) ||
                r.TeamName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Date.Contains(q, StringComparison.OrdinalIgnoreCase))
                FilteredRequests.Add(r);
        }
    }

    private void SyncFilteredDivisions()
    {
        FilteredDivisions.Clear();
        var q = DivisionSearchText.ToLower();
        foreach (var d in Divisions)
        {
            if (string.IsNullOrEmpty(q) || d.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                FilteredDivisions.Add(d);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Export filtered data
    // ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ExportFilteredSchedule()
    {
        if (!FilteredScheduledMatches.Any()) { StatusMessage = "No matches to export."; return; }
        var matches = FilteredScheduledMatches
            .Where(r => r.SourceMatch is not null)
            .Select(r => r.SourceMatch!)
            .ToList();
        var output = AppPaths.TimestampedExportPath($"schedule_filtered_{CurrentLeague?.Name ?? "export"}");
        await _exportService.ExportScheduleAsync(matches, output);
        StatusMessage = $"Exported {matches.Count} matches to: {System.IO.Path.GetFileName(output)}";
        MessageBox.Show($"Exported to:\n{output}", "Export Complete");
    }

    [RelayCommand]
    private void ExportFilteredRequests()
    {
        if (!FilteredRequests.Any()) { StatusMessage = "No requests to export."; return; }
        var lines = new List<string> { "TeamName,Date,StartTime,EndTime" };
        lines.AddRange(FilteredRequests.Select(r =>
            $"{r.TeamName},{r.Date},{r.StartTimeDisplay.Replace("(full day)", "")},{r.EndTimeDisplay.Replace("(full day)", "")}"));
        var output = AppPaths.TimestampedExportPath("requests_filtered");
        File.WriteAllLines(output, lines);
        StatusMessage = $"Exported {FilteredRequests.Count} requests to: {System.IO.Path.GetFileName(output)}";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Multi-slot selection combined time range (Requests tab)
    // ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddCombinedSlotConstraint(ListBox? slotListBox)
    {
        if (slotListBox is null || string.IsNullOrWhiteSpace(NewRequestTeam) || NewRequestDate is null) return;
        var selected = slotListBox.SelectedItems.OfType<string>().ToList();
        if (!selected.Any()) { StatusMessage = "Select one or more time slots to block."; return; }

        // Parse all selected slots, find combined min start / max end
        var slots = selected.Select(ParseSlotDisplay).ToList();
        var start = slots.Min(s => s.Start);
        var end   = slots.Max(s => s.End);
        var date  = DateOnly.FromDateTime(NewRequestDate.Value);

        var req = new SchedulingRequest
        {
            TeamName  = NewRequestTeam.Trim(),
            Date      = date,
            StartTime = start,
            EndTime   = end
        };
        SchedulingRequests.Add(new SchedulingRequestRow(req));
        SyncFilteredRequests();
        NewRequestTeam = string.Empty; NewRequestDate = null; NewRequestTimeSlot = null;
        StatusMessage = $"Blocked {start:HH\\:mm}–{end:HH\\:mm} on {date:yyyy-MM-dd} for {req.TeamName}.";
    }



    // ─────────────────────────────────────────────────────────────────────────
    // Unscheduled matches panel helpers + commands (fixes 2-4)
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulateUnscheduled(IReadOnlyList<(Match Match, string Reason)> items)
    {
        UnscheduledMatches.Clear();
        foreach (var (match, reason) in items)
        {
            match.UnscheduledReason = reason;
            UnscheduledMatches.Add(new UnscheduledMatchRow(match, reason));
        }
        // Keep League model in sync so SaveLeagueAsync persists unscheduled matches
        if (CurrentLeague is not null)
            CurrentLeague.UnscheduledMatches = items.Select(x => x.Match).ToList();
    }

    /// <summary>Manually push a single unscheduled match to the schedule using user-specified slot.</summary>
    [RelayCommand]
    private async Task ManuallyScheduleMatch(UnscheduledMatchRow? row)
    {
        if (row is null || CurrentLeague is null) return;
        if (!DateOnly.TryParse(row.ManualDate, out var date))
        { StatusMessage = "Enter a valid date (yyyy-MM-dd) in the Date field for this match."; return; }

        TimeSlot? slot = null;
        if (!string.IsNullOrWhiteSpace(row.ManualTimeSlot))
            slot = ParseSlotDisplay(row.ManualTimeSlot);

        Ground? ground = null;
        if (!string.IsNullOrWhiteSpace(row.ManualGround))
            ground = new Ground { Name = row.ManualGround.Trim() };
        else if (CurrentLeague.Tournament.Grounds.Any())
            ground = CurrentLeague.Tournament.Grounds.First();

        // Guard: never overwrite a fixed match's slot.
        var effectiveSlot = slot ?? CurrentLeague.Tournament.TimeSlots.FirstOrDefault();
        var fixedClash = FindFixedClash(CurrentLeague, date, effectiveSlot, ground, row.Match);
        if (fixedClash is not null)
        {
            StatusMessage = $"Cannot schedule here — fixed match #{fixedClash.Sequence} " +
                            $"({fixedClash.TeamOne} vs {fixedClash.TeamTwo}) already occupies this slot.";
            return;
        }

        row.Match.Date   = date;
        row.Match.Slot   = effectiveSlot;
        row.Match.Ground = ground;
        if (!string.IsNullOrWhiteSpace(row.ManualUmpire))
            row.Match.UmpireOne = row.ManualUmpire.Trim();
        row.Match.Sequence = (CurrentLeague.Matches.MaxBy(m => m.Sequence)?.Sequence ?? 0) + 1;

        CurrentLeague.Matches.Add(row.Match);
        UnscheduledMatches.Remove(row);
        CurrentLeague.UnscheduledMatches = UnscheduledMatches.Select(r => r.Match).ToList();
        if (SelectedUnscheduledMatch == row) SelectedUnscheduledMatch = null;
        await _leagueService.SaveLeagueAsync(CurrentLeague);
        RenderSchedule();
        // Update move analysis if panel is open (slot matrix changed)
        if (ShowMovePanel && MatchToMove is not null)
            AnalyzeMove(MatchToMove);
        StatusMessage = $"Match '{row.TeamOne} vs {row.TeamTwo}' manually scheduled on {date:MM/dd/yyyy}.";
    }

    /// <summary>
    /// Schedule the currently selected unscheduled match using the top-level
    /// manual-input panel (Date / Slot / Ground / Umpire fields).
    /// </summary>
    [RelayCommand]
    private async Task ScheduleSelectedUnscheduled()
    {
        if (SelectedUnscheduledMatch is null || CurrentLeague is null)
        { StatusMessage = "Select a match from the Unscheduled grid first."; return; }

        if (string.IsNullOrWhiteSpace(ManualScheduleDate))
        { StatusMessage = "Enter a date (yyyy-MM-dd) in the Manual Date field."; return; }

        if (!DateOnly.TryParse(ManualScheduleDate, out var date))
        { StatusMessage = $"'{ManualScheduleDate}' is not a valid date — use yyyy-MM-dd format."; return; }

        var row = SelectedUnscheduledMatch;

        TimeSlot? slot = null;
        if (!string.IsNullOrWhiteSpace(ManualScheduleTimeSlot))
            slot = ParseSlotDisplay(ManualScheduleTimeSlot);

        Ground? ground = null;
        if (!string.IsNullOrWhiteSpace(ManualScheduleGround))
            ground = new Ground { Name = ManualScheduleGround.Trim() };
        else if (CurrentLeague.Tournament.Grounds.Any())
            ground = CurrentLeague.Tournament.Grounds.First();

        // Guard: never overwrite a fixed match's slot.
        var effectiveSlot = slot ?? CurrentLeague.Tournament.TimeSlots.FirstOrDefault();
        var fixedClash = FindFixedClash(CurrentLeague, date, effectiveSlot, ground, row.Match);
        if (fixedClash is not null)
        {
            StatusMessage = $"Cannot schedule here — fixed match #{fixedClash.Sequence} " +
                            $"({fixedClash.TeamOne} vs {fixedClash.TeamTwo}) already occupies this slot.";
            return;
        }

        // ── Resolve affected matches before placing the unscheduled match ──────
        if (AffectedMatchesForSelectedSlot.Count > 0)
        {
            var unresolved = AffectedMatchesForSelectedSlot.Where(r => !r.IsResolved).ToList();
            if (unresolved.Count > 0)
            {
                StatusMessage = $"⚠ {unresolved.Count} affected match(es) still need resolution. " +
                                "Use 'Resolve' on each orange row, or select a different slot.";
                return;
            }

            // Apply auto-resolutions for affected matches that the engine can handle directly.
            foreach (var affected in AffectedMatchesForSelectedSlot.Where(r => r.CanAutoResolve && !r.IsManuallyResolved))
            {
                if (affected.BestSuggestion is not null)
                {
                    affected.Match.Date   = affected.BestSuggestion.Date;
                    affected.Match.Slot   = affected.BestSuggestion.Slot;
                    affected.Match.Ground = affected.BestSuggestion.Ground;
                }
            }
        }

        row.Match.Date   = date;
        row.Match.Slot   = effectiveSlot;
        row.Match.Ground = ground;
        if (!string.IsNullOrWhiteSpace(ManualScheduleUmpire))
            row.Match.UmpireOne = ManualScheduleUmpire.Trim();
        row.Match.Sequence = (CurrentLeague.Matches.MaxBy(m => m.Sequence)?.Sequence ?? 0) + 1;

        CurrentLeague.Matches.Add(row.Match);
        UnscheduledMatches.Remove(row);
        CurrentLeague.UnscheduledMatches = UnscheduledMatches.Select(r => r.Match).ToList();
        SelectedUnscheduledMatch = null;
        SelectedUnscheduledMoveOption = null;
        AffectedMatchesForSelectedSlot.Clear();
        OnPropertyChanged(nameof(HasAffectedMatches));

        // Clear panel inputs ready for next match
        ManualScheduleDate = string.Empty;
        ManualScheduleTimeSlot = null;
        ManualScheduleGround = null;
        ManualScheduleUmpire = null;

        await _leagueService.SaveLeagueAsync(CurrentLeague);
        RenderSchedule();
        // Update move analysis panel slot matrix after the change
        if (ShowMovePanel && MatchToMove is not null)
            AnalyzeMove(MatchToMove);
        StatusMessage = $"Match '{row.TeamOne} vs {row.TeamTwo}' scheduled on {date:MM/dd/yyyy}.";
    }

    // Track the row we're subscribed to so we can unsubscribe when selection changes.
    private UnscheduledMatchRow? _subscribedUnscheduledRow;

    /// <summary>
    /// When the selected unscheduled match changes, refresh the available slot analysis.
    /// Also subscribe to constraint-relaxation checkbox changes on the row so the list
    /// updates live when the user ticks/unticks a flag.
    /// </summary>
    partial void OnSelectedUnscheduledMatchChanged(UnscheduledMatchRow? value)
    {
        if (_subscribedUnscheduledRow is not null)
            _subscribedUnscheduledRow.PropertyChanged -= OnUnscheduledRowConstraintChanged;

        _subscribedUnscheduledRow = value;

        if (value is not null)
            value.PropertyChanged += OnUnscheduledRowConstraintChanged;

        // Clear slot selection and affected matches when switching unscheduled rows
        SelectedUnscheduledMoveOption = null;
        AffectedMatchesForSelectedSlot.Clear();
        OnPropertyChanged(nameof(HasAffectedMatches));

        RefreshUnscheduledSuggestions(value);
    }

    /// <summary>
    /// When the user selects a slot in the Available Slots Analysis grid, compute which
    /// already-scheduled matches would be displaced and show them in the affected-matches grid.
    /// Green rows can be auto-resolved by the engine; orange rows need the Move Analyzer.
    /// </summary>
    partial void OnSelectedUnscheduledMoveOptionChanged(MoveOptionViewModel? value)
    {
        AffectedMatchesForSelectedSlot.Clear();
        OnPropertyChanged(nameof(HasAffectedMatches));
        if (value?.SlotKey is not MoveSlotSuggestion suggestion || CurrentLeague is null) return;

        foreach (var m in suggestion.AffectedMatchList)
        {
            var options = _schedulingService.SuggestMoves(CurrentLeague, m, ForbiddenSlots.ToList());
            var best    = options.FirstOrDefault(o => o.AffectedMatchCount == 0);
            AffectedMatchesForSelectedSlot.Add(new AffectedSlotMatchRow
            {
                Match             = m,
                MatchTitle        = $"#{m.Sequence}  {m.TeamOne} vs {m.TeamTwo}",
                CurrentSlot       = $"{m.Date:MM/dd/yyyy} {m.Slot?.Start:HH:mm} @ {m.Ground?.Name}",
                CanAutoResolve    = best is not null,
                ResolutionDisplay = best is null
                    ? "⚠ No free slot — click Resolve to open Move Analyzer"
                    : $"✅ → {best.Date:MM/dd/yyyy} {best.Slot.Start:HH:mm} @ {best.Ground.Name}",
                BestSuggestion    = best
            });
        }
        OnPropertyChanged(nameof(HasAffectedMatches));
    }

    private void OnUnscheduledRowConstraintChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName?.StartsWith("Relax") == true)
            RefreshUnscheduledSuggestions(SelectedUnscheduledMatch);
    }

    /// <summary>
    /// Recomputes available slot suggestions for the given unscheduled row using its
    /// current relaxation flags.  Shows all tournament slots (including those occupied
    /// by non-fixed matches) — same universe as Move Analyzer — filtered by whichever
    /// constraints are NOT yet relaxed by the user.
    /// </summary>
    private void RefreshUnscheduledSuggestions(UnscheduledMatchRow? row)
    {
        UnscheduledMoveOptions.Clear();
        if (row is null || CurrentLeague is null) return;

        var relaxed = new RelaxedConstraints
        {
            RelaxGroundFairness      = row.RelaxGroundFairness,
            RelaxUmpireFairness      = row.RelaxUmpireFairness,
            RelaxTimeSlotFairness    = row.RelaxTimeSlotFairness,
            RelaxMaxGapRule          = row.RelaxMaxGapRule,
            RelaxOneMatchPerWeekend  = row.RelaxOneMatchPerWeekend,
            RelaxTimeSlotRestriction = row.RelaxTimeSlotRestriction,
            RelaxDateRestriction     = row.RelaxDateRestriction,
            RelaxDiscardedDates      = row.RelaxDiscardedDates
        };

        var suggestions = _schedulingService.SuggestMoves(CurrentLeague, row.Match, ForbiddenSlots.ToList(), relaxed);
        foreach (var s in suggestions)
        {
            UnscheduledMoveOptions.Add(new MoveOptionViewModel
            {
                DateDisplay     = s.Date.ToString("ddd MM/dd/yyyy"),
                TimeRange       = $"{s.Slot.Start:hh\\:mm tt} - {s.Slot.End:hh\\:mm tt}",
                GroundName      = s.Ground.Name,
                AffectedMatches = s.AffectedMatchCount,
                FairnessScore   = s.FairnessScore,
                IsRecommended   = s.IsRecommended,
                SlotKey         = s
            });
        }
    }

    /// <summary>Apply a slot from the unscheduled analysis grid into the manual input fields.</summary>
    [RelayCommand]
    private void ApplyUnscheduledSlot(MoveOptionViewModel? option)
    {
        if (option is null) return;
        var s = (MoveSlotSuggestion)option.SlotKey!;
        ManualScheduleDate     = s.Date.ToString("yyyy-MM-dd");
        ManualScheduleTimeSlot = $"{s.Slot.Start:HH:mm} - {s.Slot.End:HH:mm}";
        ManualScheduleGround   = s.Ground.Name;
    }

    /// <summary>
    /// Backtrack reschedule: relax constraints on checked unscheduled matches,
    /// then attempt to fit them into the slot matrix by re-evaluating non-fixed matches.
    /// </summary>
    [RelayCommand]
    private async Task BacktrackReschedule()
    {
        if (CurrentLeague is null || !UnscheduledMatches.Any()) return;

        var relaxedMatches = UnscheduledMatches.ToList();
        var fixedMatches   = CurrentLeague.Matches.Where(m => m.IsFixed).ToList();
        var movableMatches = CurrentLeague.Matches.Where(m => !m.IsFixed).ToList();

        var result = _schedulingService.BacktrackReschedule(
            CurrentLeague,
            relaxedMatches.Select(r => (r.Match, new RelaxedConstraints
            {
                RelaxGroundFairness      = r.RelaxGroundFairness,
                RelaxUmpireFairness      = r.RelaxUmpireFairness,
                RelaxTimeSlotFairness    = r.RelaxTimeSlotFairness,
                RelaxMaxGapRule          = r.RelaxMaxGapRule,
                RelaxOneMatchPerWeekend  = r.RelaxOneMatchPerWeekend,
                RelaxTimeSlotRestriction = r.RelaxTimeSlotRestriction,
                RelaxDateRestriction     = r.RelaxDateRestriction,
                RelaxDiscardedDates      = r.RelaxDiscardedDates
            })).ToList(),
            fixedMatches,
            ForbiddenSlots.ToList());

        CurrentLeague.Matches = result.ScheduledMatches.ToList();
        await _leagueService.SaveLeagueAsync(CurrentLeague);
        await _exportService.ExportScheduleAsync(CurrentLeague.Matches, GetScheduleOutputPath(CurrentLeague.Name));
        RenderSchedule();
        RefreshFilterOptions();
        PopulateUnscheduled(result.UnscheduledMatches);
        StatusMessage = $"Backtrack complete: {result.ScheduledMatches.Count} scheduled, {result.UnscheduledMatches.Count} still unscheduled.";
    }

    [RelayCommand]
    private void RemoveUnscheduledMatch()
    {
        if (SelectedUnscheduledMatch is not null)
            UnscheduledMatches.Remove(SelectedUnscheduledMatch);
    }

    [RelayCommand]
    private void RelaxAllConstraints(DataGrid? grid)
    {
        var rows = grid is not null
            ? grid.SelectedItems.OfType<UnscheduledMatchRow>().ToList()
            : UnscheduledMatches.ToList();
        foreach (var r in rows) r.RelaxAll();
        StatusMessage = $"All constraints relaxed for {rows.Count} match(es).";
    }

    [RelayCommand]
    private void RelaxNoConstraints(DataGrid? grid)
    {
        var rows = grid is not null
            ? grid.SelectedItems.OfType<UnscheduledMatchRow>().ToList()
            : UnscheduledMatches.ToList();
        foreach (var r in rows) r.RelaxNone();
        StatusMessage = $"All constraints restored for {rows.Count} match(es).";
    }

    [RelayCommand]
    private void ExportUnscheduled()
    {
        if (!UnscheduledMatches.Any()) { StatusMessage = "No unscheduled matches."; return; }
        var output = AppPaths.TimestampedExportPath("unscheduled_matches");
        var lines = new List<string> { "Division,TeamOne,TeamTwo,Reason" };
        lines.AddRange(UnscheduledMatches.Select(r => $"{r.Division},{r.TeamOne},{r.TeamTwo},{r.Reason.Replace(",",";")}"));
        File.WriteAllLines(output, lines);
        StatusMessage = $"Exported {UnscheduledMatches.Count} unscheduled matches to {Path.GetFileName(output)}.";
    }

    private string GetScheduleOutputPath(string name) => Path.Combine(_leaguesRoot, name, "schedule.csv");

    private static TimeSlot ParseSlotDisplay(string display)
    {
        var parts = display.Split('-', StringSplitOptions.TrimEntries);
        return new TimeSlot { Start = TimeOnly.Parse(parts[0]), End = TimeOnly.Parse(parts[1]) };
    }

    private static Match? FindFixedClash(League league, DateOnly date, TimeSlot? slot, Ground? ground, Match excluding) =>
        league.Matches.FirstOrDefault(m =>
            m.IsFixed && m != excluding &&
            m.Date == date &&
            m.Slot?.Start == slot?.Start &&
            string.Equals(m.Ground?.Name, ground?.Name, StringComparison.OrdinalIgnoreCase));
}

// ── View models for DataGrid rows ────────────────────────────────────────────

public sealed class ScheduleRowViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int Sequence { get; set; }
    public string DivisionName { get; set; } = string.Empty;
    public string DateDisplay { get; set; } = string.Empty;
    public string TimeRange { get; set; } = string.Empty;
    public string TeamOne { get; set; } = string.Empty;
    public string TeamTwo { get; set; } = string.Empty;

    // Editable inline — changes propagate to SourceMatch
    private string _groundName = string.Empty;
    public string GroundName
    {
        get => _groundName;
        set
        {
            _groundName = value;
            OnPropertyChanged();
            if (SourceMatch is not null)
                SourceMatch.Ground = string.IsNullOrWhiteSpace(value) ? null : new Ground { Name = value.Trim() };
        }
    }

    private string _umpireOne = string.Empty;
    public string UmpireOne
    {
        get => _umpireOne;
        set
        {
            _umpireOne = value;
            OnPropertyChanged();
            if (SourceMatch is not null) SourceMatch.UmpireOne = value;
        }
    }

    public string UmpireTwo { get; set; } = string.Empty;

    private bool _isFixed;
    public bool IsFixed
    {
        get => _isFixed;
        set
        {
            _isFixed = value;
            OnPropertyChanged();
            // Propagate to backing match immediately so checkbox click takes effect
            if (SourceMatch is not null) SourceMatch.IsFixed = value;
        }
    }

    private bool _hasConflict;
    public bool HasConflict
    {
        get => _hasConflict;
        set { _hasConflict = value; OnPropertyChanged(); }
    }

    // Source match kept for round-trip operations
    public Match? SourceMatch { get; set; }
}

public sealed class SchedulingRequestRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public SchedulingRequest Request { get; }

    public string TeamName
    {
        get => Request.TeamName;
        set { Request.TeamName = value; OnPropertyChanged(); }
    }

    public string Date => Request.Date.ToString("yyyy-MM-dd");
    public string StartTimeDisplay => Request.StartTime?.ToString("HH:mm") ?? "(full day)";
    public string EndTimeDisplay => Request.EndTime?.ToString("HH:mm") ?? "(full day)";
    public string BlockType => Request.IsFullDayBlock ? "Full Day Block" : "Partial Time Block";

    public SchedulingRequestRow(SchedulingRequest req) => Request = req;
}

// ── MoveOptionViewModel ───────────────────────────────────────────────────────
public sealed class MoveOptionViewModel
{
    public string DateDisplay    { get; init; } = string.Empty;
    public string TimeRange      { get; init; } = string.Empty;
    public string GroundName     { get; init; } = string.Empty;
    public int    AffectedMatches { get; init; }
    public double FairnessScore  { get; init; }
    public bool   IsRecommended  { get; init; }
    public object? SlotKey       { get; init; }

    public string Summary =>
        $"{(IsRecommended ? "★ " : "")}{DateDisplay}  {TimeRange}  @ {GroundName}" +
        $"  |  {AffectedMatches} affected  |  Fairness: {FairnessScore:F1}";
}

// ── MoveSlotSuggestion ────────────────────────────────────────────────────────
public sealed class MoveSlotSuggestion
{
    public required DateOnly Date         { get; init; }
    public required TimeSlot Slot         { get; init; }
    public required Ground   Ground       { get; init; }
    public int    AffectedMatchCount      { get; init; }
    public double FairnessScore           { get; init; }
    public bool   IsRecommended           { get; init; }
    /// <summary>Matches occupying this slot that would need to move if this slot is chosen.</summary>
    public List<Match> AffectedMatchList  { get; init; } = [];
}

// ── UnscheduledMatchRow ───────────────────────────────────────────────────────
/// <summary>
/// Represents one unscheduled match pair in the UI, with per-constraint relaxation
/// flags the user can independently toggle before running Backtrack Reschedule.
/// </summary>
public sealed class UnscheduledMatchRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public Match  Match    { get; }
    public string TeamOne  => Match.TeamOne;
    public string TeamTwo  => Match.TeamTwo;
    public string Division => Match.DivisionName;
    public string Reason   { get; set; } = string.Empty;

    // ── FAIRNESS constraints ──────────────────────────────────────────────────
    private bool _relaxGroundFairness;
    /// <summary>Allow assigning to an already over-used ground.</summary>
    public bool RelaxGroundFairness
    { get => _relaxGroundFairness; set { _relaxGroundFairness = value; OnPropertyChanged(); } }

    private bool _relaxUmpireFairness;
    /// <summary>Assign umpires regardless of current umpiring-duty load.</summary>
    public bool RelaxUmpireFairness
    { get => _relaxUmpireFairness; set { _relaxUmpireFairness = value; OnPropertyChanged(); } }

    private bool _relaxTimeSlotFairness;
    /// <summary>Allow using an already over-used time slot.</summary>
    public bool RelaxTimeSlotFairness
    { get => _relaxTimeSlotFairness; set { _relaxTimeSlotFairness = value; OnPropertyChanged(); } }

    // ── RHYTHM constraints ────────────────────────────────────────────────────
    private bool _relaxMaxGapRule;
    /// <summary>Allow a team to sit out more than 2 consecutive weekends.</summary>
    public bool RelaxMaxGapRule
    { get => _relaxMaxGapRule; set { _relaxMaxGapRule = value; OnPropertyChanged(); } }

    private bool _relaxOneMatchPerWeekend;
    /// <summary>Allow a team to play more than one match in the same weekend.</summary>
    public bool RelaxOneMatchPerWeekend
    { get => _relaxOneMatchPerWeekend; set { _relaxOneMatchPerWeekend = value; OnPropertyChanged(); } }

    // ── DATE / TIME RESTRICTION constraints ──────────────────────────────────
    private bool _relaxTimeSlotRestriction;
    /// <summary>Ignore team-specific time-slot unavailability (partial-time blocks).</summary>
    public bool RelaxTimeSlotRestriction
    { get => _relaxTimeSlotRestriction; set { _relaxTimeSlotRestriction = value; OnPropertyChanged(); } }

    private bool _relaxDateRestriction;
    /// <summary>Ignore team-specific full-day date unavailability blocks.</summary>
    public bool RelaxDateRestriction
    { get => _relaxDateRestriction; set { _relaxDateRestriction = value; OnPropertyChanged(); } }

    // ── TOURNAMENT STRUCTURE constraint ───────────────────────────────────────
    private bool _relaxDiscardedDates;
    /// <summary>Allow scheduling on tournament blackout / discarded dates.</summary>
    public bool RelaxDiscardedDates
    { get => _relaxDiscardedDates; set { _relaxDiscardedDates = value; OnPropertyChanged(); } }

    // ── Convenience: relax all at once ───────────────────────────────────────
    public void RelaxAll()
    {
        RelaxGroundFairness = RelaxUmpireFairness = RelaxTimeSlotFairness =
        RelaxMaxGapRule = RelaxOneMatchPerWeekend =
        RelaxTimeSlotRestriction = RelaxDateRestriction = RelaxDiscardedDates = true;
    }
    public void RelaxNone()
    {
        RelaxGroundFairness = RelaxUmpireFairness = RelaxTimeSlotFairness =
        RelaxMaxGapRule = RelaxOneMatchPerWeekend =
        RelaxTimeSlotRestriction = RelaxDateRestriction = RelaxDiscardedDates = false;
    }

    // ── Manual slot override (for direct scheduling) ─────────────────────────
    private string _manualDate = string.Empty;
    public string ManualDate
    { get => _manualDate; set { _manualDate = value; OnPropertyChanged(); } }

    private string? _manualTimeSlot;
    public string? ManualTimeSlot
    { get => _manualTimeSlot; set { _manualTimeSlot = value; OnPropertyChanged(); } }

    private string? _manualGround;
    public string? ManualGround
    { get => _manualGround; set { _manualGround = value; OnPropertyChanged(); } }

    private string? _manualUmpire;
    public string? ManualUmpire
    { get => _manualUmpire; set { _manualUmpire = value; OnPropertyChanged(); } }

    public UnscheduledMatchRow(Match match, string reason)
    {
        Match  = match;
        Reason = reason;
    }
}

// ── PairingRow — displayed in the fixed-mode pairings DataGrid ────────────────
public sealed class PairingRow
{
    public int    Index { get; init; }
    public string TeamA { get; init; } = string.Empty;
    public string TeamB { get; init; } = string.Empty;
}

// ── AffectedSlotMatchRow — shown when an unscheduled slot has conflicts ────────
/// <summary>
/// One row in the "Affected Matches" grid that appears when the user selects a
/// slot in the Available Slots Analysis that is occupied by a scheduled match.
/// Green rows (CanAutoResolve=true) will be moved automatically when "Schedule Match"
/// is clicked. Orange rows need manual resolution via the embedded Move Analyzer.
/// </summary>
public sealed class AffectedSlotMatchRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public Match  Match             { get; init; } = null!;
    public string MatchTitle        { get; init; } = string.Empty;
    public string CurrentSlot       { get; init; } = string.Empty;
    public bool   CanAutoResolve    { get; init; }
    public string ResolutionDisplay { get; init; } = string.Empty;
    public MoveSlotSuggestion? BestSuggestion { get; init; }

    private bool _isManuallyResolved;
    public bool IsManuallyResolved
    {
        get => _isManuallyResolved;
        set { _isManuallyResolved = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsResolved)); }
    }

    /// <summary>True when this affected match is handled (auto or manually resolved).</summary>
    public bool IsResolved => CanAutoResolve || IsManuallyResolved;
}
