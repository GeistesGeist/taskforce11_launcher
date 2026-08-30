using System.Diagnostics;
using System.IO;
using TaskForce11Launcher.Models;

namespace TaskForce11Launcher.Services;

public sealed class GameLauncherService
{
    /// <summary>
    /// Startet Arma 3 direkt (ohne den Umweg ueber Steams Startoptionen) mit den Flags
    /// fuer einen schnellen Kaltstart, dem aufgeloesten Modsatz und - sofern Serverdaten
    /// vorliegen - einem Direct Connect.
    /// </summary>
    public static Process? Launch(string arma3ExePath, IEnumerable<string> modPaths, ArmaServerInfo? server)
    {
        var args = new List<string> { "-noSplash", "-skipIntro", "-noPause", "-noLogs", "-hugePages" };

        // Ein -mod= pro Pfad statt einer einzelnen semikolongetrennten Liste: so muss
        // kein Semikolon in Anfuehrungszeichen ueberleben, und jeder Pfad bekommt seine
        // eigenen Quotes - noetig, sobald eine Steam-Bibliothek in einem Ordner mit
        // Leerzeichen liegt ("Program Files (x86)").
        foreach (var modPath in modPaths)
        {
            args.Add($"-mod=\"{modPath}\"");
        }

        if (server is not null && !string.IsNullOrWhiteSpace(server.Ip))
        {
            args.Add($"-connect={server.Ip}");
            args.Add($"-port={server.Port}");
            if (!string.IsNullOrWhiteSpace(server.Password))
            {
                args.Add($"-password=\"{server.Password}\"");
            }
        }

        return Process.Start(new ProcessStartInfo(arma3ExePath, string.Join(' ', args))
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(arma3ExePath) ?? string.Empty
        });
    }

    /// <summary>
    /// Bevorzugt die 64-Bit-Variante; die 32-Bit-EXE ist nur der Rueckfall fuer
    /// Installationen, in denen sie fehlt.
    /// </summary>
    public static string? ResolveArma3Exe(string arma3Path)
    {
        var x64 = Path.Combine(arma3Path, "arma3_x64.exe");
        if (File.Exists(x64)) return x64;

        var x86 = Path.Combine(arma3Path, "arma3.exe");
        return File.Exists(x86) ? x86 : null;
    }
}
