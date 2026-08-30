using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace TaskForce11Launcher.Services;

/// <summary>
/// HttpClient selbst cacht nichts, aber die Zwischenstationen tun es: raw.githubusercontent.com
/// liefert seine Antworten ueber ein CDN aus, das eine gerade gepushte Modliste sonst noch
/// minutenlang in der alten Fassung ausliefern kann. Explizite No-Cache-Header sorgen dafuer,
/// dass ein Modlisten-Update wirklich sofort bei allen ankommt.
/// </summary>
internal static class NoCacheHttp
{
    public static async Task<string> GetStringNoCacheAsync(this HttpClient http, string url, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
