using System.IO;
using Velopack;
using Velopack.Sources;

namespace TaskForce11Launcher.Services;

/// <summary>
/// Selbstaktualisierung ueber die GitHub-Releases des Einheits-Repos (Velopack).
/// </summary>
public sealed class UpdateService
{
    // Ein fehlgeschlagenes Einspielen liegt fast immer daran, dass der Echtzeitschutz von
    // Windows Defender eine Datei unter "current/" gerade offen haelt, waehrend Update.exe
    // den Ordner sichern will. Das ist nach ein paar Minuten vorbei und nicht dauerhaft -
    // die Sperre unten muss also nur lang genug sein, um ein Neustart-Karussell zu
    // verhindern, und kurz genug, um einen legitimen zweiten Versuch nicht auszubremsen.
    private static readonly TimeSpan RetryCooldown = TimeSpan.FromMinutes(3);

    private readonly string _githubRepoUrl;

    // Bewusst eine eigene Markierungsdatei und kein Feld in settings.json: das ist
    // Buchhaltung des Updaters, keine Einstellung des Spielers.
    private static string MarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskForce11Launcher", "last-update-attempt.txt");

    public UpdateService(string githubRepoUrl)
    {
        _githubRepoUrl = githubRepoUrl;
    }

    public async Task<bool> CheckAndApplyAsync(Action<string>? onStatus = null)
    {
        if (string.IsNullOrWhiteSpace(_githubRepoUrl)) return false;

        try
        {
            var manager = new UpdateManager(new GithubSource(_githubRepoUrl, null, false));
            if (!manager.IsInstalled)
            {
                onStatus?.Invoke("Kein installiertes Paket erkannt (Entwicklungsmodus) - Update-Prüfung übersprungen.");
                return false;
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                // Wir sind auf dem aktuellen Stand - der Versuch, auf den eine etwaige
                // Markierung zeigt, muss also doch durchgekommen sein. Aufraeumen, damit
                // ein spaeterer, echt gescheiterter Versuch nicht von einem Ueberbleibsel
                // verdeckt wird.
                ClearLastAttempt();
                onStatus?.Invoke("Launcher ist aktuell.");
                return false;
            }

            var targetVersion = update.TargetFullRelease.Version.ToString();

            // Hat der vorige Start genau diese Version schon versucht und sie wird uns
            // jetzt immer noch angeboten, wuerde ein sofortiger zweiter Anlauf nur
            // denselben Fehler wiederholen - alle paar Sekunden in dieselbe Panne
            // neuzustarten ist genau das, was aus einem kurzen Virenscan eine
            // Endlosschleife macht. Nach Ablauf der Sperre ist es dagegen ein frischer
            // Versuch, der eine echte Chance verdient.
            var (lastVersion, lastAttemptAt) = ReadLastAttempt();
            if (lastVersion == targetVersion && DateTime.UtcNow - lastAttemptAt < RetryCooldown)
            {
                onStatus?.Invoke(
                    $"Update auf {targetVersion} ist beim letzten Versuch nicht angekommen - wird für ein paar " +
                    "Minuten übersprungen, damit keine Endlosschleife entsteht. Ein Neustart des Launchers " +
                    "versucht es automatisch erneut, sobald die Sperre abgelaufen ist. Falls es weiter " +
                    "fehlschlägt: Ordner %LocalAppData%\\TaskForce11Launcher löschen und die neueste " +
                    "Setup.exe von GitHub frisch installieren.");
                return false;
            }

            onStatus?.Invoke($"Update {targetVersion} wird geladen…");
            await manager.DownloadUpdatesAsync(update);

            onStatus?.Invoke("Update wird installiert, Launcher startet neu…");
            WriteLastAttempt(targetVersion);
            manager.ApplyUpdatesAndRestart(update);
            return true;
        }
        catch (Exception ex)
        {
            // Ohne Netz, mit blockierender Firewall oder bei einem GitHub-Ausfall soll der
            // Launcher trotzdem starten - eine gescheiterte Update-Pruefung ist kein
            // Grund, niemanden mehr aufs Spiel zu lassen.
            onStatus?.Invoke($"Update-Prüfung fehlgeschlagen: {ex.Message}");
            return false;
        }
    }

    private static (string? Version, DateTime AttemptedAtUtc) ReadLastAttempt()
    {
        try
        {
            if (!File.Exists(MarkerPath)) return (null, DateTime.MinValue);

            var parts = File.ReadAllText(MarkerPath).Trim().Split('|', 2);
            if (parts.Length == 2 && long.TryParse(parts[1], out var ticks))
            {
                return (parts[0], new DateTime(ticks, DateTimeKind.Utc));
            }

            return (null, DateTime.MinValue);
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException)
        {
            return (null, DateTime.MinValue);
        }
    }

    private static void WriteLastAttempt(string version)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, $"{version}|{DateTime.UtcNow.Ticks}");
        }
        catch (IOException)
        {
            // Schlimmstenfalls greift die Schleifenbremse beim naechsten Start nicht -
            // deswegen das Update abzubrechen waere schlechter.
        }
    }

    private static void ClearLastAttempt()
    {
        try
        {
            if (File.Exists(MarkerPath)) File.Delete(MarkerPath);
        }
        catch (IOException)
        {
        }
    }
}
