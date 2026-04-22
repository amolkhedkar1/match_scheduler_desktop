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

        // Wire commit/push-child callbacks that need a live Window reference
        vm.OnCommitAction  = () => { DialogResult = true;  Close(); };
        vm.OnPushChildAction = childVm =>
        {
            var child = new MoveAnalyzerWindow(childVm, owner: this);
            if (child.ShowDialog() == true)
                vm.RefreshAfterChildCommit();
        };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
