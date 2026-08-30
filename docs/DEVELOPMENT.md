# Entwicklung

## Aufbau

WPF-Anwendung auf .NET 8 (`net8.0-windows`, x64), MVVM über CommunityToolkit.Mvvm,
Selbstaktualisierung über Velopack.

```
src/TaskForce11Launcher/
├── App.xaml                 Farbwelt, Steuerelement-Stile
├── App.xaml.cs              Start: Velopack-Haken, Update-Prüfung, Fenster
├── MainWindow.xaml(.cs)     Hauptfenster samt Mods/Verlauf-Schublade
├── SettingsWindow.xaml(.cs) Pfad-Einstellungen
├── ViewModels/              MainViewModel — der gesamte Ablauf
├── Services/                Steam, Pfade, Feeds, Serverstatus, Update
├── Models/                  Datenklassen
├── Converters/              Wertkonverter für die Bindungen
├── Interop/                 Windows-11-Fensterecken
├── config/                  Mitgelieferte Standardkonfiguration
├── steam_api64.dll          Steamworks-Redistributable (SDK v1.65), siehe unten
└── steam_appid.txt          107410 — sagt der Steam-API, um welches Spiel es geht

data/                        Wird zur Laufzeit aus dem Repo gelesen
├── workshop.json            Pflichtmods (aus dem Preset erzeugt)
├── modlist.md               Dieselbe Liste, lesbar
├── serverdata.json          Server, TeamSpeak, Missionszeit, Hintergrundbild
└── presets/                 Die exportierten Arma-3-Presets

tools/Convert-Preset.ps1     Preset → workshop.json + modlist.md
```

## Bauen

Voraussetzung: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet restore TaskForce11Launcher.sln
dotnet build TaskForce11Launcher.sln -c Release
dotnet run --project src/TaskForce11Launcher/TaskForce11Launcher.csproj
```

Im Entwicklungsbetrieb überspringt der Updater sich selbst (`manager.IsInstalled` ist
`false`) — die Selbstaktualisierung lässt sich nur an einer per Setup.exe installierten
Fassung testen.

## Wie ein Start abläuft

`PrepareAndLaunchAsync` im `MainViewModel` ist der ganze Ablauf:

1. **Serverdaten laden**, falls noch nicht geschehen (`ServerDataService`)
2. **Modliste holen** und gegen die Platte abgleichen (`ModlistService`, `ModManager`)
3. **Abonnieren und aktualisieren** (`SteamWorkshopService`)
4. **Arma 3 starten** mit einem `-mod=`-Argument pro Mod plus `-connect`/`-port`/`-password`

## Drei Dinge, die nicht offensichtlich sind

Sie sind der Grund für einen Großteil der Umwege im Code — wer hier vereinfacht, baut
sie wieder ein.

**Steams „installiert“ ist keine Aussage über die Festplatte.** `GetItemState` meldet
`Installed` aus Steams eigener Buchführung. Beobachtet wurde mehrfach, dass dabei nur
die leere Ordnerstruktur (`addons/`, `keys/`) angelegt war und keine einzige Datei darin
lag — Arma findet dann nichts zu laden. Deshalb prüft `ModManager.HasContent` rekursiv
auf eine echte Datei, und jede Erfolgsmeldung im `SteamWorkshopService` hängt zusätzlich
an dieser Prüfung.

**Steams `NeedsUpdate` hinkt hinterher.** Das Flag weiß nur, was der Client zufällig
schon mitbekommen hat. Ein Mod, der vor zehn Minuten im Workshop aktualisiert wurde,
gilt lokal weiter als aktuell — und der Spieler fliegt beim Beitritt raus.
`GetServerUpdateTimestampsAsync` fragt deshalb Steams Backend gebündelt nach dem echten
Änderungszeitpunkt und vergleicht ihn mit dem, was beim letzten bestätigten Abgleich
galt (`ModVersionCache`). Weicht er ab, wird der Mod gelöscht und neu geladen. Ein Mod
ohne Eintrag im Cache gilt als „unbekannt“, nicht als „veraltet“ — sonst lädt beim
ersten Start jeder die komplette Liste neu.

**Das Steam-Overlay hängt sich an jeden Prozess mit laufender Steam-Sitzung.** Ein
Opt-out gibt es nicht. Zwei Maßnahmen dagegen: `App` schaltet WPF auf Software-Rendering
(ohne Direct3D-Swapchain gibt es nichts, woran sich das Overlay hängen könnte), und der
`SteamWorkshopService` hält eine Sitzung nur so lange offen, wie er sie braucht — jeder
Vorgang endet mit `Shutdown()`.

## Weitere Fallstricke

- **`Shutdown()` muss auf die Callback-Pumpe warten.** `Timer.Dispose()` ohne Argument
  wartet nicht auf einen laufenden Callback; `SteamAPI_Shutdown()` währenddessen endet
  in einer Zugriffsverletzung in Steams eigener DLL. Deshalb `Dispose(WaitHandle)`.
- **Die Update-Prüfung läuft vor allem anderen** — insbesondere bevor
  `steam_api64.dll` in den Prozess geladen wird. `Update.exe` muss beim Einspielen kurz
  jede Datei unter `current/` allein besitzen; hält der eigene Prozess (oder ein
  Virenscanner, der darauf reagiert) noch ein Handle, scheitert es an einer
  Sharing-Verletzung. Der `UpdateService` merkt sich einen gescheiterten Versuch und
  überspringt dieselbe Version drei Minuten lang, damit daraus keine Neustart-Schleife
  wird.
- **`SteamUGCDetails_t` muss byteweise stimmen.** Nur drei Felder werden gelesen, aber
  ein falsches Layout lässt Steam über das Ende des Puffers hinausschreiben. Windows
  packt mit 8 Byte (`Pack = 8`).
- **Die Feed-URLs kommen immer aus `config/default-config.json`,** nie aus der
  gespeicherten `settings.json` — sonst hinge nach einem Repo-Umzug jeder auf der alten
  URL fest, bis er seine lokale Datei löscht.
- **`A2S_INFO` braucht heute eine Challenge.** Die erste Antwort ist oft nur `'A'` plus
  4-Byte-Challenge; erst die erneut gesendete Anfrage mit angehängter Challenge liefert
  die Spielerzahlen. Schlägt dieser zweite Umlauf fehl, gilt der Server trotzdem als
  online — die erste Antwort war bereits der Beweis.

## Konfiguration

`config/default-config.json` wird mit ausgeliefert und gilt beim ersten Start:

| Schlüssel | Bedeutung |
|---|---|
| `ModlistUrl` | Rohdaten-URL von `data/workshop.json` |
| `ServerDataUrl` | Rohdaten-URL von `data/serverdata.json` |
| `GithubRepoUrl` | Quelle für den Auto-Updater |

Nutzerseitig kommt nur der Arma-3-Pfad dazu; gespeichert wird das in
`%AppData%\TaskForce11Launcher\settings.json`. Daneben liegen `mod-versions.json`
(Zeitstempel-Cache) und `cache/background.img`.

## Release

Tag `v<version>` pushen — `.github/workflows/build.yml` baut self-contained als
Single-File (keine .NET-Installation auf den Spieler-PCs nötig), paketiert mit Velopack
und legt das Release samt Update-Feed an. Der Auto-Updater der bereits installierten
Launcher zieht es daraufhin von selbst.

## Herkunft von `steam_api64.dll`

Die Datei ist die unveränderte Redistributable aus dem Steamworks SDK (v1.65) und
liegt im Repo, weil der Build sie neben der EXE braucht. Sie stammt nicht aus diesem
Projekt und wird hier auch nicht gepflegt — wer sie ersetzen oder aktualisieren will,
lädt das [Steamworks SDK](https://partner.steamgames.com/downloads/list) herunter und
nimmt `redistributable_bin/win64/steam_api64.dll` daraus.

Wird sie gegen eine neuere Fassung getauscht, sind die versionierten Einstiegspunkte im
`SteamWorkshopService` zu prüfen — `SteamAPI_SteamUGC_v021`, `SteamAPI_SteamFriends_v018`,
`SteamAPI_SteamUser_v023` und `SteamAPI_SteamUtils_v011`. Ändert Valve eine
Schnittstellenversion, verschwindet der alte Name aus der Export-Tabelle und der Aufruf
scheitert zur Laufzeit mit einer `EntryPointNotFoundException`, nicht beim Kompilieren.

## Schrift

Überschriften laufen in **Bebas Neue**, derselben Schrift, die die Einheitsverwaltung
verwendet (nachweisbar in deren Stylesheet). Sie liegt unter `Assets/Fonts` und wird in
die Anwendung eingebettet — auf keinem Windows ist sie vorinstalliert, und ohne
Einbettung fiele WPF stillschweigend auf eine Ersatzschrift zurück.

Angesprochen wird sie über die Ressource `HeadingFont` in `App.xaml`. Die Schreibweise
`pack://application:,,,/Assets/Fonts/#Bebas Neue` ist Pflicht: hinter dem Doppelkreuz
steht der Familienname aus der Schriftdatei, nicht der Dateiname.

Zwei Dinge sind beim Setzen zu beachten:

- **Nur Versalien.** Bebas Neue hat keine Kleinbuchstaben; Fließtext bleibt deshalb bei
  Segoe UI. Betroffen sind ausschließlich Überschriften, Reiter und Knopfbeschriftungen.
- **Kein Fettschnitt.** Es gibt nur „Regular". Ein `FontWeight="Bold"` ließe WPF eine
  künstliche Fettung berechnen, die unsauber aussieht — deshalb steht bei diesen
  Textblöcken gar kein FontWeight.

Weil die Schrift schmaler läuft als Segoe UI, sind die Schriftgrade der Überschriften
gegenüber vorher angehoben.
