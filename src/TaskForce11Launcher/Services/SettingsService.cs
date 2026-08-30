using System.IO;
using System.Text.Json;
using TaskForce11Launcher.Models;

namespace TaskForce11Launcher.Services;

/// <summary>
/// Nutzereinstellungen liegen unter %AppData%\TaskForce11Launcher\settings.json. Die
/// Feed-URLs stehen zusaetzlich in der mitgelieferten config/default-config.json - die
/// gilt beim ersten Start und ist die einzige Stelle, an der die Einheit ihre URLs pflegt.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static string SettingsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskForce11Launcher");

    private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    private static string BundledDefaultConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "config", "default-config.json");

    public AppSettings Load()
    {
        var defaults = LoadBundledDefaults();

        if (!File.Exists(SettingsPath)) return defaults;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null) return defaults;

            // Die URLs kommen immer aus der mitgelieferten Konfiguration, nie aus der
            // gespeicherten settings.json: zieht die Einheit auf ein neues Repo um, soll
            // ein Launcher-Update das mitbringen, ohne dass jeder Spieler seine lokale
            // settings.json loeschen muss. Nur die Pfade sind wirklich nutzerspezifisch.
            settings.ModlistUrl = defaults.ModlistUrl;
            settings.ServerDataUrl = defaults.ServerDataUrl;
            settings.GithubRepoUrl = defaults.GithubRepoUrl;

            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Eine kaputte oder gerade gesperrte settings.json darf den Launcher nicht
            // am Starten hindern - die Pfade werden dann eben neu erkannt.
            return defaults;
        }
    }

    private static AppSettings LoadBundledDefaults()
    {
        if (!File.Exists(BundledDefaultConfigPath)) return new AppSettings();

        try
        {
            var json = File.ReadAllText(BundledDefaultConfigPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (IOException)
        {
            // Schlimmstenfalls werden die Pfade beim naechsten Start erneut erkannt -
            // kein Grund, den laufenden Start abzubrechen.
        }
    }
}
