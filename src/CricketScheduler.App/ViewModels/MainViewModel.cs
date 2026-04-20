using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CricketScheduler.App.Models;
using CricketScheduler.App.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace CricketScheduler.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly LeagueService _leagueService;
    private readonly SchedulingService _schedulingService;
    private readonly ExportService _exportService;
    private readonly string _leaguesRoot;

    [ObservableProperty]
    private string? selectedLeagueName;

    [ObservableProperty]
    private League? currentLeague;

    [ObservableProperty]
    private string tournamentNameDisplay = "No league loaded";

    [ObservableProperty]
    private string tournamentDateRangeDisplay = "N/A";

    public ObservableCollection<string> LeagueNames { get; } = [];
    public ObservableCollection<ScheduleRowViewModel> ScheduledMatches { get; } = [];
    public ObservableCollection<string> DivisionSummaries { get; } = [];
    public ObservableCollection<string> ConstraintSummaries { get; } = [];

    public MainViewModel(LeagueService leagueService, SchedulingService schedulingService, ExportService exportService, string leaguesRoot)
    {
        _leagueService = leagueService;
        _schedulingService = schedulingService;
        _exportService = exportService;
        _leaguesRoot = leaguesRoot;

        RefreshLeagueNames();
        EnsureSampleLeagueAndLoadAsync().GetAwaiter().GetResult();
    }

    [RelayCommand]
    private void CreateLeague()
    {
        var name = $"League_{DateTime.Now:yyyyMMdd_HHmmss}";
        _leagueService.CreateLeague(name);
        RefreshLeagueNames();
        SelectedLeagueName = name;
    }

    [RelayCommand]
    private async Task OpenLeague()
    {
        if (string.IsNullOrWhiteSpace(SelectedLeagueName))
        {
            return;
        }

        CurrentLeague = await _leagueService.LoadLeagueAsync(SelectedLeagueName);
        RenderSchedule();
        RefreshLeagueDetails();
    }

    [RelayCommand]
    private void DeleteLeague()
    {
        if (string.IsNullOrWhiteSpace(SelectedLeagueName))
        {
            return;
        }

        _leagueService.DeleteLeague(SelectedLeagueName);
        SelectedLeagueName = null;
        CurrentLeague = null;
        ScheduledMatches.Clear();
        RefreshLeagueDetails();
        RefreshLeagueNames();
    }

    [RelayCommand]
    private async Task GenerateSchedule()
    {
        if (CurrentLeague is null)
        {
            MessageBox.Show("Open a league first.", "Scheduler");
            return;
        }

        var result = _schedulingService.Generate(CurrentLeague);
        CurrentLeague.Matches = result.ScheduledMatches.ToList();
        await _leagueService.SaveLeagueAsync(CurrentLeague);
        await _exportService.ExportScheduleAsync(CurrentLeague.Matches, GetScheduleOutputPath(CurrentLeague.Name));
        RenderSchedule();

        if (result.UnscheduledMatches.Count > 0)
        {
            var reasons = string.Join(Environment.NewLine, result.UnscheduledMatches.Take(5).Select(x => $"{x.Match.TeamOne} vs {x.Match.TeamTwo}: {x.Reason}"));
            MessageBox.Show($"Unscheduled matches detected:\n{reasons}\n\nWhich constraint should be relaxed?", "Constraint Relaxation Required");
        }
    }

    [RelayCommand]
    private async Task ExportSchedule()
    {
        if (CurrentLeague is null)
        {
            return;
        }

        var output = GetScheduleOutputPath(CurrentLeague.Name);
        await _exportService.ExportScheduleAsync(CurrentLeague.Matches, output);
        MessageBox.Show($"Exported: {output}", "Export");
    }

    private void RefreshLeagueNames()
    {
        LeagueNames.Clear();
        foreach (var name in _leagueService.GetLeagueNames())
        {
            LeagueNames.Add(name);
        }
    }

    private async Task EnsureSampleLeagueAndLoadAsync()
    {
        const string sampleLeague = "SampleLeague";
        if (!LeagueNames.Contains(sampleLeague))
        {
            _leagueService.CreateLeague(sampleLeague);
        }

        RefreshLeagueNames();
        SelectedLeagueName = LeagueNames.Contains(sampleLeague) ? sampleLeague : LeagueNames.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(SelectedLeagueName))
        {
            CurrentLeague = await _leagueService.LoadLeagueAsync(SelectedLeagueName);
            RenderSchedule();
            RefreshLeagueDetails();
        }
    }

    private string GetScheduleOutputPath(string leagueName)
    {
        return Path.Combine(_leaguesRoot, leagueName, "schedule.csv");
    }

    private void RenderSchedule()
    {
        ScheduledMatches.Clear();
        if (CurrentLeague is null)
        {
            return;
        }

        foreach (var match in CurrentLeague.Matches.OrderBy(m => m.Sequence))
        {
            ScheduledMatches.Add(new ScheduleRowViewModel
            {
                Sequence = match.Sequence,
                DivisionName = match.DivisionName,
                Date = match.Date,
                TimeRange = match.Slot is null ? string.Empty : $"{match.Slot.Start:hh\\:mm} - {match.Slot.End:hh\\:mm}",
                TeamOne = match.TeamOne,
                TeamTwo = match.TeamTwo,
                GroundName = match.Ground?.Name ?? string.Empty,
                UmpireOne = match.UmpireOne ?? string.Empty,
                UmpireTwo = match.UmpireTwo ?? string.Empty
            });
        }
    }

    private void RefreshLeagueDetails()
    {
        DivisionSummaries.Clear();
        ConstraintSummaries.Clear();

        if (CurrentLeague?.Tournament is null)
        {
            TournamentNameDisplay = "No league loaded";
            TournamentDateRangeDisplay = "N/A";
            return;
        }

        TournamentNameDisplay = CurrentLeague.Tournament.Name;
        TournamentDateRangeDisplay = $"{CurrentLeague.Tournament.StartDate:yyyy-MM-dd} to {CurrentLeague.Tournament.EndDate:yyyy-MM-dd}";

        foreach (var division in CurrentLeague.Divisions)
        {
            var teamCount = division.Teams.Count;
            DivisionSummaries.Add($"{division.Name} ({teamCount} teams)");
        }

        foreach (var request in CurrentLeague.Constraints.OrderBy(c => c.Date).ThenBy(c => c.TeamName))
        {
            var start = request.StartTime?.ToString("HH:mm") ?? "--";
            var end = request.EndTime?.ToString("HH:mm") ?? "--";
            ConstraintSummaries.Add($"{request.Date:yyyy-MM-dd} | {request.TeamName} | {start}-{end}");
        }
    }
}

public sealed class ScheduleRowViewModel
{
    public int Sequence { get; init; }
    public string DivisionName { get; init; } = string.Empty;
    public DateOnly? Date { get; init; }
    public string TimeRange { get; init; } = string.Empty;
    public string TeamOne { get; init; } = string.Empty;
    public string TeamTwo { get; init; } = string.Empty;
    public string GroundName { get; init; } = string.Empty;
    public string UmpireOne { get; init; } = string.Empty;
    public string UmpireTwo { get; init; } = string.Empty;
}
