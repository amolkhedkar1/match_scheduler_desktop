using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CricketScheduler.App;

/// <summary>Simple single-line text input dialog.</summary>
public sealed class InputDialog : Window
{
    private readonly TextBox _textBox;
    public string Result => _textBox.Text;

    public InputDialog(string title, string prompt)
    {
        Title = title;
        Width = 380;
        Height = 155;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });

        _textBox = new TextBox { Padding = new Thickness(4) };
        _textBox.KeyDown += (s, e) => { if (e.Key == Key.Return) DialogResult = true; };
        stack.Children.Add(_textBox);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var ok = new Button { Content = "OK", Width = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (s, e) => DialogResult = true;

        var cancel = new Button { Content = "Cancel", Width = 72, IsCancel = true };
        cancel.Click += (s, e) => DialogResult = false;

        btnPanel.Children.Add(ok);
        btnPanel.Children.Add(cancel);
        stack.Children.Add(btnPanel);

        Content = stack;
        Loaded += (s, e) => _textBox.Focus();
    }
}
