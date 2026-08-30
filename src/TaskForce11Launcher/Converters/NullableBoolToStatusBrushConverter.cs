using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TaskForce11Launcher.Converters;

/// <summary>true = online, false = offline, null = wird noch geprüft.</summary>
public sealed class NullableBoolToStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            true => "OkBrush",
            false => "ErrorBrush",
            _ => "MutedBrush"
        };

        return Application.Current.TryFindResource(key) as SolidColorBrush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
