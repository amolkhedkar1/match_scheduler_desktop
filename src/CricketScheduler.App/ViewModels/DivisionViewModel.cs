using CommunityToolkit.Mvvm.ComponentModel;
using CricketScheduler.App.Models;
using System.Collections.ObjectModel;

namespace CricketScheduler.App.ViewModels;

public partial class DivisionViewModel : ObservableObject
{
    public ObservableCollection<Division> Divisions { get; } = [];
}
