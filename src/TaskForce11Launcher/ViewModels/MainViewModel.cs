using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskForce11Launcher.Models;
using TaskForce11Launcher.Services;

namespace TaskForce11Launcher.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly HttpClient _http = new();
    private readonly SettingsService _settingsService = new();
    private readonly PathDetectionService _pathDetection = new();
    private readonly ModlistService _modlistService;
    private readonly ServerDataService _serverDataService;
    private readonly SteamWorkshopService _steamWorkshop = new();
    private readonly BackgroundImageService _backgroundImageService;
    private readonly ModVersionCache _modVersionCache = new();
    private readonly UpdateService _updateService;

    private readonly CancellationTokenSource _shutdownCts = new();

    private AppSettings _settings;
    private string? _workshopContentPath;
    private ServerData? _serverData;

    public MainViewModel()
    {
        _modlistService = new ModlistService(_http);
        _serverDataService = new ServerDataService(_http);
        _backgroundImageService = new BackgroundImageService(_http);
        _settings = _settingsService.Load();

        _updateService = new UpdateService(_settings.GithubRepoUrl);
        ConnectToServer = _settings.ConnectToServer;

        DetectPaths();
        _ = LoadServerDataAndBackgroundAsync();
        _ = InitializeSteamAsync();
        _ = ServerStatusLoopAsync(_shutdownCts.Token);
        _ = UpdateCheckLoopAsync(_shutdownCts.Token);
        _ = CheckModsAsync();
    }

    public ObservableCollection<ModStatusItem> Mods { get; } = new();

    // Kommt aus dem -p:Version des Builds (siehe build.yml). Assembly.Location taugt
    // dafuer nicht, weil der Single-File-Publish keine Datei auf der Platte hinterlaesst -
    // das hier ist eingebettete Metadata und funktioniert in beiden Faellen.
    public string AppVersion => "v" + (Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev");

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private string _statusText = "Bereit.";

    [ObservableProperty]
    private bool _isBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanLaunch));

    // PrepareAndLaunchAsync ruft CheckModsAsync und SubscribeAndUpdateModsAsync auf, die
    // beide selbst "beschaeftigt" melden. Wuerde jede davon IsBusy am Ende schlicht auf
    // false setzen, waere der START-Knopf zwischen den Schritten kurz wieder anklickbar -
    // und ein zweiter Klick wuerde einen zweiten Startlauf danebenlegen. Ein Zaehler statt
    // eines Schalters loest das: frei ist erst, wenn der aeusserste Vorgang fertig ist.
    private int _busyDepth;

    private void EnterBusy()
    {
        if (++_busyDepth == 1) IsBusy = true;
    }

    private void ExitBusy()
    {
        if (--_busyDepth <= 0)
        {
            _busyDepth = 0;
            IsBusy = false;
        }
    }

    [ObservableProperty]
    private bool _isArma3Running;

    partial void OnIsArma3RunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(StartButtonLabel));
    }

    public bool CanLaunch => !IsBusy && !IsArma3Running;

    public string StartButtonLabel => IsArma3Running ? "LÄUFT" : "START";

    [ObservableProperty]
    private string? _arma3Path;

    [ObservableProperty]
    private ImageSource? _backgroundImageSource;

    [ObservableProperty]
    private string? _steamUserName;

    [ObservableProperty]
    private ImageSource? _steamAvatar;

    [ObservableProperty]
    private bool? _arma3ServerOnline;

    /// <summary>
    /// Spielerzahl als "7/50". Steht von Anfang an da - vor der ersten Abfrage als
    /// Strichpaar, damit die Zeile nicht erst leer ist und dann plötzlich breiter wird.
    /// </summary>
    [ObservableProperty]
    private string _arma3PlayerCount = UnknownCount;

    /// <summary>Platzhalter, solange keine Zahlen vorliegen (Gedankenstriche, kein Bindestrich).</summary>
    private const string UnknownCount = "–/–";

    /// <summary>Aus welchem Preset die geladene Modliste stammt - fuer die Kopfzeile der Modliste.</summary>
    [ObservableProperty]
    private string? _modlistLabel;

    /// <summary>Naechster Missionstermin laut serverdata.json, z. B. "Sonntag 16:00".</summary>
    [ObservableProperty]
    private string? _missionTime;

    /// <summary>Adresse des TeamSpeak-Servers; null blendet den TS-Knopf aus.</summary>
    [ObservableProperty]
    private string? _teamspeakHost;

    /// <summary>true = erreichbar, false = nicht erreichbar, null = noch nicht geprüft.</summary>
    [ObservableProperty]
    private bool? _teamspeakOnline;

    /// <summary>Belegte von maximalen Plätzen im TeamSpeak, z. B. "7/32".</summary>
    [ObservableProperty]
    private string? _teamspeakUserCount;

    /// <summary>
    /// Version eines heruntergeladenen, noch nicht eingespielten Updates - null, solange
    /// nichts bereitliegt. Steuert den Hinweis in der Kommandoleiste.
    /// </summary>
    [ObservableProperty]
    private string? _availableUpdateVersion;

    /// <summary>
    /// Steuert das Kästchen neben dem START-Knopf. Die Wahl wird sofort gespeichert, damit
    /// sie den nächsten Start überdauert.
    /// </summary>
    [ObservableProperty]
    private bool _connectToServer = true;

    partial void OnConnectToServerChanged(bool value)
    {
        // Beim ersten Setzen aus den geladenen Einstellungen heraus gibt es nichts zu
        // sichern - _settings traegt den Wert dann bereits.
        if (_settings.ConnectToServer == value) return;

        _settings.ConnectToServer = value;
        _settingsService.Save(_settings);

        LogOnly(value
            ? "Arma 3 verbindet nach dem Start mit dem Einheitsserver."
            : "Arma 3 startet ohne Verbindung zum Einheitsserver.");
    }

    public AppSettings Settings => _settings;

    [RelayCommand]
    private void DetectPaths()
    {
        _settings.SteamPath ??= _pathDetection.FindSteamPath();

        if (_settings.SteamPath is not null)
        {
            _settings.Arma3Path ??= _pathDetection.FindArma3Path(_settings.SteamPath);
            if (_settings.Arma3Path is not null)
            {
                _workshopContentPath = _pathDetection.FindWorkshopContentPath(_settings.SteamPath, _settings.Arma3Path);
            }
        }

        _settings.TeamspeakPath ??= _pathDetection.FindTeamspeakClient();

        Arma3Path = _settings.Arma3Path;

        // Der gefundene Pfad ist Nebensache und gehoert in den Verlauf. Ein fehlender
        // dagegen verhindert jeden Start - der muss in der Statuszeile stehen, auch wenn
        // dort sonst die Modmeldung sitzt.
        if (Arma3Path is null)
        {
            Log("Arma 3 wurde nicht automatisch gefunden - bitte den Pfad in den Einstellungen setzen.");
        }
        else
        {
            LogOnly($"Arma 3 gefunden: {Arma3Path}");
        }

        // TeamSpeak ist kein Grund zur Sorge, wenn es fehlt - der Start funktioniert
        // auch ohne, nur der TS-Knopf faellt dann auf den ts3server:-Link zurueck.
        if (_settings.TeamspeakPath is not null) LogOnly($"TeamSpeak gefunden: {_settings.TeamspeakPath}");

        _settingsService.Save(_settings);
    }

    /// <summary>
    /// Holt die Pflichtmodliste aus dem Einheits-Repo und gleicht sie gegen die Platte ab.
    /// </summary>
    [RelayCommand]
    private async Task CheckModsAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.ModlistUrl))
        {
            Log("Keine Modlisten-URL konfiguriert (config/default-config.json).");
            return;
        }

        EnterBusy();
        StatusText = "Prüfe Modliste…";
        try
        {
            var modlist = await _modlistService.FetchAsync(_settings.ModlistUrl, _shutdownCts.Token);
            Mods.Clear();

            ModlistLabel = string.IsNullOrWhiteSpace(modlist.PresetName)
                ? null
                : $"{modlist.PresetName}" + (string.IsNullOrWhiteSpace(modlist.Updated) ? "" : $" · {modlist.Updated}");

            foreach (var mod in modlist.Mods)
            {
                var installed = _workshopContentPath is not null && ModManager.IsInstalled(mod, _workshopContentPath);
                Mods.Add(new ModStatusItem(mod) { Status = installed ? ModStatus.Installed : ModStatus.Missing });
            }

            var missingCount = Mods.Count(m => m.Status == ModStatus.Missing);
            StatusText = missingCount == 0
                ? $"Alle {Mods.Count} Mods sind installiert."
                : $"{missingCount} von {Mods.Count} Mods fehlen.";
            Log(StatusText);
        }
        catch (OperationCanceledException)
        {
            // Launcher wird gerade geschlossen.
        }
        catch (Exception ex)
        {
            // Die Modliste ist der einzige Weg zu erfahren, was gebraucht wird - ohne sie
            // faende ein Start ohne Mods statt, und der endet auf dem Server im Rauswurf.
            // Deswegen hier deutlich melden statt still weiterzulaufen.
            StatusText = "Modliste konnte nicht geladen werden.";
            Log($"Fehler beim Laden der Modliste: {ex.Message}");
        }
        finally
        {
            ExitBusy();
        }
    }

    [RelayCommand]
    private async Task CheckAndSubscribeModsAsync()
    {
        await CheckModsAsync();
        await SubscribeAndUpdateModsAsync();
    }

    /// <summary>
    /// Abonniert, was fehlt, und stellt fuer den Rest sicher, dass es wirklich die aktuelle
    /// Workshop-Version ist - ein vorhandener Ordner heisst nur, dass der Mod irgendwann
    /// einmal installiert wurde, nicht dass Steam ihn seither nachgezogen hat. Diese
    /// Pruefung laeuft bei jedem Start, nicht nur wenn etwas fehlt.
    ///
    /// Zusaetzlich zum (mitunter veralteten) lokalen NeedsUpdate-Flag von Steam fragt sie
    /// Steams Backend gebuendelt nach dem echten Aenderungszeitstempel jedes Mods und
    /// vergleicht ihn mit dem, was beim letzten bestaetigten Abgleich galt
    /// (ModVersionCache) - so faellt auch ein Workshop-Update auf, das der Steam-Client
    /// von sich aus noch nicht mitbekommen hat.
    /// </summary>
    [RelayCommand]
    private async Task SubscribeAndUpdateModsAsync()
    {
        if (_workshopContentPath is null)
        {
            Log("Arma-3-Pfad unbekannt - der Workshop-Ordner lässt sich nicht bestimmen.");
            return;
        }

        if (Mods.Count == 0) return;

        EnterBusy();
        try
        {
            if (!_steamWorkshop.IsInitialized && !_steamWorkshop.Initialize())
            {
                Log($"Steam nicht erreichbar ({_steamWorkshop.LastError ?? "unbekannt"}) - Mods können nicht " +
                    "abonniert oder aktualisiert werden. Bitte Steam starten oder die Mods manuell im " +
                    "Workshop abonnieren.");
                return;
            }

            var serverTimestamps = await _steamWorkshop.GetServerUpdateTimestampsAsync(
                Mods.Select(m => m.WorkshopId).ToList(), TimeSpan.FromSeconds(10), _shutdownCts.Token);

            var progress = new Progress<string>(Log);
            var updatedCount = 0;
            var failedCount = 0;

            foreach (var item in Mods)
            {
                item.Status = item.Status == ModStatus.Missing ? ModStatus.Subscribing : ModStatus.Updating;

                // Ein aufgeloester Serverzeitstempel, der nicht zum zuletzt bestaetigten
                // passt, ist ein positiver Beleg dafuer, dass sich das Item geaendert hat -
                // auch wenn Steams eigenes NeedsUpdate-Flag das anders sieht. Dann lieber
                // einen echten Neu-Download erzwingen als dem Flag zu glauben.
                //
                // Ein Mod ohne Eintrag im Cache (erste Pruefung ueberhaupt, oder ein neu
                // aufgenommener Mod) hat nichts zum Vergleichen. Der laeuft ueber den
                // normalen Weg und bekommt diesmal nur seinen Ausgangswert eingetragen -
                // "unbekannt" als "veraltet" zu behandeln wuerde beim ersten Start jeden
                // Spieler die komplette Modliste neu herunterladen lassen.
                var hasServerTime = serverTimestamps.TryGetValue(item.WorkshopId, out var serverTime);
                var cachedTime = _modVersionCache.Get(item.WorkshopId);
                var knownStale = hasServerTime && cachedTime is not null && cachedTime != serverTime;

                var installPath = ModManager.GetModPath(item.Mod, _workshopContentPath);

                SubscribeOutcome outcome;
                if (knownStale && item.Status != ModStatus.Subscribing)
                {
                    ModManager.DeleteInstalledMod(item.Mod, _workshopContentPath);
                    outcome = await _steamWorkshop.ForceRedownloadAsync(
                        item.WorkshopId, installPath, TimeSpan.FromMinutes(10), progress, _shutdownCts.Token);
                }
                else
                {
                    outcome = await _steamWorkshop.SubscribeAndInstallAsync(
                        item.WorkshopId, installPath, TimeSpan.FromMinutes(10), progress, _shutdownCts.Token);
                }

                item.Status = outcome is SubscribeOutcome.Success or SubscribeOutcome.AlreadyCurrent
                    ? ModStatus.Installed
                    : ModStatus.Failed;

                if (outcome is SubscribeOutcome.Success or SubscribeOutcome.AlreadyCurrent && hasServerTime)
                {
                    _modVersionCache.Set(item.WorkshopId, serverTime);
                }

                if (outcome == SubscribeOutcome.Success)
                {
                    updatedCount++;
                    Log($"{item.Name}: aktualisiert/abonniert.");
                }
                else if (outcome != SubscribeOutcome.AlreadyCurrent)
                {
                    failedCount++;
                    Log($"{item.Name}: {DescribeOutcome(outcome)}");
                }
            }

            StatusText = failedCount == 0
                ? (updatedCount == 0 ? "Alle Mods sind aktuell." : $"{updatedCount} Mod(s) aktualisiert - alle aktuell.")
                : $"{failedCount} Mod(s) konnten nicht aktualisiert werden.";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            // Sitzung sofort wieder schliessen statt sie fuer die restliche Laufzeit offen
            // zu halten - siehe SteamWorkshopService.Shutdown zum Overlay.
            _steamWorkshop.Shutdown();
            ExitBusy();
        }
    }

    private static string DescribeOutcome(SubscribeOutcome outcome) => outcome switch
    {
        SubscribeOutcome.TimedOut => "Zeitüberschreitung beim Herunterladen.",
        SubscribeOutcome.SteamNotRunning => "Steam läuft nicht.",
        _ => outcome.ToString()
    };

    /// <summary>
    /// Loescht einen einzelnen Mod und laedt ihn komplett neu - fuer den Fall, dass ein Mod
    /// sich im Spiel verhaelt wie lokal beschaedigt. Die normale "schon aktuell"-Pruefung
    /// findet so etwas nicht, weil sie nur Steams Buchfuehrung ansieht und nicht, ob die
    /// Dateien auf der Platte heil sind.
    /// </summary>
    [RelayCommand]
    private async Task RedownloadModAsync(ModStatusItem? item)
    {
        if (item is null) return;

        if (IsBusy)
        {
            Log("Es läuft bereits ein anderer Vorgang - bitte warten.");
            return;
        }

        if (_workshopContentPath is null)
        {
            Log("Arma-3-Pfad unbekannt - der Workshop-Ordner lässt sich nicht bestimmen.");
            return;
        }

        var confirmed = MessageWindow.Confirm(
            "MOD NEU HERUNTERLADEN",
            $"„{item.Name}“ wird gelöscht und komplett neu heruntergeladen. Fortfahren?",
            confirmText: "Neu laden",
            cancelText: "Abbrechen");

        if (!confirmed) return;

        EnterBusy();
        item.Status = ModStatus.Redownloading;

        try
        {
            if (!_steamWorkshop.IsInitialized && !_steamWorkshop.Initialize())
            {
                Log($"Steam nicht erreichbar ({_steamWorkshop.LastError ?? "unbekannt"}) - Mod kann nicht neu geladen werden.");
                item.Status = ModStatus.Failed;
                return;
            }

            try
            {
                ModManager.DeleteInstalledMod(item.Mod, _workshopContentPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log($"{item.Name}: lokaler Ordner konnte nicht gelöscht werden ({ex.Message}).");
                item.Status = ModStatus.Failed;
                return;
            }

            var progress = new Progress<string>(Log);
            var installPath = ModManager.GetModPath(item.Mod, _workshopContentPath);
            var outcome = await _steamWorkshop.ForceRedownloadAsync(
                item.WorkshopId, installPath, TimeSpan.FromMinutes(10), progress, _shutdownCts.Token);

            item.Status = outcome == SubscribeOutcome.Success ? ModStatus.Installed : ModStatus.Failed;
            Log(outcome == SubscribeOutcome.Success
                ? $"{item.Name}: neu heruntergeladen."
                : $"{item.Name}: Neuladen fehlgeschlagen ({DescribeOutcome(outcome)}).");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _steamWorkshop.Shutdown();
            ExitBusy();
        }
    }

    /// <summary>
    /// Verbindet den TeamSpeak-3-Client mit dem Einheitsserver - ein Klick statt Adresse
    /// heraussuchen und eintippen.
    ///
    /// Bevorzugt wird der bekannte Programmpfad, weil der ts3server:-Link allein nicht
    /// verlaesslich ist: bei einer portablen Installation registriert TeamSpeak das
    /// Schema gar nicht, und wenn ein anderes Programm es zuletzt beansprucht hat, landet
    /// der Klick dort statt bei TeamSpeak. Der Link bleibt der Rueckfall fuer den Fall,
    /// dass sich kein Pfad ermitteln liess.
    /// </summary>
    [RelayCommand]
    private void ConnectTeamspeak()
    {
        if (string.IsNullOrWhiteSpace(TeamspeakHost)) return;

        var url = $"ts3server://{TeamspeakHost}";
        if (!string.IsNullOrWhiteSpace(_serverData?.Teamspeak.Password))
        {
            url += $"?password={Uri.EscapeDataString(_serverData.Teamspeak.Password)}";
        }

        var client = _settings.TeamspeakPath;
        if (string.IsNullOrWhiteSpace(client) || !File.Exists(client))
        {
            LogOnly("TeamSpeak-Pfad unbekannt - versuche es über die Windows-Verknüpfung.");
            OpenExternal(url);
            return;
        }

        try
        {
            // Laeuft bereits eine Instanz, reicht der neu gestartete Prozess die Adresse
            // an sie weiter und beendet sich selbst - es oeffnet sich also kein zweiter
            // Client, sondern der vorhandene wechselt auf den Server.
            Process.Start(new ProcessStartInfo(client, $"\"{url}\"")
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(client) ?? string.Empty
            });
            LogOnly($"Verbinde TeamSpeak mit {TeamspeakHost}…");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogOnly($"TeamSpeak konnte nicht gestartet werden ({ex.Message}) - versuche es über die Windows-Verknüpfung.");
            OpenExternal(url);
        }
    }

    private void OpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Kein Programm fuer dieses Schema registriert (kein Standardbrowser, oder
            // TeamSpeak ist nicht installiert) - im Verlauf vermerken statt still schlucken.
            LogOnly($"„{url}“ konnte nicht geöffnet werden: {ex.Message}");
        }
    }

    private async Task LoadServerDataAndBackgroundAsync()
    {
        // Erst das zwischengespeicherte Bild zeigen, damit das Fenster nicht leer
        // aufgeht, dann im Hintergrund gegen die aktuelle Fassung tauschen.
        BackgroundImageSource = BackgroundImageService.LoadCached();

        if (string.IsNullOrWhiteSpace(_settings.ServerDataUrl)) return;

        try
        {
            _serverData = await _serverDataService.FetchAsync(_settings.ServerDataUrl, _shutdownCts.Token);

            MissionTime = string.IsNullOrWhiteSpace(_serverData.MissionTime) ? null : _serverData.MissionTime;
            TeamspeakHost = string.IsNullOrWhiteSpace(_serverData.Teamspeak.Host) ? null : _serverData.Teamspeak.Host;

            if (!string.IsNullOrWhiteSpace(_serverData.LauncherBackgroundUrl))
            {
                var fresh = await _backgroundImageService.FetchAndCacheAsync(
                    _serverData.LauncherBackgroundUrl, _shutdownCts.Token);
                if (fresh is not null) BackgroundImageSource = fresh;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogOnly($"Serverdaten konnten nicht geladen werden: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task PrepareAndLaunchAsync()
    {
        EnterBusy();
        try
        {
            if (_serverData is null)
            {
                StatusText = "Lade Serverdaten…";
                await LoadServerDataAndBackgroundAsync();
            }

            await CheckModsAsync();
            await SubscribeAndUpdateModsAsync();

            LaunchArma3();
        }
        catch (Exception ex)
        {
            Log($"Fehler beim Start: {ex.Message}");
        }
        finally
        {
            ExitBusy();
        }
    }

    private void LaunchArma3()
    {
        if (string.IsNullOrWhiteSpace(_settings.Arma3Path))
        {
            Log("Arma-3-Pfad unbekannt - Start abgebrochen. Bitte in den Einstellungen setzen.");
            return;
        }

        var arma3Exe = GameLauncherService.ResolveArma3Exe(_settings.Arma3Path);
        if (arma3Exe is null)
        {
            Log($"In „{_settings.Arma3Path}“ liegt keine arma3_x64.exe - Start abgebrochen.");
            return;
        }

        var modPaths = _workshopContentPath is not null
            ? ModManager.GetInstalledModPaths(Mods.Select(m => m.Mod), _workshopContentPath)
            : Array.Empty<string>();

        // Fehlt hier etwas gegenueber der Modliste, startet Arma trotzdem - aber der
        // Server wirft wegen fehlender Addons raus. Das gehoert sichtbar ins Protokoll.
        if (modPaths.Count < Mods.Count)
        {
            Log($"Achtung: nur {modPaths.Count} von {Mods.Count} Mods liegen vor - der Server lehnt den " +
                "Beitritt mit fehlenden Addons ab.");
        }

        // Ohne Serverangabe startet Arma ins Hauptmenü - der Modsatz ist derselbe.
        var server = ConnectToServer ? _serverData?.Arma3 : null;

        // In der Statuszeile steht, was mit den Mods passiert; wohin gestartet wird,
        // gehoert in den Verlauf. Das Kaestchen daneben sagt es ohnehin schon.
        Log($"Starte Arma 3 mit {modPaths.Count} Mods…");
        LogOnly(server is not null
            ? $"Verbinde mit {server.Ip}:{server.Port}."
            : "Ohne Serververbindung - Arma 3 startet ins Hauptmenü.");

        var process = GameLauncherService.Launch(arma3Exe, modPaths, server);
        StatusText = "Arma 3 gestartet.";

        if (process is not null)
        {
            IsArma3Running = true;
            _ = MonitorArma3ExitAsync(process);
        }
    }

    private async Task MonitorArma3ExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Der Prozess-Handle kann in Randfaellen unbrauchbar sein (etwa weil der
            // Prozess bereits beendet ist) - dann gilt Arma schlicht als nicht mehr laufend.
        }
        finally
        {
            process.Dispose();
        }

        IsArma3Running = false;
        Log("Arma 3 wurde beendet.");
    }

    private async Task InitializeSteamAsync()
    {
        // Nur kurz rein und wieder raus, um Name und Profilbild fuer die Titelleiste zu
        // holen - keine Sitzung, die ueber die ganze Laufzeit offen bleibt.
        var initialized = await Task.Run(() => _steamWorkshop.Initialize());
        SteamUserName = initialized ? _steamWorkshop.GetPersonaName() : null;
        SteamAvatar = initialized ? _steamWorkshop.GetAvatarImage() : null;
        _steamWorkshop.Shutdown();
    }

    private async Task ServerStatusLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_serverData is not null && !string.IsNullOrWhiteSpace(_serverData.Arma3.Ip))
            {
                var status = await ServerStatusService.CheckArma3Async(
                    _serverData.Arma3.Ip, _serverData.Arma3.Port, TimeSpan.FromSeconds(3), ct);
                Arma3ServerOnline = status.IsOnline;

                // Antwortet der Server nicht oder liefert er keine Zahlen, bleibt es beim
                // Strichpaar - der Punkt daneben sagt ohnehin schon, dass etwas nicht
                // stimmt. Eine alte Zahl stehen zu lassen waere irrefuehrender.
                Arma3PlayerCount = status.Players is not null && status.MaxPlayers is not null
                    ? $"{status.Players}/{status.MaxPlayers}"
                    : UnknownCount;
            }

            // Ohne hinterlegten ServerQuery-Zugang bleibt es beim blossen Knopf - dann
            // gar nicht erst verbinden, statt jede Minute in einen Fehlschlag zu laufen.
            if (_serverData is not null && _serverData.Teamspeak.HasQueryAccess)
            {
                var ts = await TeamspeakStatusService.CheckAsync(
                    _serverData.Teamspeak, TimeSpan.FromSeconds(5), ct);
                TeamspeakOnline = ts.IsOnline;
                TeamspeakUserCount = ts.Clients is not null && ts.MaxClients is not null
                    ? $"{ts.Clients}/{ts.MaxClients}"
                    : null;
            }

            try
            {
                // Solange die Serverdaten noch nicht geladen sind, in kurzem Takt
                // nachsehen; danach reicht einmal pro Minute.
                await Task.Delay(TimeSpan.FromSeconds(_serverData is null ? 5 : 60), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Sieht in ruhigem Takt nach, ob ein neues Release vorliegt, und laedt es im
    /// Hintergrund herunter. Eingespielt wird es nicht von selbst - der Launcher steht
    /// womoeglich neben einer laufenden Arma-Sitzung, und ein ungefragter Neustart waere
    /// dort das Letzte, was jemand gebrauchen kann. Stattdessen erscheint der Hinweis in
    /// der Kommandoleiste, und der Spieler entscheidet, wann er neu startet.
    ///
    /// Beim Start selbst wird bereits geprueft und sofort eingespielt (siehe App); diese
    /// Schleife fuellt die Luecke fuer Launcher, die stundenlang offen stehen - typisch
    /// an einem Missionsabend, an dem noch schnell ein Release herausgeht.
    /// </summary>
    private async Task UpdateCheckLoopAsync(CancellationToken ct)
    {
        // Der Start hat gerade eben geprueft - ein sofortiger zweiter Durchlauf brächte
        // nichts als eine ueberfluessige Anfrage.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(30), ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            var version = await _updateService.CheckAndDownloadAsync(ct);
            if (version is not null && AvailableUpdateVersion != version)
            {
                AvailableUpdateVersion = version;
                LogOnly($"Version {version} steht bereit - Launcher neu starten, um sie zu übernehmen.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(30), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Spielt das bereitliegende Update ein und startet den Launcher neu. Laeuft gerade
    /// Arma, wird vorher gefragt - der Neustart des Launchers beendet zwar nicht das
    /// Spiel, aber die Modpruefung und die Serveranzeige sind waehrenddessen weg.
    /// </summary>
    [RelayCommand]
    private void RestartToUpdate()
    {
        if (AvailableUpdateVersion is null) return;

        if (IsArma3Running || IsBusy)
        {
            var confirmed = MessageWindow.Confirm(
                $"UPDATE AUF {AvailableUpdateVersion}",
                IsArma3Running
                    ? "Arma 3 läuft gerade. Der Launcher startet neu — das Spiel bleibt davon unberührt. Fortfahren?"
                    : "Es läuft gerade ein Vorgang. Der Launcher startet neu und bricht ihn ab. Fortfahren?",
                confirmText: "Neu starten",
                cancelText: "Abbrechen");

            if (!confirmed) return;
        }

        Log($"Update auf {AvailableUpdateVersion} wird eingespielt, Launcher startet neu…");

        // Der Updater wartet auf das Ende dieses Prozesses, bevor er die Dateien
        // austauscht - ohne das Beenden hier bliebe das Update liegen.
        if (_updateService.ApplyPendingAndRestart())
        {
            Application.Current.Shutdown();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        _settings = settings;
        _settingsService.Save(_settings);
        DetectPaths();
    }

    /// <summary>
    /// Schreibt in den Verlauf und in die Statuszeile der Kommandoleiste. Fuer alles, was
    /// den Modabgleich oder den Start betrifft - also das, was dort stehen soll.
    /// </summary>
    private void Log(string message) => Write(message, alsStatus: true);

    /// <summary>
    /// Schreibt nur in den Verlauf. Fuer Nebenlaeufiges wie erkannte Pfade, die
    /// TeamSpeak-Verbindung oder das Umschalten einer Einstellung: nachlesbar, aber ohne
    /// die Statuszeile zu belegen, in der die Mod-Meldung stehen soll.
    /// </summary>
    private void LogOnly(string message) => Write(message, alsStatus: false);

    private void Write(string message, bool alsStatus)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            LogText = LogText.Length == 0 ? line : LogText + Environment.NewLine + line;
            if (alsStatus) StatusText = message;
        });
    }

    public void Dispose()
    {
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
        _steamWorkshop.Dispose();
        _http.Dispose();
    }
}
