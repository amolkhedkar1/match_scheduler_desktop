using CricketScheduler.App.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace CricketScheduler.App.Views;

public partial class SchedulerView : UserControl
{
    public SchedulerView() => InitializeComponent();

    /// <summary>
    /// Double-clicking a row in the available-slot analysis grid fills the
    /// manual scheduling input fields (Date / Slot / Ground) automatically.
    /// </summary>
    private void UnscheduledAnalysisRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm &&
            UnscheduledAnalysisGrid.SelectedItem is MoveOptionViewModel option)
            vm.ApplyUnscheduledSlotCommand.Execute(option);
    }
}
