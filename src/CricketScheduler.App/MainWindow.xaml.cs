using System.Windows;
using CricketScheduler.App.Services;
using CricketScheduler.App.ViewModels;

namespace CricketScheduler.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var dataRoot = AppPaths.ResolveLeaguesRoot();
        var csvService = new CsvService();
        var leagueService = new LeagueService(dataRoot, csvService);
        var schedulingService = new SchedulingService();
        var exportService = new ExportService(csvService);

        var vm = new MainViewModel(leagueService, schedulingService, exportService, dataRoot);
        vm.OwnerWindow = this;
        DataContext = vm;
    }
}
