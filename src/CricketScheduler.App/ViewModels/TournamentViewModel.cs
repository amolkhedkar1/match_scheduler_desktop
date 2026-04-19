using CommunityToolkit.Mvvm.ComponentModel;
using CricketScheduler.App.Models;
using System.Collections.ObjectModel;

namespace CricketScheduler.App.ViewModels;

public partial class TournamentViewModel : ObservableObject
{
    [ObservableProperty] private string tournamentName = string.Empty;
    [ObservableProperty] private DateOnly startDate = DateOnly.FromDateTime(DateTime.Today);
    [ObservableProperty] private DateOnly endDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(2));

    public ObservableCollection<DateOnly> DiscardedDates { get; } = [];
    public ObservableCollection<Ground> Grounds { get; } = [];
    public ObservableCollection<TimeSlot> TimeSlots { get; } = [];
}
