using System.Windows;

namespace CricketScheduler.App;

public partial class App : Application
{
    public App()
    {
        this.DispatcherUnhandledException += (s, e) =>
        {

            var text = e.Exception.ToString() + e.Exception.StackTrace;
            Clipboard.SetText(text); // optional: auto-copy


            var w = new Window
            {
                Title = "Unhandled Exception",
                Width = 600,
                Height = 400,
                Content = new System.Windows.Controls.TextBox
                {
                    Text = text,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
                }
            };

            w.ShowDialog();
            Environment.Exit(1);

        };
    }

}
