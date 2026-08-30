using System.IO;
using System.Threading;
using Velopack;
using Velopack.Sources;

namespace TaskForce11Launcher.Services;

/// <summary>Was der Updater gerade tut - fuer die Anzeige waehrend des Starts.</summary>
public sealed record UpdateStatus(string Message, int? PercentComplete = null);

/// <summary>
/// Selbstaktualisierung ueber die GitHub-Releases des Einheits-Repos (Velopack).
///
/// Zwei Wege hinein: beim Start wird geprueft, geladen und sofort eingespielt
/// (<see cref="CheckAndApplyAsync"/>), waehrend der Laufzeit dagegen nur geprueft und
/// im Hintergrund geladen (<see cref="CheckAndDownloadAsync"/>) - eingespielt wird
/// dann erst, wenn der Spieler es selbst auslöst. Mitten in einer laufenden Sitzung
/// ungefragt neu zu starten waere das Letzte, was jemand gebrauchen kann.
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
    private UpdateManager? _manager;
    private UpdateInfo? _pendingUpdate;

    // Bewusst eine eigene Markierungsdatei und kein Feld in settings.json: das ist
    // Buchhaltung des Updaters, keine Einstellung des Spielers.
    private static string MarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskForce11Launcher", "last-update-attempt.txt");

    public UpdateService(string githubRepoUrl)
    {
        _githubRepoUrl = githubRepoUrl;
    }

    /// <summary>Version des geladenen, noch nicht eingespielten Updates - sonst null.</summary>
    public string? PendingVersion => _pendingUpdate?.TargetFullRelease.Version.ToString();

    /// <summary>
    /// Im Entwicklungsbetrieb (kein per Setup.exe installiertes Paket) gibt es nichts zu
    /// aktualisieren - dann meldet sich der Updater gar nicht erst.
    /// </summary>
    private UpdateManager? GetManager()
    {
        if (string.IsNullOrWhiteSpace(_githubRepoUrl)) return null;

        _manager ??= new UpdateManager(new GithubSource(_githubRepoUrl, null, false));
        return _manager.IsInstalled ? _manager : null;
    }

    /// <summary>
    /// Startpfad: pruefen, laden und sofort einspielen. Gibt true zurueck, wenn der
    /// Neustart eingeleitet wurde - der Aufrufer soll dann nichts weiter tun.
    /// </summary>
    public async Task<bool> CheckAndApplyAsync(IProgress<UpdateStatus>? progress = null)
    {
        try
        {
            var manager = GetManager();
            if (manager is null) return false;

            progress?.Report(new UpdateStatus("Suche nach Updates…"));

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                // Wir sind auf dem aktuellen Stand - der Versuch, auf den eine etwaige
                // Markierung zeigt, muss also doch durchgekommen sein. Aufraeumen, damit
                // ein spaeterer, echt gescheiterter Versuch nicht von einem Ueberbleibsel
                // verdeckt wird.
                ClearLastAttempt();
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
                progress?.Report(new UpdateStatus(
                    $"Update auf {targetVersion} ist beim letzten Versuch nicht angekommen - wird kurz übersprungen."));
                return false;
            }

            progress?.Report(new UpdateStatus($"Lade Version {targetVersion}…", 0));
            await manager.DownloadUpdatesAsync(update, p => progress?.Report(
                new UpdateStatus($"Lade Version {targetVersion}…", p)));

            progress?.Report(new UpdateStatus("Update wird installiert, Launcher startet neu…", 100));
            WriteLastAttempt(targetVersion);
            manager.ApplyUpdatesAndRestart(update);
            return true;
        }
        catch (Exception ex)
        {
            // Ohne Netz, mit blockierender Firewall oder bei einem GitHub-Ausfall soll der
            // Launcher trotzdem starten - eine gescheiterte Update-Pruefung ist kein
            // Grund, niemanden mehr aufs Spiel zu lassen.
            progress?.Report(new UpdateStatus($"Update-Prüfung fehlgeschlagen: {ex.Message}"));
            return false;
        }
    }

    /// <summary>
    /// Laufzeitpfad: pruefen und - wenn es etwas gibt - im Hintergrund herunterladen,
    /// aber nicht einspielen. Gibt die Version zurueck, sobald sie bereitliegt, sonst
    /// null. Danach wartet sie auf <see cref="ApplyPendingAndRestart"/>.
    /// </summary>
    public async Task<string?> CheckAndDownloadAsync(CancellationToken ct = default)
    {
        // Schon etwas geladen? Dann nicht erneut suchen - die Version liegt bereit und
        // wartet nur noch darauf, eingespielt zu werden.
        if (_pendingUpdate is not null) return PendingVersion;

        try
        {
            var manager = GetManager();
            if (manager is null) return null;

            var update = await manager.CheckForUpdatesAsync();
            if (update is null) return null;

            await manager.DownloadUpdatesAsync(update, cancelToken: ct);

            _pendingUpdate = update;
            return PendingVersion;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Im Hintergrund und ohne Zutun des Spielers - ein Fehlschlag bleibt hier
            // folgenlos, beim naechsten Durchlauf wird es erneut versucht.
            return null;
        }
    }

    /// <summary>
    /// Spielt das bereitliegende Update ein und startet den Launcher neu. Kehrt nur
    /// zurueck, wenn nichts bereitlag.
    /// </summary>
    public void ApplyPendingAndRestart()
    {
        if (_pendingUpdate is null || _manager is null) return;

        WriteLastAttempt(_pendingUpdate.TargetFullRelease.Version.ToString());
        _manager.ApplyUpdatesAndRestart(_pendingUpdate);
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
