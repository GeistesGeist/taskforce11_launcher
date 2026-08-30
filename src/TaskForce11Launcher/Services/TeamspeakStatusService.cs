using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TaskForce11Launcher.Models;

namespace TaskForce11Launcher.Services;

public readonly record struct TeamspeakStatus(bool IsOnline, int? Clients, int? MaxClients);

/// <summary>
/// Fragt die Nutzerzahl des TeamSpeak-Servers über dessen ServerQuery-Schnittstelle ab
/// (Klartextprotokoll auf TCP, standardmäßig Port 10011).
///
/// Der Ablauf ist knapp gehalten - verbinden, anmelden, auf den virtuellen Server
/// schalten, eine Abfrage, abmelden. Das ist Absicht: TeamSpeak hat eine
/// Flood-Protection, die eine IP nach zu vielen Befehlen in kurzer Folge sperrt. Bei
/// einem Abruf pro Minute und vier Befehlen bleibt das weit darunter, aber jeder
/// zusätzliche Befehl geht auf dasselbe Budget.
/// </summary>
public sealed class TeamspeakStatusService
{
    public static async Task<TeamspeakStatus> CheckAsync(
        TeamspeakInfo info,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (!info.HasQueryAccess) return new TeamspeakStatus(false, null, null);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(info.Host, info.QueryPort, linked.Token);

            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            // Begrüßung abholen: "TS3" plus eine Hinweiszeile - erst danach nimmt der
            // Server Befehle entgegen.
            await reader.ReadLineAsync(linked.Token);
            await reader.ReadLineAsync(linked.Token);

            if (!await SendAsync(writer, reader, $"login {Escape(info.QueryUser)} {Escape(info.QueryPassword)}", linked.Token))
            {
                return new TeamspeakStatus(false, null, null);
            }

            // Ein TeamSpeak-Prozess kann mehrere virtuelle Server bedienen - erst die
            // Auswahl über den Sprachport bestimmt, welcher gemeint ist.
            if (!await SendAsync(writer, reader, $"use port={info.VoicePort}", linked.Token))
            {
                return new TeamspeakStatus(false, null, null);
            }

            var payload = await QueryAsync(writer, reader, "serverinfo", linked.Token);
            await writer.WriteAsync("quit\n");

            if (payload is null) return new TeamspeakStatus(false, null, null);

            var online = ParseInt(payload, "virtualserver_clientsonline");
            var queryClients = ParseInt(payload, "virtualserver_queryclientsonline") ?? 0;
            var max = ParseInt(payload, "virtualserver_maxclients");

            // Die Query-Verbindungen zählen in clientsonline mit - auch die eigene. Wer
            // "3 online" liest, meint drei Leute im Sprachchat, nicht zwei Leute plus
            // diesen Launcher.
            var people = online is null ? null : (int?)Math.Max(0, online.Value - queryClients);

            return new TeamspeakStatus(true, people, max);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            // Server aus, Port dicht, Zugangsdaten falsch oder IP durch die
            // Flood-Protection gesperrt - für die Anzeige ist das alles dasselbe.
            return new TeamspeakStatus(false, null, null);
        }
    }

    /// <summary>Schickt einen Befehl und meldet nur, ob er ohne Fehler durchlief.</summary>
    private static async Task<bool> SendAsync(TextWriter writer, TextReader reader, string command, CancellationToken ct)
    {
        await writer.WriteAsync($"{command}\n");

        var line = await ReadUntilErrorLineAsync(reader, ct);
        return line is not null && line.Contains("error id=0", StringComparison.Ordinal);
    }

    /// <summary>
    /// Schickt eine Abfrage und liefert deren Nutzdatenzeile - also alles vor der
    /// abschließenden "error id=..."-Zeile.
    /// </summary>
    private static async Task<string?> QueryAsync(TextWriter writer, TextReader reader, string command, CancellationToken ct)
    {
        await writer.WriteAsync($"{command}\n");

        string? payload = null;
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) return null;

            var trimmed = line.Trim('\r', '\n', ' ');
            if (trimmed.Length == 0) continue;

            if (trimmed.StartsWith("error ", StringComparison.Ordinal))
            {
                return trimmed.Contains("error id=0", StringComparison.Ordinal) ? payload : null;
            }

            payload = trimmed;
        }
    }

    private static async Task<string?> ReadUntilErrorLineAsync(TextReader reader, CancellationToken ct)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) return null;

            var trimmed = line.Trim('\r', '\n', ' ');
            if (trimmed.StartsWith("error ", StringComparison.Ordinal)) return trimmed;
        }
    }

    private static int? ParseInt(string payload, string key)
    {
        // Die Antwort ist eine Kette aus "schluessel=wert", getrennt durch Leerzeichen.
        foreach (var pair in payload.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0) continue;
            if (!pair.AsSpan(0, separator).SequenceEqual(key)) continue;

            return int.TryParse(pair.AsSpan(separator + 1), out var value) ? value : null;
        }

        return null;
    }

    /// <summary>
    /// ServerQuery trennt Parameter mit Leerzeichen, Sonderzeichen müssen deshalb
    /// maskiert werden - sonst zerfällt ein Passwort mit Leerzeichen in zwei Argumente
    /// und die Anmeldung scheitert aus scheinbar unerklärlichem Grund.
    /// </summary>
    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace("/", "\\/")
        .Replace(" ", "\\s")
        .Replace("|", "\\p")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\t", "\\t");
}
