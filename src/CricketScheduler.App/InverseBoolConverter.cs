using System.Globalization;
using System.Windows.Data;

namespace CricketScheduler.App;

/// <summary>
/// Inverts a boolean value for use with RadioButton IsChecked bindings
/// where one radio represents true and another represents false on the same property.
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>
/// Converts bool → Visibility in inverse: false → Visible, true → Collapsed.
/// Used for the "Resolve" button which shows only when CanAutoResolve=false.
/// Usage: Visibility="{Binding CanAutoResolve, Converter={StaticResource InverseBoolVis}}"
/// </summary>
[ValueConversion(typeof(bool), typeof(System.Windows.Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
