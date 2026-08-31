namespace TaskForce11Launcher.Models;

public sealed class AppSettings
{
    public string? SteamPath { get; set; }

    public string? Arma3Path { get; set; }

    /// <summary>Vollständiger Pfad zur ts3client_win64.exe (oder _win32).</summary>
    public string? TeamspeakPath { get; set; }

    /// <summary>
    /// Ob Arma 3 nach dem Start direkt mit dem Einheitsserver verbinden soll. Aus, wenn
    /// jemand nur im Editor arbeiten oder eine andere Mission spielen will - die Mods
    /// werden trotzdem geprüft und geladen.
    /// </summary>
    public bool ConnectToServer { get; set; } = true;

    /// <summary>Feed mit den Pflichtmods - zeigt auf data/workshop.json im Einheits-Repo.</summary>
    public string ModlistUrl { get; set; } = string.Empty;

    /// <summary>Server-IP/Port und Hintergrundbild - zeigt auf data/serverdata.json im Einheits-Repo.</summary>
    public string ServerDataUrl { get; set; } = string.Empty;

    /// <summary>Quelle fuer den Auto-Updater (GitHub Releases).</summary>
    public string GithubRepoUrl { get; set; } = string.Empty;
}
