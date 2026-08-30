namespace TaskForce11Launcher.Models;

public sealed class AppSettings
{
    public string? SteamPath { get; set; }

    public string? Arma3Path { get; set; }

    /// <summary>Feed mit den Pflichtmods - zeigt auf data/workshop.json im Einheits-Repo.</summary>
    public string ModlistUrl { get; set; } = string.Empty;

    /// <summary>Server-IP/Port und Hintergrundbild - zeigt auf data/serverdata.json im Einheits-Repo.</summary>
    public string ServerDataUrl { get; set; } = string.Empty;

    /// <summary>Quelle fuer den Auto-Updater (GitHub Releases).</summary>
    public string GithubRepoUrl { get; set; } = string.Empty;
}
