namespace TaskForce11Launcher.Models;

/// <summary>
/// Inhalt von data/serverdata.json im Einheits-Repo. Bewusst alles, was sich aendern
/// kann, ohne dass der Launcher neu gebaut werden muss.
/// </summary>
public sealed class ServerData
{
    public ArmaServerInfo Arma3 { get; set; } = new();

    public TeamspeakInfo Teamspeak { get; set; } = new();

    /// <summary>Freitext für die Anzeige, z. B. "Sonntag 16:00".</summary>
    public string MissionTime { get; set; } = string.Empty;

    public string LauncherBackgroundUrl { get; set; } = string.Empty;
}

public sealed class ArmaServerInfo
{
    /// <summary>
    /// Hostname oder IP. Ein Hostname ist der Normalfall - zieht der Server um, reicht
    /// eine DNS-Änderung und niemand braucht ein Launcher-Update.
    /// </summary>
    public string Ip { get; set; } = string.Empty;

    public int Port { get; set; } = 2302;

    public string Password { get; set; } = string.Empty;
}

public sealed class TeamspeakInfo
{
    public string Host { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
