using System.Windows;
using System.Windows.Input;
using TaskForce11Launcher.Interop;
using TaskForce11Launcher.Services;

namespace TaskForce11Launcher;

/// <summary>
/// Rückfragen und Meldungen im Stil des Launchers, anstelle der Windows-Standarddialoge.
/// Wird immer über die statischen Methoden benutzt, nie direkt erzeugt.
/// </summary>
public partial class MessageWindow : Window
{
    private MessageWindow(string heading, string message, string confirmText, string? cancelText)
    {
        InitializeComponent();
        WindowCornerHelper.ApplyRoundedCorners(this);
        BackgroundImage.Source = BackgroundImageService.LoadCached();

        HeadingLabel.Text = heading;
        MessageLabel.Text = message;
        ConfirmButton.Content = confirmText;

        if (cancelText is null)
        {
            CancelButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            CancelButton.Content = cancelText;
        }
    }

    /// <summary>Stellt eine Ja/Nein-Frage. true, wenn bestätigt wurde.</summary>
    public static bool Confirm(string heading, string message, string confirmText = "Ja", string cancelText = "Nein") =>
        ShowCore(heading, message, confirmText, cancelText) == true;

    /// <summary>Zeigt eine Meldung mit einem einzelnen Knopf.</summary>
    public static void Inform(string heading, string message, string confirmText = "OK") =>
        ShowCore(heading, message, confirmText, cancelText: null);

    private static bool? ShowCore(string heading, string message, string confirmText, string? cancelText)
    {
        var window = new MessageWindow(heading, message, confirmText, cancelText);

        // Über dem Hauptfenster zentrieren, sofern eines offen ist. Beim Start - etwa bei
        // einem Fehler noch vor dem ersten Fenster - gibt es keines; dann bleibt die
        // Mitte des Bildschirms, statt dass ShowDialog an einem unsichtbaren Owner
        // scheitert.
        var owner = Application.Current?.MainWindow;
        if (owner is not null && !ReferenceEquals(owner, window) && owner.IsVisible)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        return window.ShowDialog();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Escape bricht ab - bei einer Meldung ohne Abbrechen-Knopf schliesst es wie OK,
        // damit das Fenster nicht ohne Ausweg stehen bleibt.
        if (e.Key != Key.Escape) return;

        DialogResult = CancelButton.Visibility == Visibility.Visible ? false : true;
    }
}
