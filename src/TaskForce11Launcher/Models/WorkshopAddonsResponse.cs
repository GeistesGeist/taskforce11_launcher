namespace TaskForce11Launcher.Models;

/// <summary>
/// Form von data/workshop.json. presetName/updated sind rein informativ (sie stehen
/// im Feed, damit man einer heruntergeladenen Liste ansieht, aus welchem Preset sie
/// stammt) - fuer den Launcher zaehlt nur workshopAddons.
/// </summary>
public sealed class WorkshopAddonsResponse
{
    public string PresetName { get; set; } = string.Empty;

    public string Updated { get; set; } = string.Empty;

    public List<WorkshopAddon> WorkshopAddons { get; set; } = new();
}

public sealed class WorkshopAddon
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
