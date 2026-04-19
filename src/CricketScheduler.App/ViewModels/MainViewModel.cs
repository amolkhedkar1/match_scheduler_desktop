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

    [ObservableProperty]
    private string? selectedLeagueName;

    [ObservableProperty]
    private League? currentLeague;

    public ObservableCollection<string> LeagueNames { get; } = [];
    public ObservableCollection<ScheduleRowViewModel> ScheduledMatches { get; } = [];

    public MainViewModel(LeagueService leagueService, SchedulingService schedulingService, ExportService exportService)
    {
        _leagueService = leagueService;
        _schedulingService = schedulingService;
        _exportService = exportService;

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
        await _exportService.ExportScheduleAsync(CurrentLeague.Matches, Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "leagues", CurrentLeague.Name, "schedule.csv"));
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

        var output = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "leagues", CurrentLeague.Name, "schedule.csv");
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
        }
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
