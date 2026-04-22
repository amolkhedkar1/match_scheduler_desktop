using CricketScheduler.App.ViewModels;
using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CricketScheduler.App.Views;

public partial class StatisticsView : UserControl
{
    private StatisticsViewModel? _vm;

    public StatisticsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        if (e.NewValue is MainViewModel mainVm)
        {
            _vm = mainVm.StatisticsVM;
            _vm.PropertyChanged += OnVmPropertyChanged;
            RefreshAllGrids();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(StatisticsViewModel.MatchesScheduledTable):
                MatchesGrid.ItemsSource = _vm?.MatchesScheduledTable?.DefaultView;
                break;
            case nameof(StatisticsViewModel.UmpiringScheduleTable):
                UmpiringGrid.ItemsSource = _vm?.UmpiringScheduleTable?.DefaultView;
                break;
            case nameof(StatisticsViewModel.GroundAssignmentTable):
                GroundGrid.ItemsSource = _vm?.GroundAssignmentTable?.DefaultView;
                break;
        }
    }

    private void RefreshAllGrids()
    {
        MatchesGrid.ItemsSource = _vm?.MatchesScheduledTable?.DefaultView;
        UmpiringGrid.ItemsSource = _vm?.UmpiringScheduleTable?.DefaultView;
        GroundGrid.ItemsSource = _vm?.GroundAssignmentTable?.DefaultView;
    }

    internal void Grid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        var header = e.Column.Header?.ToString() ?? string.Empty;

        if (header == "Team")
        {
            e.Column.MinWidth = 130;
            e.Column.Width = new DataGridLength(130);
            if (e.Column is DataGridTextColumn tc)
                tc.HeaderStyle = (Style)Resources["TeamHeaderStyle"];
        }
        else if (header == "Total")
        {
            e.Column.MinWidth = 55;
            e.Column.Width = new DataGridLength(55);
            if (e.Column is DataGridTextColumn tc)
                tc.HeaderStyle = (Style)Resources["TotalHeaderStyle"];
        }
        else
        {
            e.Column.MinWidth = 52;
            e.Column.Width = new DataGridLength(52);
        }
    }

    internal void Grid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is DataRowView drv &&
            drv.Row.Table.Columns.Contains("Team") &&
            drv["Team"]?.ToString() == "Total")
        {
            e.Row.FontWeight = FontWeights.Bold;
            e.Row.Background = new SolidColorBrush(Color.FromRgb(220, 220, 220));
        }
        else
        {
            e.Row.FontWeight = FontWeights.Normal;
            e.Row.Background = Brushes.Transparent;
        }
    }
}
