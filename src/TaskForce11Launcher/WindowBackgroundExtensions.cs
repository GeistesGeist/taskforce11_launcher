using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskForce11Launcher.Services;

namespace TaskForce11Launcher;

internal static class WindowBackgroundExtensions
{
    /// <summary>
    /// Legt das zwischengespeicherte Hintergrundbild hinter den Inhalt eines Fensters,
    /// sofern eines vorliegt. Erwartet im Fenster ein Panel namens "BackgroundLayer".
    ///
    /// Gesetzt wird ein Hintergrundpinsel und kein Image-Element: ein Image meldet die
    /// Abmessungen seiner Bildquelle als Wunschgröße an das Layout. In einem Fenster,
    /// das seine Höhe aus dem Inhalt bezieht, bläht das die Höhe auf die des Bildes auf -
    /// eine 440 Pixel breite Rückfrage wurde so über tausend Pixel hoch. Ein Pinsel geht
    /// in die Messung nicht ein und füllt nur, was ohnehin da ist.
    /// </summary>
    public static void ApplyCachedBackground(this Window window)
    {
        if (window.FindName("BackgroundLayer") is not Panel layer) return;

        var cached = BackgroundImageService.LoadCached();
        if (cached is null) return;

        layer.Background = new ImageBrush(cached)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };
    }
}
