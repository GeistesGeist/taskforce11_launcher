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
