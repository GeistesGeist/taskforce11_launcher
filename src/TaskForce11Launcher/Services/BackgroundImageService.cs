using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows.Media.Imaging;

namespace TaskForce11Launcher.Services;

/// <summary>
/// Laedt das in serverdata.json hinterlegte Hintergrundbild und legt es lokal ab, damit
/// der Launcher es beim naechsten Start sofort zeigen kann - statt erst nach dem
/// Netzwerk-Roundtrip, mit einem leeren Fenster davor.
/// </summary>
public sealed class BackgroundImageService
{
    private static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskForce11Launcher", "cache");

    private static string CachePath => Path.Combine(CacheDir, "background.img");

    private readonly HttpClient _http;

    public BackgroundImageService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Das zuletzt gespeicherte Hintergrundbild, oder null. Bewusst statisch: auch das
    /// Update-Fenster braucht es, und das laeuft, bevor irgendein Dienst erzeugt wurde.
    /// </summary>
    public static BitmapImage? LoadCached()
    {
        if (!File.Exists(CachePath)) return null;

        try
        {
            return CreateFrozenBitmap(File.ReadAllBytes(CachePath));
        }
        catch
        {
            // Ein unlesbares oder halb geschriebenes Cache-Bild ist kein Fehler, der die
            // Oberflaeche betrifft - dann bleibt eben der Farbverlauf stehen.
            return null;
        }
    }

    public async Task<BitmapImage?> FetchAndCacheAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var bytes = await _http.GetByteArrayAsync(url, ct);
        var bitmap = CreateFrozenBitmap(bytes);

        Directory.CreateDirectory(CacheDir);
        await File.WriteAllBytesAsync(CachePath, bytes, ct);

        return bitmap;
    }

    /// <summary>
    /// Eingefroren, weil das Bild auf einem Hintergrund-Thread entsteht und danach an die
    /// Oberflaeche gebunden wird - ein nicht eingefrorenes BitmapImage gehoert dem
    /// erzeugenden Thread und waere dort nicht verwendbar.
    /// </summary>
    private static BitmapImage CreateFrozenBitmap(byte[] bytes)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(bytes);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
