using System.Windows;
using CricketScheduler.App.Services;
using CricketScheduler.App.ViewModels;

namespace CricketScheduler.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var dataRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "leagues");
        var csvService = new CsvService();
        var leagueService = new LeagueService(dataRoot, csvService);
        var schedulingService = new SchedulingService();
        var exportService = new ExportService(csvService);

        DataContext = new MainViewModel(leagueService, schedulingService, exportService);
    }
}
