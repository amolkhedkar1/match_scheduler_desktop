using CricketScheduler.App.ViewModels;
using System.Windows;

namespace CricketScheduler.App.Views;

public partial class MoveAnalyzerWindow : Window
{
    private readonly MoveAnalyzerViewModel _vm;

    public MoveAnalyzerWindow(MoveAnalyzerViewModel vm, Window owner)
    {
        InitializeComponent();
        _vm         = vm;
        DataContext = vm;
        Owner       = owner;

        // Commit closes the window with success.
        vm.OnCommitAction = () => { DialogResult = true; Close(); };

        // Open a child Move Analyzer; always refresh parent after child closes
        // so any real-state changes (commit or unschedule) are reflected here.
        vm.OnPushChildAction = childVm =>
        {
            var child = new MoveAnalyzerWindow(childVm, owner: this);
            child.ShowDialog(); // result doesn't matter — always refresh
            vm.RefreshAfterChildAction();
        };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
