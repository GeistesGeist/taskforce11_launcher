using System.Net.Http;
using System.Text.Json;
using System.Threading;
using TaskForce11Launcher.Models;

namespace TaskForce11Launcher.Services;

/// <summary>Die geladene Modliste samt Herkunftsangabe aus dem Feed.</summary>
public sealed record Modlist(string PresetName, string Updated, IReadOnlyList<ModEntry> Mods);

/// <summary>
/// Laedt die Pflichtmodliste (data/workshop.json aus dem Einheits-Repo) und macht daraus
/// ModEntry-Objekte. Der Feed wird aus dem Arma-3-Preset erzeugt (tools/Convert-Preset.ps1),
/// die Reihenfolge im JSON ist also die Ladereihenfolge des Presets - und die bleibt hier
/// erhalten, weil Arma Mods in genau der Reihenfolge laedt, in der sie per -mod= kommen.
/// </summary>
public sealed class ModlistService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public ModlistService(HttpClient http)
    {
        _http = http;
    }

    public async Task<Modlist> FetchAsync(string url, CancellationToken ct = default)
    {
        var json = await _http.GetStringNoCacheAsync(url, ct);
        return Parse(json);
    }

    public static Modlist Parse(string json)
    {
        var response = JsonSerializer.Deserialize<WorkshopAddonsResponse>(json, JsonOptions);
        if (response?.WorkshopAddons is null)
        {
            return new Modlist(string.Empty, string.Empty, Array.Empty<ModEntry>());
        }

        var mods = new List<ModEntry>();
        var seenIds = new HashSet<ulong>();

        foreach (var addon in response.WorkshopAddons)
        {
            if (!ulong.TryParse(addon.Id, out var workshopId)) continue;

            // Ein doppelter Eintrag im Feed wuerde denselben Mod zweimal pruefen und
            // zweimal per -mod= an Arma uebergeben.
            if (!seenIds.Add(workshopId)) continue;

            var name = string.IsNullOrWhiteSpace(addon.Name) ? workshopId.ToString() : addon.Name.Trim();
            mods.Add(new ModEntry(name, workshopId));
        }

        return new Modlist(response.PresetName, response.Updated, mods);
    }
}
