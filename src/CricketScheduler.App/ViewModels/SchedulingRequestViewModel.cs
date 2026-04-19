using CommunityToolkit.Mvvm.ComponentModel;
using CricketScheduler.App.Models;
using System.Collections.ObjectModel;

namespace CricketScheduler.App.ViewModels;

public partial class SchedulingRequestViewModel : ObservableObject
{
    public ObservableCollection<SchedulingRequest> Requests { get; } = [];
}
