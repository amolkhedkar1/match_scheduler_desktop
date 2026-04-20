using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CricketScheduler.App.Models;
using CricketScheduler.App.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

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
    [ObservableProperty] private string tournamentStartDate = string.Empty;
    [ObservableProperty] private string tournamentEndDate = string.Empty;
    [ObservableProperty] private string newDiscardedDate = string.Empty;
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

    // ── Request form ─────────────────────────────────────────────────────────
    [ObservableProperty] private string newRequestTeam = string.Empty;
    [ObservableProperty] private string newRequestDate = string.Empty;
    [ObservableProperty] private string newRequestStartTime = string.Empty;
    [ObservableProperty] private string newRequestEndTime = string.Empty;
    [ObservableProperty] private SchedulingRequestRow? selectedRequest;

    // ── Scheduler filters ────────────────────────────────────────────────────
    [ObservableProperty] private string? filterDivision;
    [ObservableProperty] private string? filterTeam;
    [ObservableProperty] private string? filterGround;
    [ObservableProperty] private ScheduleRowViewModel? selectedMatch;

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
        if (!DateOnly.TryParse(NewDiscardedDate, out var d)) { StatusMessage = "Invalid date format."; return; }
        var s = d.ToString("yyyy-MM-dd");
        if (!DiscardedDates.Contains(s)) DiscardedDates.Add(s);
        NewDiscardedDate = string.Empty;
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
        if (!DateOnly.TryParse(TournamentStartDate, out var start) ||
            !DateOnly.TryParse(TournamentEndDate, out var end))
        { StatusMessage = "Invalid start or end date."; return; }

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

    partial void OnSelectedDivisionChanged(Division? value)
    {
        OnPropertyChanged(nameof(SelectedDivisionName));
        TeamsInSelectedDivision.Clear();
        if (value is null) return;
        foreach (var t in value.Teams)
            TeamsInSelectedDivision.Add(t);
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

    // ─────────────────────────────────────────────────────────────────────────
    // Scheduling Request commands
    // ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddRequest()
    {
        if (string.IsNullOrWhiteSpace(NewRequestTeam) || string.IsNullOrWhiteSpace(NewRequestDate))
        { StatusMessage = "Team and Date are required."; return; }
        if (!DateOnly.TryParse(NewRequestDate, out var date))
        { StatusMessage = "Invalid date format (yyyy-MM-dd)."; return; }

        TimeOnly? start = null, end = null;
        if (!string.IsNullOrWhiteSpace(NewRequestStartTime) && TimeOnly.TryParse(NewRequestStartTime, out var s)) start = s;
        if (!string.IsNullOrWhiteSpace(NewRequestEndTime) && TimeOnly.TryParse(NewRequestEndTime, out var e)) end = e;

        var req = new SchedulingRequest { TeamName = NewRequestTeam.Trim(), Date = date, StartTime = start, EndTime = end };
        SchedulingRequests.Add(new SchedulingRequestRow(req));
        NewRequestTeam = NewRequestDate = NewRequestStartTime = NewRequestEndTime = string.Empty;
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

        // Sync in-memory edits back to league before scheduling
        CurrentLeague.Divisions = Divisions.ToList();
        CurrentLeague.Constraints = SchedulingRequests.Select(r => r.Request).ToList();

        var result = _schedulingService.Generate(CurrentLeague);
        CurrentLeague.Matches = result.ScheduledMatches.ToList();
        await _leagueService.SaveLeagueAsync(CurrentLeague);
        await _exportService.ExportScheduleAsync(CurrentLeague.Matches, GetScheduleOutputPath(CurrentLeague.Name));
        RenderSchedule();
        RefreshFilterOptions();

        StatusMessage = $"Generated {result.ScheduledMatches.Count} matches. {result.UnscheduledMatches.Count} unscheduled.";

        if (result.UnscheduledMatches.Count > 0)
        {
            var reasons = string.Join(Environment.NewLine,
                result.UnscheduledMatches.Take(8).Select(x => $"• {x.Match.TeamOne} vs {x.Match.TeamTwo}: {x.Reason}"));
            MessageBox.Show(
                $"{result.UnscheduledMatches.Count} match(es) could not be scheduled:\n\n{reasons}\n\nWhich constraint should be relaxed?\n" +
                "- Team availability\n- One match per weekend\n- Max 2 consecutive no-match weekends\n- Ground fairness\n- Time slot fairness",
                "Constraint Relaxation Required");
        }
    }

    [RelayCommand]
    private async Task ExportSchedule()
    {
        if (CurrentLeague is null) return;
        var output = GetScheduleOutputPath(CurrentLeague.Name);
        await _exportService.ExportScheduleAsync(CurrentLeague.Matches, output);
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
    }

    private void ApplyFilters()
    {
        FilteredScheduledMatches.Clear();
        foreach (var row in ScheduledMatches)
        {
            if (FilterDivision is not null && FilterDivision != "(All)" && row.DivisionName != FilterDivision) continue;
            if (FilterTeam is not null && FilterTeam != "(All)" && row.TeamOne != FilterTeam && row.TeamTwo != FilterTeam) continue;
            if (FilterGround is not null && FilterGround != "(All)" && row.GroundName != FilterGround) continue;
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
        TournamentStartDate = CurrentLeague.Tournament.StartDate.ToString("yyyy-MM-dd");
        TournamentEndDate = CurrentLeague.Tournament.EndDate.ToString("yyyy-MM-dd");
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

        StatusMessage = $"Loaded: {CurrentLeague.Name} — {CurrentLeague.Divisions.Count} divisions, {CurrentLeague.Matches.Count} matches";
    }

    private void ClearAllForms()
    {
        TournamentName = TournamentStartDate = TournamentEndDate = string.Empty;
        DiscardedDates.Clear(); GroundNames.Clear(); TimeSlotDisplays.Clear();
        Divisions.Clear(); TeamsInSelectedDivision.Clear();
        SchedulingRequests.Clear(); ScheduledMatches.Clear(); FilteredScheduledMatches.Clear();
        FilterDivisions.Clear(); FilterTeams.Clear(); FilterGrounds.Clear();
        AllTeamNames.Clear();
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
                IsFixed = match.IsFixed
            });
        }
        ApplyFilters();
        OnPropertyChanged(nameof(ScheduleStats));
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

    private string GetScheduleOutputPath(string name) => Path.Combine(_leaguesRoot, name, "schedule.csv");

    private static TimeSlot ParseSlotDisplay(string display)
    {
        var parts = display.Split('-', StringSplitOptions.TrimEntries);
        return new TimeSlot { Start = TimeOnly.Parse(parts[0]), End = TimeOnly.Parse(parts[1]) };
    }
}

// ── View models for DataGrid rows ────────────────────────────────────────────

public sealed class ScheduleRowViewModel
{
    public int Sequence { get; init; }
    public string DivisionName { get; init; } = string.Empty;
    public string DateDisplay { get; init; } = string.Empty;
    public string TimeRange { get; init; } = string.Empty;
    public string TeamOne { get; init; } = string.Empty;
    public string TeamTwo { get; init; } = string.Empty;
    public string GroundName { get; init; } = string.Empty;
    public string UmpireOne { get; init; } = string.Empty;
    public string UmpireTwo { get; init; } = string.Empty;
    public bool IsFixed { get; init; }
    public bool HasConflict { get; init; }
}

public sealed class SchedulingRequestRow
{
    public SchedulingRequest Request { get; }
    public string TeamName => Request.TeamName;
    public string Date => Request.Date.ToString("yyyy-MM-dd");
    public string StartTimeDisplay => Request.StartTime?.ToString("HH:mm") ?? "(full day)";
    public string EndTimeDisplay => Request.EndTime?.ToString("HH:mm") ?? "(full day)";
    public string BlockType => Request.IsFullDayBlock ? "Full Day Block" : "Partial Time Block";

    public SchedulingRequestRow(SchedulingRequest req) => Request = req;
}
