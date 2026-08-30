# Task Force 11 Launcher

Offizieller Launcher für **[Task Force 11](https://www.taskforce11.de/)** — Kommando
Spezialkräfte der Marine.

Der Launcher übernimmt:

- Prüft, ob alle Pflichtmods installiert sind, und abonniert bzw. lädt fehlende
  automatisch über den Steam Workshop nach
- Erkennt Mods, die auf dem Workshop aktualisiert wurden, auch wenn Steam selbst
  noch nichts davon mitbekommen hat
- Startet Arma 3 mit der kompletten Modliste und verbindet direkt mit dem Einheitsserver
- Verbindet auf Klick mit dem TeamSpeak-Server
- Aktualisiert sich selbst

## Download

Neueste Version: **[Releases](../../releases/latest)**

Dort `TaskForce11Launcher-win-Setup.exe` herunterladen und ausführen. Die übrigen
Dateien auf der Release-Seite gehören zum Auto-Updater und werden nicht gebraucht.

## Benutzung

1. Steam starten und einloggen
2. Launcher öffnen
3. Auf **START** klicken — fehlende Mods werden abonniert und geladen, danach startet
   Arma 3 und verbindet automatisch mit dem Server

Solange Arma 3 läuft, zeigt der Knopf „LÄUFT“. Oben links stehen Serverstatus und
Spielerzahl, oben rechts der eingeloggte Steam-Account.

Über **MODS** öffnet sich die Modliste mit dem Status jedes einzelnen Mods. Der
🔗-Knopf führt zur Workshop-Seite, der ↻-Knopf löscht einen Mod und lädt ihn komplett
neu — der Weg, wenn ein Mod sich verhält, als wären die Dateien beschädigt.

## Einstellungen

Über das Zahnrad oben rechts, falls Arma 3 nicht automatisch gefunden wurde. Dort lässt
sich der Installationsordner manuell setzen; „Pfade neu erkennen“ verwirft ihn wieder
und sucht erneut.

## Voraussetzungen

- Windows 10/11 (64-bit)
- [Steam](https://store.steampowered.com/) mit Arma 3, installiert und eingeloggt
- TeamSpeak 3 (für den TS-Knopf)

## Für die Einheitsleitung

### Modliste aktualisieren

Die Pflichtmods stehen in [`data/workshop.json`](data/workshop.json) — die lesbare
Fassung in [`data/modlist.md`](data/modlist.md). Der Launcher lädt die Liste bei jedem
Start neu, ein **Modlisten-Update braucht also kein neues Launcher-Release**:

1. Preset im Arma 3 Launcher exportieren
2. Datei nach `data/presets/` legen
3. Konvertieren:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/Convert-Preset.ps1 -PresetPath "data/presets/<preset>.html"
   ```

4. `data/workshop.json` und `data/modlist.md` committen und pushen

Beim nächsten Launcher-Start haben alle die neue Liste.

### Serverdaten ändern

[`data/serverdata.json`](data/serverdata.json) hält Serveradresse, Port, Passwort,
TeamSpeak-Adresse, Missionstermin und die URL des Launcher-Hintergrundbilds. Auch das
wird zur Laufzeit gelesen — ein Serverumzug ist ein Commit, kein Release.

> **Hinweis:** Sobald dieses Repository öffentlich ist, sind Serveradresse und
> Serverpasswort für jeden lesbar. Ist das nicht gewollt, sollte das Repo privat
> bleiben — dann muss allerdings auch `ModlistUrl`/`ServerDataUrl` in
> `src/TaskForce11Launcher/config/default-config.json` auf eine Quelle zeigen, die der
> Launcher ohne Anmeldung erreicht (z. B. eine Datei auf taskforce11.de).

### Repository-URLs

`src/TaskForce11Launcher/config/default-config.json` verweist auf
[`GeistesGeist/taskforce11_launcher`](https://github.com/GeistesGeist/taskforce11_launcher),
Branch `main`. Zieht das Projekt um oder heißt der Standard-Branch anders, sind dort
alle drei URLs anzupassen — sie steuern Modlisten-Feed, Serverdaten und Auto-Update.

### Release bauen

Ein Tag `v*` löst den Workflow in [`.github/workflows/build.yml`](.github/workflows/build.yml)
aus; er baut, paketiert mit Velopack und legt das GitHub-Release samt Update-Feed an:

```bash
git tag v0.1.0
git push origin v0.1.0
```

Ohne Tag baut jeder Push auf `main` eine Version `0.1.<Build-Nummer>`.

Details zum Aufbau des Projekts: [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md).

## Probleme?

An die Einheitsleitung von Task Force 11 wenden — oder im
[Discord](https://discord.gg/74cPDMcyaU) melden.
