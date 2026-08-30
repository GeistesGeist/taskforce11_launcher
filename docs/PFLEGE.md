# Pflege — für die Einheitsleitung

Was sich ohne neues Launcher-Release ändern lässt, und wie ein Release entsteht.

## Modliste aktualisieren

Die Pflichtmods stehen in [`data/workshop.json`](../data/workshop.json), die lesbare
Fassung in [`data/modlist.md`](../data/modlist.md). Der Launcher lädt die Liste bei
jedem Start neu — ein **Modlisten-Update braucht also kein neues Release**:

1. Preset im Arma 3 Launcher exportieren
2. Datei nach `data/presets/` legen
3. Konvertieren:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/Convert-Preset.ps1 -PresetPath "data/presets/<preset>.html"
   ```

4. `data/workshop.json` und `data/modlist.md` committen und pushen

Beim nächsten Launcher-Start haben alle die neue Liste. Die Reihenfolge des Presets
bleibt dabei erhalten — sie ist die Ladereihenfolge.

## Serverdaten ändern

[`data/serverdata.json`](../data/serverdata.json) hält alles, was sich ändern kann,
ohne dass jemand etwas neu installieren muss:

| Feld | Bedeutung |
|---|---|
| `arma3.ip` / `.port` / `.password` | Direct Connect beim Start |
| `teamspeak.host` / `.password` | Ziel des TeamSpeak-Knopfes |
| `teamspeak.voicePort` | Sprachport, Standard 9987 |
| `teamspeak.queryPort` / `.queryUser` / `.queryPassword` | ServerQuery für die Nutzerzahl — siehe unten |
| `missionTime` | Anzeigetext, z. B. „Sonntag 16:00". Leer blendet die Zeile aus |
| `launcherBackgroundUrl` | Hintergrundbild des Launchers. Leer lässt den Farbverlauf stehen |

Auch das wird zur Laufzeit gelesen: ein Serverumzug ist ein Commit, kein Release.

> **Achtung:** Das Repository ist öffentlich. Serveradresse und Serverpasswort sind
> damit für jeden lesbar. Falls das nicht gewollt ist, muss das Repo privat werden —
> dann müssen allerdings `ModlistUrl` und `ServerDataUrl` in
> [`src/TaskForce11Launcher/config/default-config.json`](../src/TaskForce11Launcher/config/default-config.json)
> auf eine Quelle zeigen, die der Launcher ohne Anmeldung erreicht (z. B. eine Datei
> auf taskforce11.de).

### TeamSpeak-Nutzerzahl aktivieren

Solange `queryUser` und `queryPassword` leer sind, zeigt der Launcher nur den
TeamSpeak-Knopf und keinen Status. Für die Nutzerzahl braucht es drei Dinge:

1. **Port 10011/TCP** muss von außen erreichbar sein
2. **Einen eigenen ServerQuery-Login**, dessen Servergruppe ausschließlich
   `b_virtualserver_info_view` und `b_serverquery_login` hat — **niemals `serveradmin`**.
   Die Zugangsdaten stehen in einem öffentlichen Repo; mit vollen Rechten könnte jeder
   Fremde den TeamSpeak-Server administrieren.
3. Beide Werte in `serverdata.json` eintragen

Jeder Launcher fragt einmal pro Minute mit vier Befehlen an — pro IP weit unter der
Flood-Grenze von TeamSpeak. Sollten trotzdem Sperren auftreten, hilft ein Eintrag in
`query_ip_whitelist.txt` auf dem Server.

## Repository-URLs

[`config/default-config.json`](../src/TaskForce11Launcher/config/default-config.json)
verweist auf `GeistesGeist/taskforce11_launcher`, Branch `main`. Zieht das Projekt um
oder heißt der Standard-Branch anders, sind dort alle drei URLs anzupassen — sie
steuern Modlisten-Feed, Serverdaten und Auto-Update.

Diese Datei ist die einzige Stelle, an der die URLs gepflegt werden: Sie überschreibt
beim Start immer die lokal gespeicherten Werte, damit ein Umzug per Launcher-Update
ankommt und niemand seine `settings.json` löschen muss.

## Release bauen

Ein Tag `v*` löst [`.github/workflows/build.yml`](../.github/workflows/build.yml) aus;
der Workflow baut, paketiert mit Velopack und legt das GitHub-Release samt Update-Feed
an:

```bash
git tag v1.0.0
git push origin v1.0.0
```

Ohne Tag baut jeder Push auf `main` eine Version `0.1.<Build-Nummer>`. Bereits
installierte Launcher ziehen das neue Release von selbst — beim nächsten Start sofort,
im laufenden Betrieb nach spätestens 30 Minuten als Hinweis zum Neustarten.

## Weiteres

Aufbau des Projekts und die Fallstricke im Code: [`DEVELOPMENT.md`](DEVELOPMENT.md).
