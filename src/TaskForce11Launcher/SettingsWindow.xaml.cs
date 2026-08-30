using System.Windows;
using Microsoft.Win32;
using TaskForce11Launcher.Interop;
using TaskForce11Launcher.Models;

namespace TaskForce11Launcher;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _original;

    public AppSettings ResultSettings { get; private set; }

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        WindowCornerHelper.ApplyRoundedCorners(this);
        this.ApplyCachedBackground();
        _original = current;
        ResultSettings = current;

        Arma3PathBox.Text = current.Arma3Path ?? string.Empty;
        TeamspeakPathBox.Text = current.TeamspeakPath ?? string.Empty;
    }

    private void OnBrowseArma3Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Arma 3 Installationsordner wählen" };
        if (dialog.ShowDialog() == true) Arma3PathBox.Text = dialog.FolderName;
    }

    private void OnBrowseTeamspeakClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "TeamSpeak-3-Client wählen",
            Filter = "TeamSpeak 3 Client|ts3client_win64.exe;ts3client_win32.exe|Programme|*.exe"
        };

        if (dialog.ShowDialog() == true) TeamspeakPathBox.Text = dialog.FileName;
    }

    /// <summary>
    /// Leert beide Felder, damit die automatische Erkennung beim Speichern wieder greift -
    /// der Ausweg, wenn ein einmal falsch gesetzter Pfad die Erkennung dauerhaft blockiert.
    /// </summary>
    private void OnRedetectClick(object sender, RoutedEventArgs e)
    {
        Arma3PathBox.Text = string.Empty;
        TeamspeakPathBox.Text = string.Empty;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ResultSettings = new AppSettings
        {
            // Ist der Arma-Pfad leer, soll auch der gemerkte Steam-Pfad neu ermittelt
            // werden - sonst sucht die Erkennung weiter in einer Steam-Installation, die
            // gar nicht mehr die richtige sein muss.
            SteamPath = string.IsNullOrWhiteSpace(Arma3PathBox.Text) ? null : _original.SteamPath,
            Arma3Path = string.IsNullOrWhiteSpace(Arma3PathBox.Text) ? null : Arma3PathBox.Text.Trim(),
            TeamspeakPath = string.IsNullOrWhiteSpace(TeamspeakPathBox.Text) ? null : TeamspeakPathBox.Text.Trim(),

            // Feste Anwendungskonfiguration, nicht vom Spieler editierbar - unveraendert
            // uebernehmen. Gepflegt wird sie in config/default-config.json.
            ModlistUrl = _original.ModlistUrl,
            ServerDataUrl = _original.ServerDataUrl,
            GithubRepoUrl = _original.GithubRepoUrl
        };

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnCloseIconClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
