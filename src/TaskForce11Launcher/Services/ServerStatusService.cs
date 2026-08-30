using System.Net.Sockets;
using System.Threading;

namespace TaskForce11Launcher.Services;

public readonly record struct Arma3ServerStatus(bool IsOnline, int? Players, int? MaxPlayers);

/// <summary>
/// Kurze "laeuft der Server?"-Abfrage fuer die Anzeige - kein vollwertiges Monitoring.
/// </summary>
public sealed class ServerStatusService
{
    /// <summary>
    /// Arma 3 beantwortet - wie alle Source-Query-kompatiblen Server - eine
    /// UDP-A2S_INFO-Anfrage standardmaessig auf gamePort + 1. Die Antwort enthaelt nach
    /// dem 0xFFFFFFFF-Praefix und dem 'I'-Header vier nullterminierte Strings (Name,
    /// Karte, Ordner, Spiel) und eine 2-Byte-AppID, danach kommen aktuelle und maximale
    /// Spielerzahl.
    ///
    /// Aktuelle Server stellen dieser Antwort ein Challenge-Response voran - eine
    /// Massnahme gegen DDoS-Reflection, die Valve nachtraeglich eingefuehrt hat. Die
    /// erste Antwort ist dann nur ein 'A' (0x41) plus 4-Byte-Challenge statt der echten
    /// 'I'-Antwort (0x49); der Client muss dieselbe Anfrage mit angehaengter Challenge
    /// erneut senden, um die eigentlichen Daten zu bekommen.
    /// </summary>
    public static async Task<Arma3ServerStatus> CheckArma3Async(string ip, int gamePort, TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            using var udp = new UdpClient();
            var baseRequest = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }
                .Concat("TSource Engine Query\0"u8.ToArray())
                .ToArray();

            await udp.SendAsync(baseRequest, baseRequest.Length, ip, gamePort + 1);
            var buffer = await ReceiveAsync(udp, timeout, ct);
            if (buffer is null || !HasValidPrefix(buffer)) return new Arma3ServerStatus(false, null, null);

            if (buffer.Length >= 9 && buffer[4] == 0x41)
            {
                var challengedRequest = baseRequest.Concat(buffer[5..9]).ToArray();
                await udp.SendAsync(challengedRequest, challengedRequest.Length, ip, gamePort + 1);

                // Die erste Antwort hat bereits bewiesen, dass der Server erreichbar ist.
                // Geht dieser zweite Roundtrip daneben, kostet das nur die Spielerzahl -
                // der Server darf deswegen nicht als offline angezeigt werden.
                var infoResponse = await ReceiveAsync(udp, timeout, ct);
                if (infoResponse is not null && HasValidPrefix(infoResponse)) buffer = infoResponse;
            }

            var (players, maxPlayers) = ParsePlayerCounts(buffer);
            return new Arma3ServerStatus(true, players, maxPlayers);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return new Arma3ServerStatus(false, null, null);
        }
    }

    private static async Task<byte[]?> ReceiveAsync(UdpClient udp, TimeSpan timeout, CancellationToken ct)
    {
        var receiveTask = udp.ReceiveAsync(ct).AsTask();
        var completed = await Task.WhenAny(receiveTask, Task.Delay(timeout, ct));

        if (completed == receiveTask && receiveTask.IsCompletedSuccessfully) return receiveTask.Result.Buffer;

        // Beim Timeout laeuft der Empfang noch. Sobald der Aufrufer den UdpClient
        // verwirft, bricht er mit einer ObjectDisposedException ab - unbeobachtet waere
        // das eine TaskScheduler.UnobservedTaskException, und die reisst den Prozess
        // ueber den Finalizer-Thread mit, obwohl hier nichts Schlimmes passiert ist.
        // Block-Lambda, kein Ausdruck: "t => _ = t.Exception" hat einen Wert und passt
        // damit auf ContinueWith(Action<...>) wie auf ContinueWith<TResult>(Func<...>) -
        // der Aufruf waere mehrdeutig.
        _ = receiveTask.ContinueWith(
            t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return null;
    }

    private static bool HasValidPrefix(byte[] buffer) =>
        buffer.Length > 4 && buffer[0] == 0xFF && buffer[1] == 0xFF && buffer[2] == 0xFF && buffer[3] == 0xFF;

    private static (int? Players, int? MaxPlayers) ParsePlayerCounts(byte[] buffer)
    {
        var pos = 4;
        if (pos >= buffer.Length || buffer[pos] != 0x49) return (null, null); // 'I' = S2A_INFO
        pos += 2; // Header-Byte + Protokollversion

        for (var i = 0; i < 4; i++) // Name, Karte, Ordner, Spiel
        {
            while (pos < buffer.Length && buffer[pos] != 0) pos++;
            pos++;
        }

        pos += 2; // Steam-AppID (short)
        if (pos + 1 >= buffer.Length) return (null, null);

        return (buffer[pos], buffer[pos + 1]);
    }
}
