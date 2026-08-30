using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TaskForce11Launcher.Services;

/// <summary>
/// Findet Steam- und Arma-3-Installation ueber Registry und bekannte Standardpfade,
/// damit niemand etwas eintippen muss. Schlaegt das fehl, faellt der Aufrufer auf den
/// manuell in den Einstellungen gesetzten Pfad zurueck.
/// </summary>
public sealed class PathDetectionService
{
    public const uint Arma3AppId = 107410;

    public string? FindSteamPath()
    {
        var fromUser = ReadRegistryString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        if (IsValidDirectory(fromUser)) return NormalizePath(fromUser!);

        var fromMachine64 = ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        if (IsValidDirectory(fromMachine64)) return NormalizePath(fromMachine64!);

        var fromMachine32 = ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
        if (IsValidDirectory(fromMachine32)) return NormalizePath(fromMachine32!);

        string[] fallbacks =
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam"
        };
        return fallbacks.FirstOrDefault(Directory.Exists);
    }

    /// <summary>
    /// Arma 3 und der Workshop-Ordner liegen bei vielen Spielern nicht in der
    /// Steam-Hauptinstallation, sondern auf einer zweiten Platte - libraryfolders.vdf
    /// listet alle eingerichteten Bibliotheken.
    /// </summary>
    public IReadOnlyList<string> FindSteamLibraryRoots(string steamPath)
    {
        var roots = new List<string> { steamPath };

        var vdfCandidates = new[]
        {
            Path.Combine(steamPath, "steamapps", "libraryfolders.vdf"),
            Path.Combine(steamPath, "config", "libraryfolders.vdf")
        };

        foreach (var vdfPath in vdfCandidates)
        {
            if (!File.Exists(vdfPath)) continue;

            string text;
            try
            {
                text = File.ReadAllText(vdfPath);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (Match match in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
            {
                // Pfade stehen in der VDF mit verdoppelten Backslashes.
                var path = match.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(path) && !roots.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    roots.Add(path);
                }
            }

            break;
        }

        return roots;
    }

    public string? FindArma3Path(string steamPath)
    {
        foreach (var root in FindSteamLibraryRoots(steamPath))
        {
            var candidate = Path.Combine(root, "steamapps", "common", "Arma 3");
            if (File.Exists(Path.Combine(candidate, "arma3_x64.exe")) ||
                File.Exists(Path.Combine(candidate, "arma3.exe")))
            {
                return candidate;
            }
        }

        return null;
    }

    public string? FindWorkshopContentPath(string steamPath, string arma3Path)
    {
        foreach (var root in FindSteamLibraryRoots(steamPath))
        {
            var candidate = Path.Combine(root, "steamapps", "workshop", "content", Arma3AppId.ToString());
            if (Directory.Exists(candidate)) return candidate;

            // Der Workshop-Ordner liegt in derselben Bibliothek wie das Spiel. Existiert
            // er noch nicht (frische Installation, noch nichts abonniert), ist das der
            // Pfad, unter dem Steam ihn beim ersten Abo anlegen wird.
            if (arma3Path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return candidate;
        }

        return null;
    }

    /// <summary>
    /// Sucht die ausführbare Datei des TeamSpeak-3-Clients. Erste Wahl ist der
    /// registrierte ts3server:-Handler - er zeigt genau auf die Installation, die
    /// Windows für solche Links auch tatsächlich startet, egal wohin sie installiert
    /// wurde. Danach der Uninstall-Eintrag, zuletzt die üblichen Standardpfade.
    /// </summary>
    public string? FindTeamspeakClient()
    {
        var fromHandler = ReadTs3UrlHandlerPath();
        if (fromHandler is not null && File.Exists(fromHandler)) return fromHandler;

        foreach (var (hive, path) in new[]
                 {
                     (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TeamSpeak 3 Client"),
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\TeamSpeak 3 Client"),
                     (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TeamSpeak 3 Client")
                 })
        {
            var installLocation = ReadRegistryString(hive, path, "InstallLocation");
            var exe = ResolveTeamspeakExe(installLocation);
            if (exe is not null) return exe;
        }

        string[] fallbacks =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TeamSpeak 3 Client"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "TeamSpeak 3 Client")
        };

        return fallbacks.Select(ResolveTeamspeakExe).FirstOrDefault(exe => exe is not null);
    }

    /// <summary>
    /// Der Handler steht in der Registry als vollständige Kommandozeile, also etwa
    /// "C:\...\ts3client_win64.exe" "%1" - daraus muss nur das Programm selbst heraus.
    /// </summary>
    private static string? ReadTs3UrlHandlerPath()
    {
        var command = ReadRegistryString(Registry.ClassesRoot, @"ts3server\shell\open\command", valueName: string.Empty);
        if (string.IsNullOrWhiteSpace(command)) return null;

        var match = Regex.Match(command, "^\\s*\"([^\"]+)\"");
        if (match.Success) return match.Groups[1].Value;

        // Ohne Anführungszeichen endet der Pfad beim ersten Leerzeichen - das trifft nur
        // Installationen ohne Leerzeichen im Pfad, sonst ist der Eintrag ohnehin zitiert.
        var firstToken = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstToken) ? null : firstToken;
    }

    public static string? ResolveTeamspeakExe(string? installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory)) return null;

        string[] candidates = { "ts3client_win64.exe", "ts3client_win32.exe" };
        return candidates
            .Select(name => Path.Combine(installDirectory, name))
            .FirstOrDefault(File.Exists);
    }

    private static string? ReadRegistryString(RegistryKey hive, string subKeyPath, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(subKeyPath);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsValidDirectory(string? path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    private static string NormalizePath(string path) => path.Replace('/', '\\');
}
