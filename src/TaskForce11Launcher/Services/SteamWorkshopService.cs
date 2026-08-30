using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskForce11Launcher.Services;

public enum SubscribeOutcome
{
    /// <summary>Bereits abonniert, installiert und aktuell - es war nichts zu tun.</summary>
    AlreadyCurrent,

    /// <summary>Neu abonniert und/oder ein Update erfolgreich heruntergeladen.</summary>
    Success,

    TimedOut,
    SteamNotRunning
}

[Flags]
internal enum EItemState : uint
{
    None = 0,
    Subscribed = 1,
    LegacyItem = 2,
    Installed = 4,
    NeedsUpdate = 8,
    Downloading = 16,
    DownloadPending = 32,
    DisabledLocally = 64
}

internal enum ESteamAPIInitResult
{
    Ok = 0,
    FailedGeneric = 1,
    NoSteamClient = 2,
    VersionMismatch = 3
}

/// <summary>
/// Speicherlayout des Detail-Structs aus dem Steamworks SDK. Windows packt mit 8 Byte.
/// Verwendet werden nur m_nPublishedFileId, m_eResult und m_rtimeUpdated - der Rest muss
/// trotzdem exakt stimmen, weil sonst die Offsets dahinter verrutschen und Steam ueber
/// das Ende eines zu kleinen Puffers hinausschreibt.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct SteamUGCDetails_t
{
    public ulong m_nPublishedFileId;
    public int m_eResult;
    public int m_eFileType;
    public uint m_nCreatorAppID;
    public uint m_nConsumerAppID;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 129)]
    public byte[] m_rgchTitle;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8000)]
    public byte[] m_rgchDescription;
    public ulong m_ulSteamIDOwner;
    public uint m_rtimeCreated;
    public uint m_rtimeUpdated;
    public uint m_rtimeAddedToUserList;
    public int m_eVisibility;
    [MarshalAs(UnmanagedType.I1)]
    public bool m_bBanned;
    [MarshalAs(UnmanagedType.I1)]
    public bool m_bAcceptedForUse;
    [MarshalAs(UnmanagedType.I1)]
    public bool m_bTagsTruncated;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1025)]
    public byte[] m_rgchTags;
    public ulong m_hFile;
    public ulong m_hPreviewFile;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 260)]
    public byte[] m_pchFileName;
    public int m_nFileSize;
    public int m_nPreviewFileSize;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
    public byte[] m_rgchURL;
    public uint m_unVotesUp;
    public uint m_unVotesDown;
    public float m_flScore;
    public uint m_unNumChildren;
    public ulong m_ulTotalFilesSize;
}

/// <summary>
/// Spricht steam_api64.dll ueber deren "flache" C-Exporte an. Die klassischen Helfer
/// SteamAPI_Init()/SteamUGC() sind in SDKs ab etwa Mitte 2024 (hier v1.65) nur noch
/// Inline-Funktionen im C++-Header und lassen sich nicht mehr per Name aufrufen - uebrig
/// sind SteamAPI_InitFlat und die SteamAPI_ISteamUGC_*-Einstiegspunkte.
///
/// Damit abonniert und laedt der Launcher fehlende Workshop-Items direkt, ohne den
/// Spieler in den Browser oder das Steam-Overlay zu schicken. Voraussetzung: Steam
/// laeuft und der Account besitzt Arma 3 (AppID 107410, siehe steam_appid.txt neben
/// der EXE).
/// </summary>
public sealed class SteamWorkshopService : IDisposable
{
    private const string SteamApiDll = "steam_api64";

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int SteamAPI_InitFlat(StringBuilder? errMsg);

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SteamAPI_RunCallbacks();

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SteamAPI_Shutdown();

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SteamAPI_SteamUGC_v021();

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SteamAPI_SteamFriends_v018();

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SteamAPI_ISteamFriends_GetPersonaName(IntPtr self);

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SteamAPI_ISteamFriends_GetMediumFriendAvatar(IntPtr self, ulong steamIdFriend);

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SteamAPI_SteamUser_v023();

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SteamAPI_SteamUtils_v011();

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamUtils_GetImageSize(IntPtr self, int image, out uint width, out uint height);

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamUtils_GetImageRGBA(IntPtr self, int image, byte[] destBuffer, int destBufferSize);

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong SteamAPI_ISteamUGC_SubscribeItem(IntPtr self, ulong publishedFileId);

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint SteamAPI_ISteamUGC_GetItemState(IntPtr self, ulong publishedFileId);

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamUGC_DownloadItem(IntPtr self, ulong publishedFileId, [MarshalAs(UnmanagedType.U1)] bool highPriority);

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong SteamAPI_ISteamUGC_CreateQueryUGCDetailsRequest(IntPtr self, ulong[] pvecPublishedFileID, uint unNumPublishedFileIDs);

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong SteamAPI_ISteamUGC_SendQueryUGCRequest(IntPtr self, ulong handle);

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamUGC_GetQueryUGCResult(IntPtr self, ulong handle, uint index, out SteamUGCDetails_t pDetails);

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamUGC_ReleaseQueryUGCRequest(IntPtr self, ulong handle);

    [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamUtils_IsAPICallCompleted(IntPtr self, ulong hSteamAPICall, [MarshalAs(UnmanagedType.U1)] out bool pbFailed);

    private const ulong UgcQueryHandleInvalid = 0xffffffffffffffff;
    private const int EResultOk = 1;

    private Timer? _callbackPump;
    private IntPtr _ugc;
    private IntPtr _friends;
    private IntPtr _utils;

    public bool IsInitialized { get; private set; }

    public string? LastError { get; private set; }

    public bool Initialize()
    {
        try
        {
            var errMsg = new StringBuilder(1024);
            var result = (ESteamAPIInitResult)SteamAPI_InitFlat(errMsg);
            IsInitialized = result == ESteamAPIInitResult.Ok;
            LastError = IsInitialized
                ? null
                : (errMsg.Length > 0 ? errMsg.ToString() : DescribeInitResult(result));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            IsInitialized = false;
            LastError = ex.Message;
            return false;
        }

        if (!IsInitialized) return false;

        _ugc = SteamAPI_SteamUGC_v021();
        if (_ugc == IntPtr.Zero)
        {
            IsInitialized = false;
            LastError = "SteamUGC-Schnittstelle nicht verfügbar (Steam-Client zu alt?).";
            return false;
        }

        _friends = SteamAPI_SteamFriends_v018();
        _utils = SteamAPI_SteamUtils_v011();

        // Steam liefert Ergebnisse asynchroner Aufrufe nur aus, waehrend RunCallbacks
        // laeuft - ohne diese Pumpe wuerde die Detailabfrage unten nie fertig.
        _callbackPump = new Timer(_ => SteamAPI_RunCallbacks(), null, 0, 100);

        return true;
    }

    private static string DescribeInitResult(ESteamAPIInitResult result) => result switch
    {
        ESteamAPIInitResult.NoSteamClient => "Steam läuft nicht - bitte Steam starten und einloggen.",
        ESteamAPIInitResult.VersionMismatch => "Steam-Client ist veraltet - bitte Steam aktualisieren.",
        _ => "Steam konnte nicht angesprochen werden."
    };

    public string? GetPersonaName()
    {
        if (!IsInitialized || _friends == IntPtr.Zero) return null;

        var ptr = SteamAPI_ISteamFriends_GetPersonaName(_friends);
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
    }

    /// <summary>
    /// Das eigene Profilbild haelt der Steam-Client bereits vor - anders als die von
    /// Freunden ist es direkt nach der Initialisierung da, ohne auf einen
    /// AvatarImageLoaded_t-Callback warten zu muessen.
    /// </summary>
    public BitmapSource? GetAvatarImage()
    {
        if (!IsInitialized || _friends == IntPtr.Zero || _utils == IntPtr.Zero) return null;

        try
        {
            var user = SteamAPI_SteamUser_v023();
            if (user == IntPtr.Zero) return null;

            var steamId = SteamAPI_ISteamUser_GetSteamID(user);
            var imageHandle = SteamAPI_ISteamFriends_GetMediumFriendAvatar(_friends, steamId);
            if (imageHandle <= 0) return null;

            if (!SteamAPI_ISteamUtils_GetImageSize(_utils, imageHandle, out var width, out var height)
                || width == 0 || height == 0)
            {
                return null;
            }

            var buffer = new byte[width * height * 4];
            if (!SteamAPI_ISteamUtils_GetImageRGBA(_utils, imageHandle, buffer, buffer.Length)) return null;

            // Steam liefert RGBA, WPF erwartet BGRA - Rot und Blau tauschen.
            for (var i = 0; i < buffer.Length; i += 4)
            {
                (buffer[i], buffer[i + 2]) = (buffer[i + 2], buffer[i]);
            }

            var bitmap = BitmapSource.Create((int)width, (int)height, 96, 96, PixelFormats.Bgra32, null, buffer, (int)width * 4);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            // Das Profilbild ist reine Kosmetik in der Titelleiste.
            return null;
        }
    }

    public async Task<SubscribeOutcome> SubscribeAndInstallAsync(
        ulong workshopId,
        string installPath,
        TimeSpan timeout,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!IsInitialized) return SubscribeOutcome.SteamNotRunning;

        // Schnellweg: ein vorhandener Ordner heisst nicht, dass es die aktuelle Version
        // ist - Steam aktualisiert Abos nach eigenem Zeitplan, der bis zum Klick auf
        // START nicht gelaufen sein muss. Diese eine billige Abfrage vorweg erspart es,
        // fuer jeden der 50 Mods bei jedem Start das volle Abo-und-Warte-Verfahren zu
        // durchlaufen.
        var currentState = (EItemState)SteamAPI_ISteamUGC_GetItemState(_ugc, workshopId);
        if (currentState.HasFlag(EItemState.Installed)
            && !currentState.HasFlag(EItemState.NeedsUpdate)
            && ModManager.HasContent(installPath))
        {
            return SubscribeOutcome.AlreadyCurrent;
        }

        SteamAPI_ISteamUGC_SubscribeItem(_ugc, workshopId);
        progress?.Report($"Abonniere {workshopId}…");

        var deadline = DateTime.UtcNow + timeout;
        var downloadTriggered = false;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var state = (EItemState)SteamAPI_ISteamUGC_GetItemState(_ugc, workshopId);

            // Das Installed-Flag spiegelt Steams eigene Buchfuehrung wider, keine
            // Pruefung der Festplatte - es kann "installiert und aktuell" melden, bevor
            // tatsaechlich Inhalt angekommen ist. Erst der Blick auf echte Dateien ist
            // die verlaessliche Aussage.
            if (IsFullyInstalled(state) && ModManager.HasContent(installPath))
            {
                return SubscribeOutcome.Success;
            }

            if (state.HasFlag(EItemState.Subscribed) && !downloadTriggered)
            {
                SteamAPI_ISteamUGC_DownloadItem(_ugc, workshopId, true);
                downloadTriggered = true;
                progress?.Report($"Lade {workshopId} herunter…");
            }

            await Task.Delay(500, ct);
        }

        return SubscribeOutcome.TimedOut;
    }

    /// <summary>
    /// Erzwingt den kompletten Neu-Download eines Items, egal was Steams Zustandsverwaltung
    /// meint - im Gegensatz zu SubscribeAndInstallAsync ohne den "schon aktuell"-Schnellweg,
    /// denn genau darum geht es hier: eine lokale Beschaedigung zu reparieren, von der
    /// Steams Buchfuehrung nichts weiss. Der Aufrufer hat den lokalen Ordner vorher
    /// geloescht, damit nichts Altes danebenliegen bleibt.
    /// </summary>
    public async Task<SubscribeOutcome> ForceRedownloadAsync(
        ulong workshopId,
        string installPath,
        TimeSpan timeout,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!IsInitialized) return SubscribeOutcome.SteamNotRunning;

        SteamAPI_ISteamUGC_SubscribeItem(_ugc, workshopId);
        SteamAPI_ISteamUGC_DownloadItem(_ugc, workshopId, true);
        progress?.Report($"Lade {workshopId} neu herunter…");

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var state = (EItemState)SteamAPI_ISteamUGC_GetItemState(_ugc, workshopId);

            // Dieselbe Anforderung an echte Dateien wie oben: unmittelbar nach dem
            // Loeschen des Ordners meldet Steam sonst "fertig", bevor irgendetwas neu
            // geladen wurde.
            if (IsFullyInstalled(state) && ModManager.HasContent(installPath))
            {
                return SubscribeOutcome.Success;
            }

            await Task.Delay(500, ct);
        }

        return SubscribeOutcome.TimedOut;
    }

    private static bool IsFullyInstalled(EItemState state) =>
        state.HasFlag(EItemState.Installed)
        && !state.HasFlag(EItemState.NeedsUpdate)
        && !state.HasFlag(EItemState.Downloading)
        && !state.HasFlag(EItemState.DownloadPending);

    /// <summary>
    /// Fragt Steams Backend direkt - nicht den lokalen Client-Zustand - nach dem letzten
    /// Aenderungszeitpunkt der uebergebenen Items. Das NeedsUpdate-Flag von GetItemState
    /// weiss nur, was der Client zufaellig schon mitbekommen hat, und das haengt daran, ob
    /// dessen eigener Abo-Abgleich fuer genau dieses Item kuerzlich gelaufen ist. Hier geht
    /// stattdessen eine echte Anfrage raus - gebuendelt fuer die ganze Modliste statt
    /// einzeln pro Mod. Zurueck kommt, was bis zum Timeout aufgeloest werden konnte;
    /// fehlende Eintraege bedeuten "unbekannt", nicht "unveraendert".
    /// </summary>
    public async Task<Dictionary<ulong, uint>> GetServerUpdateTimestampsAsync(
        IReadOnlyList<ulong> workshopIds,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var result = new Dictionary<ulong, uint>();
        if (!IsInitialized || workshopIds.Count == 0) return result;

        var handle = SteamAPI_ISteamUGC_CreateQueryUGCDetailsRequest(_ugc, workshopIds.ToArray(), (uint)workshopIds.Count);
        if (handle == UgcQueryHandleInvalid) return result;

        try
        {
            var apiCall = SteamAPI_ISteamUGC_SendQueryUGCRequest(_ugc, handle);

            var deadline = DateTime.UtcNow + timeout;
            var completed = false;

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                if (SteamAPI_ISteamUtils_IsAPICallCompleted(_utils, apiCall, out var failed))
                {
                    completed = !failed;
                    break;
                }

                await Task.Delay(200, ct);
            }

            if (!completed) return result;

            for (uint i = 0; i < workshopIds.Count; i++)
            {
                if (SteamAPI_ISteamUGC_GetQueryUGCResult(_ugc, handle, i, out var details)
                    && details.m_eResult == EResultOk)
                {
                    result[details.m_nPublishedFileId] = details.m_rtimeUpdated;
                }
            }
        }
        finally
        {
            SteamAPI_ISteamUGC_ReleaseQueryUGCRequest(_ugc, handle);
        }

        return result;
    }

    /// <summary>
    /// Beendet die Steam-Sitzung wieder. Steams Overlay haengt sich in jeden Prozess mit
    /// laufender Sitzung ein (es gibt keine offizielle Moeglichkeit, sich davon
    /// abzumelden) - deshalb haelt der Launcher eine Sitzung nur so lange offen, wie er
    /// sie wirklich braucht, statt ueber seine gesamte Laufzeit.
    /// </summary>
    public void Shutdown()
    {
        if (_callbackPump is not null)
        {
            // Timer.Dispose() ohne Argument wartet nicht darauf, dass ein gerade laufender
            // Callback (SteamAPI_RunCallbacks auf einem Threadpool-Thread) fertig wird.
            // SteamAPI_Shutdown() waehrenddessen aufzurufen ist ein Wettlauf, der in Steams
            // eigener DLL mit einer Zugriffsverletzung endet. Dispose(WaitHandle) blockiert,
            // bis der laufende Callback zurueckgekehrt ist.
            using var timerStopped = new ManualResetEvent(false);
            _callbackPump.Dispose(timerStopped);
            timerStopped.WaitOne();
            _callbackPump = null;
        }

        if (IsInitialized)
        {
            SteamAPI_Shutdown();
        }

        IsInitialized = false;
        _ugc = IntPtr.Zero;
        _friends = IntPtr.Zero;
        _utils = IntPtr.Zero;
    }

    public void Dispose() => Shutdown();
}
