using CricketScheduler.App.ViewModels;
using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CricketScheduler.App.Views;

public partial class StatisticsView : UserControl
{
    private StatisticsViewModel? _vm;

    public StatisticsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        ExportAllMatchesBtn.Click  += (_, _) => ExportAll(_vm?.MatchesByDivision,  "matches");
        ExportAllUmpiringBtn.Click += (_, _) => ExportAll(_vm?.UmpiringByDivision, "umpiring");
        ExportAllGroundBtn.Click   += (_, _) => ExportAll(_vm?.GroundByDivision,   "ground");
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        if (e.NewValue is MainViewModel mainVm)
        {
            _vm = mainVm.StatisticsVM;
            _vm.PropertyChanged += OnVmPropertyChanged;
            RefreshAll();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(StatisticsViewModel.MatchesByDivision):
                BuildPanels(MatchesPanel,  _vm!.MatchesByDivision,  "matches");  break;
            case nameof(StatisticsViewModel.UmpiringByDivision):
                BuildPanels(UmpiringPanel, _vm!.UmpiringByDivision, "umpiring"); break;
            case nameof(StatisticsViewModel.GroundByDivision):
                BuildPanels(GroundPanel,   _vm!.GroundByDivision,   "ground");   break;
        }
    }

    private void RefreshAll()
    {
        if (_vm is null) return;
        BuildPanels(MatchesPanel,  _vm.MatchesByDivision,  "matches");
        BuildPanels(UmpiringPanel, _vm.UmpiringByDivision, "umpiring");
        BuildPanels(GroundPanel,   _vm.GroundByDivision,   "ground");
    }

    // ── Build one panel (one DataGrid per division) ───────────────────────────

    private void BuildPanels(StackPanel panel, List<DivisionStatTable> tables, string prefix)
    {
        panel.Children.Clear();
        if (tables.Count == 0) return;

        foreach (var div in tables)
        {
            // Division header
            var headerDock = new DockPanel { Margin = new Thickness(0, 10, 0, 3) };
            var exportBtn  = new Button
            {
                Content  = "📤 Export",
                Padding  = new Thickness(8, 3, 8, 3),
                Margin   = new Thickness(8, 0, 0, 0),
                FontSize = 11
            };
            var captured = div;
            exportBtn.Click += (_, _) => ExportOne(captured, prefix);
            DockPanel.SetDock(exportBtn, Dock.Right);

            headerDock.Children.Add(exportBtn);
            headerDock.Children.Add(new TextBlock
            {
                Text              = div.DivisionName,
                FontWeight        = FontWeights.SemiBold,
                FontSize          = 13,
                Foreground        = new SolidColorBrush(Color.FromRgb(21, 101, 192)),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(headerDock);

            // DataGrid — AutoGenerateColumns=false so we build columns with explicit Foreground
            var grid = new DataGrid
            {
                IsReadOnly              = true,
                AutoGenerateColumns     = false,
                CanUserAddRows          = false,
                CanUserDeleteRows       = false,
                CanUserReorderColumns   = false,
                GridLinesVisibility     = DataGridGridLinesVisibility.All,
                HorizontalGridLinesBrush= new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                VerticalGridLinesBrush  = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                AlternatingRowBackground= new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
                RowBackground           = Brushes.White,
                SelectionMode           = DataGridSelectionMode.Single,
                ColumnHeaderHeight      = 30,
                RowHeight               = 24,
                FontSize                = 12,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
                Margin                  = new Thickness(0, 0, 0, 4)
            };

            BuildColumns(grid, div.Table);
            grid.LoadingRow += OnLoadingRow;

            panel.Children.Add(grid);
            grid.ItemsSource = div.Table.DefaultView;
        }
    }

    // Build one DataGridTemplateColumn per DataTable column.
    // Foreground is set as a LOCAL VALUE via FrameworkElementFactory.SetValue — this has the
    // highest WPF property precedence and is never overridden by theme or template bindings.
    private void BuildColumns(DataGrid grid, DataTable table)
    {
        foreach (DataColumn col in table.Columns)
        {
            var name   = col.ColumnName;
            var isTeam = name == "Team";
            var width  = isTeam ? 130 : name == "Total" ? 55 : 50;

            var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetValue(TextBlock.ForegroundProperty, Brushes.Black);           // local value — beats everything
            factory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            if (!isTeam)
                factory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            factory.SetBinding(TextBlock.TextProperty, new Binding($"[{name}]"));

            var headerStyle = isTeam        ? (Style)Resources["TeamHeaderStyle"]
                            : name == "Total" ? (Style)Resources["TotalHeaderStyle"]
                            : null;

            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header         = name,
                HeaderStyle    = headerStyle,
                CellTemplate   = new DataTemplate { VisualTree = factory },
                MinWidth       = width,
                Width          = new DataGridLength(width),
                CanUserSort    = true,
                SortMemberPath = name
            });
        }
    }

    private static void OnLoadingRow(object? sender, DataGridRowEventArgs e)
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

    // ── Export helpers ────────────────────────────────────────────────────────

    private static void ExportOne(DivisionStatTable div, string prefix)
    {
        var path = StatisticsViewModel.ExportTableToCsv(div.Table, prefix, div.DivisionName);
        MessageBox.Show($"Exported to:\n{path}", "Export Complete");
    }

    private static void ExportAll(List<DivisionStatTable>? tables, string prefix)
    {
        if (tables is null || tables.Count == 0) return;
        string? lastPath = null;
        foreach (var div in tables)
            lastPath = StatisticsViewModel.ExportTableToCsv(div.Table, prefix, div.DivisionName);
        MessageBox.Show($"Exported {tables.Count} division(s) to outputs folder.\nLast file:\n{lastPath}", "Export Complete");
    }
}
