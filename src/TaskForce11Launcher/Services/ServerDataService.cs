using System.Net.Http;
using System.Text.Json;
using System.Threading;
using TaskForce11Launcher.Models;

namespace TaskForce11Launcher.Services;

/// <summary>
/// Laedt data/serverdata.json aus dem Einheits-Repo: Serveradresse fuer den Direct
/// Connect und die URL des Launcher-Hintergrundbilds. Beides liegt bewusst nicht in der
/// mitgelieferten Konfiguration, damit ein Serverumzug oder ein neues Hintergrundbild
/// nur einen Commit kostet und kein neues Launcher-Release.
/// </summary>
public sealed class ServerDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public ServerDataService(HttpClient http)
    {
        _http = http;
    }

    public async Task<ServerData> FetchAsync(string url, CancellationToken ct = default)
    {
        var json = await _http.GetStringNoCacheAsync(url, ct);
        var data = JsonSerializer.Deserialize<ServerData>(json, JsonOptions) ?? new ServerData();

        // "ip" wird erfahrungsgemaess gerne als "host:port" eingetragen, obwohl "port"
        // daneben schon existiert - den Port abschneiden, sonst steht er doppelt im
        // -connect=/-port=-Paar und Arma verbindet ins Leere.
        data.Arma3.Ip = StripPort(data.Arma3.Ip.Trim());

        // Ein mitkopiertes Leerzeichen im Passwort ist unsichtbar und laesst den Beitritt
        // ohne erkennbaren Grund scheitern.
        data.Arma3.Password = data.Arma3.Password.Trim();

        data.Teamspeak.Host = data.Teamspeak.Host.Trim();
        data.Teamspeak.Password = data.Teamspeak.Password.Trim();
        data.MissionTime = data.MissionTime.Trim();

        return data;
    }

    private static string StripPort(string hostMaybeWithPort)
    {
        var idx = hostMaybeWithPort.LastIndexOf(':');
        if (idx > 0 && idx < hostMaybeWithPort.Length - 1 && hostMaybeWithPort[(idx + 1)..].All(char.IsDigit))
        {
            return hostMaybeWithPort[..idx];
        }

        return hostMaybeWithPort;
    }
}
