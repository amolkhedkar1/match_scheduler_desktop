using CommunityToolkit.Mvvm.ComponentModel;

namespace CricketScheduler.App.ViewModels;

public partial class LeagueViewModel : ObservableObject
{
    [ObservableProperty]
    private string leagueName = string.Empty;
}
