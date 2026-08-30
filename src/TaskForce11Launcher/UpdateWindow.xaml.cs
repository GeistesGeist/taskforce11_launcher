using System.Windows;
using TaskForce11Launcher.Interop;
using TaskForce11Launcher.Services;

namespace TaskForce11Launcher;

/// <summary>
/// Kleines Fenster, das waehrend der Update-Pruefung beim Start zu sehen ist. Ohne das
/// haengt der Launcher beim ersten Start nach einem Release scheinbar grundlos ein paar
/// Sekunden bis Minuten, bevor sich ueberhaupt etwas zeigt - bei einem Paket dieser
/// Groesse sieht das aus, als waere er abgestuerzt.
/// </summary>
public partial class UpdateWindow : Window
{
    public UpdateWindow()
    {
        InitializeComponent();
        WindowCornerHelper.ApplyRoundedCorners(this);
        this.ApplyCachedBackground();
    }

    /// <summary>
    /// Nimmt Meldungen des Updaters entgegen. Wird aus einem Progress&lt;T&gt; heraus auf
    /// dem UI-Thread aufgerufen, das Fenster kann also direkt angefasst werden.
    /// </summary>
    public void Report(UpdateStatus status)
    {
        StatusLabel.Text = status.Message;

        // Solange nur gesucht wird, gibt es keinen sinnvollen Prozentwert - dann laeuft
        // der Balken durch, statt bei null zu stehen und Stillstand vorzutaeuschen.
        if (status.PercentComplete is { } percent)
        {
            Progress.IsIndeterminate = false;
            Progress.Value = percent;
            PercentLabel.Text = $"{percent} %";
        }
        else
        {
            Progress.IsIndeterminate = true;
            PercentLabel.Text = string.Empty;
        }
    }
}
