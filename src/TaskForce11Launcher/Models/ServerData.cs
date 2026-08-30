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

    /// <summary>Passwort des Sprachservers (nicht das der ServerQuery).</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Sprachport - wird gebraucht, um die ServerQuery auf den richtigen virtuellen Server zu schalten.</summary>
    public int VoicePort { get; set; } = 9987;

    /// <summary>
    /// ServerQuery-Zugang für die Nutzerzahl. Bleibt einer der drei Werte leer, zeigt der
    /// Launcher nur die Adresse und keinen Status - er versucht dann gar nicht erst zu
    /// verbinden.
    /// </summary>
    public int QueryPort { get; set; } = 10011;

    public string QueryUser { get; set; } = string.Empty;

    public string QueryPassword { get; set; } = string.Empty;

    public bool HasQueryAccess =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(QueryUser)
        && !string.IsNullOrWhiteSpace(QueryPassword);
}
