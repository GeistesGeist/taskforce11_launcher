using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TaskForce11Launcher.Models;

namespace TaskForce11Launcher.Converters;

/// <summary>
/// Faerbt Statusmarkierung und -plakette einer Modzeile. Mit ConverterParameter="Subtle"
/// kommt dieselbe Farbe stark transparent zurueck - fuer den Hintergrund der Plakette,
/// damit sie zur Schriftfarbe passt, ohne sie zu ueberstrahlen.
/// </summary>
public sealed class ModStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ModStatus.Installed => "OkBrush",
            ModStatus.Missing => "WarnBrush",
            ModStatus.Subscribing or ModStatus.Updating or ModStatus.Redownloading => "AccentBrush",
            ModStatus.Failed => "ErrorBrush",
            _ => "MutedBrush"
        };

        var brush = Application.Current.TryFindResource(key) as SolidColorBrush ?? Brushes.Gray;

        if (string.Equals(parameter as string, "Subtle", StringComparison.OrdinalIgnoreCase))
        {
            var c = brush.Color;
            var subtle = new SolidColorBrush(Color.FromArgb(38, c.R, c.G, c.B));
            subtle.Freeze();
            return subtle;
        }

        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
