using System.Globalization;
using System.Windows.Data;

namespace TaskForce11Launcher.Converters;

public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;
}
