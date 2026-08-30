using System.IO;
using System.Text.Json;

namespace TaskForce11Launcher.Services;

/// <summary>
/// Merkt sich pro Workshop-Item den serverseitigen "zuletzt geaendert"-Zeitstempel
/// (SteamUGCDetails_t.m_rtimeUpdated), so wie er galt, als der Launcher die lokale
/// Installation zuletzt als passend bestaetigt hat. Der Vergleich mit einem frisch
/// abgefragten Zeitstempel ist der Weg, eine neue Workshop-Version zu erkennen, auch
/// wenn Steams eigenes NeedsUpdate-Flag noch nichts davon weiss - siehe
/// SteamWorkshopService.GetServerUpdateTimestampsAsync.
/// </summary>
public sealed class ModVersionCache
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskForce11Launcher", "mod-versions.json");

    private Dictionary<ulong, uint> _timestamps = new();
    private bool _loaded;

    public uint? Get(ulong workshopId)
    {
        EnsureLoaded();
        return _timestamps.TryGetValue(workshopId, out var value) ? value : null;
    }

    public void Set(ulong workshopId, uint serverUpdatedAt)
    {
        EnsureLoaded();
        _timestamps[workshopId] = serverUpdatedAt;
        Save();
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        if (!File.Exists(CachePath)) return;

        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<ulong, uint>>(File.ReadAllText(CachePath));
            if (loaded is not null) _timestamps = loaded;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Ein kaputter Cache heisst nur, dass jeder Mod als "unbekannt" gilt und
            // gegen Steam neu geprueft wird - kein Grund, deswegen abzubrechen.
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(_timestamps, JsonOptions));
        }
        catch (IOException)
        {
        }
    }
}
