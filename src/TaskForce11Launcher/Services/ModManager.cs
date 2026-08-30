using System.IO;
using TaskForce11Launcher.Models;

namespace TaskForce11Launcher.Services;

/// <summary>
/// Gleicht die Pflichtmods gegen das ab, was im Workshop-Inhaltsordner von Arma 3
/// tatsaechlich liegt (steamapps/workshop/content/107410/&lt;id&gt;) - dort legt Steam
/// abonnierte Items ab, unabhaengig davon, ob Steam gerade laeuft.
/// </summary>
public sealed class ModManager
{
    public static string GetModPath(ModEntry mod, string workshopContentPath) =>
        Path.Combine(workshopContentPath, mod.WorkshopId.ToString());

    /// <summary>
    /// Ein vorhandener Ordner reicht als Nachweis nicht: Steam legt die Ordnerstruktur
    /// eines Items (addons/, keys/) teilweise schon an, bevor irgendein Inhalt
    /// heruntergeladen wurde - der Mod gilt in Steams eigener Buchfuehrung dann als
    /// installiert, waehrend Arma nichts zu laden findet. Auch eine flache Pruefung auf
    /// Ordnerinhalt greift zu kurz, weil eben jene leeren Unterordner schon als Eintraege
    /// zaehlen. Erst "enthaelt rekursiv mindestens eine Datei" ist die echte Aussage
    /// darueber, ob Arma den Mod laden kann.
    /// </summary>
    public static bool IsInstalled(ModEntry mod, string workshopContentPath) =>
        HasContent(GetModPath(mod, workshopContentPath));

    public static bool HasContent(string path) =>
        Directory.Exists(path) && Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any();

    public static IReadOnlyList<ModEntry> GetMissing(IEnumerable<ModEntry> required, string workshopContentPath) =>
        required.Where(mod => !IsInstalled(mod, workshopContentPath)).ToList();

    public static IReadOnlyList<string> GetInstalledModPaths(IEnumerable<ModEntry> required, string workshopContentPath) =>
        required
            .Select(mod => GetModPath(mod, workshopContentPath))
            .Where(HasContent)
            .ToList();

    /// <summary>
    /// Loescht den lokalen Ordner eines Mods, damit ein anschliessender Download bei null
    /// anfaengt - noetig, weil Steams Zustandsverwaltung eine lokale Beschaedigung, von
    /// der sie nie erfahren hat, sonst nie bemerkt.
    /// </summary>
    public static void DeleteInstalledMod(ModEntry mod, string workshopContentPath)
    {
        var path = GetModPath(mod, workshopContentPath);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
