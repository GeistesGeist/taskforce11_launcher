# Anmeldung im Launcher — was dafür gebraucht wird

Entwurf für den `dev`-Branch. Dieses Dokument beschreibt, was auf Seiten der
Einheitsverwaltung (Panel) entstehen muss, damit sich Mitglieder im Launcher anmelden
können und Anwärter nur eingeschränkten Zugriff auf die Modliste bekommen.

## Warum der Launcher nicht direkt an die Datenbank darf

Der Launcher liegt auf jedem Spieler-PC. Alles, was in ihm steckt, lässt sich auslesen —
auch Verschlüsseltes, denn der Schlüssel läge daneben. Datenbank-Zugangsdaten im Launcher
hieße: Jedes Mitglied kann die gesamte Einheitsverwaltung lesen und ändern, und wer den
Launcher weitergibt, gibt den Datenbankzugang mit weiter.

Der Launcher spricht deshalb mit dem Panel, so wie es der Browser beim Anmelden tut. Nur
das Panel spricht mit der Datenbank.

```
Launcher  ──HTTPS──>  Panel (prüft gegen die Datenbank)  ──>  Datenbank
```

## Was im Panel entstehen muss

Zwei Endpunkte. Beide geben JSON zurück.

### 1. Anmelden

```
POST /api/launcher/login
Content-Type: application/json

{ "username": "jan.geist", "password": "…" }
```

Antwort bei Erfolg (HTTP 200):

```json
{
  "token": "<zufälliges Sitzungstoken>",
  "expiresAt": "2026-09-06T18:00:00Z",
  "displayName": "Jan Geist",
  "rank": "Hauptbootsmann",
  "role": "mitglied"
}
```

Antwort bei falschen Daten: **HTTP 401** mit `{ "error": "…" }`. Bei gesperrtem oder
ausgeschiedenem Konto ebenfalls 401 — der Launcher unterscheidet das nicht, die Meldung
für den Spieler ist dieselbe.

Für `role` genügen vorerst zwei Werte:

| Wert | Bedeutung |
|---|---|
| `anwaerter` | eingeschränkte Modliste |
| `mitglied` | vollständige Modliste |

### 2. Sitzung prüfen

```
GET /api/launcher/me
Authorization: Bearer <token>
```

Antwort: dasselbe Objekt wie oben, ohne `token`. Bei abgelaufenem oder unbekanntem
Token **HTTP 401**.

Damit muss niemand sein Passwort bei jedem Start neu eingeben: Der Launcher legt das
Token lokal ab und prüft es beim nächsten Mal gegen diesen Endpunkt.

### Anforderungen

- **Nur über HTTPS.** Ohne das gehen Zugangsdaten im Klartext durchs Netz.
- **Passwörter nur gehasht** in der Datenbank (bcrypt oder argon2), niemals im Klartext.
  Das Panel macht das mit ziemlicher Sicherheit schon.
- **Bremse gegen Rateversuche**, etwa fünf Fehlversuche pro Konto und Minute. Sonst lässt
  sich über diesen Endpunkt bequem durchprobieren.
- **Token als Zufallswert** (mindestens 32 Byte), begrenzte Gültigkeit, serverseitig
  widerrufbar — damit ein ausgeschiedenes Mitglied sofort ausgesperrt werden kann.

Das ist überschaubar: In einer Meteor-Anwendung sind das rund 40 Zeilen, weil das
Konto-System samt Passwortprüfung bereits existiert.

## Was der Launcher daraus macht

1. Beim Start Anmeldefenster, falls kein gültiges Token vorliegt
2. Token lokal ablegen (`%AppData%\TaskForce11Launcher`), beim nächsten Start prüfen
3. **Ohne Anmeldung**: kein Modabgleich, kein Spielstart — die Knöpfe bleiben gesperrt
4. Modliste nach `role` filtern

## Modliste nach Rolle

`data/workshop.json` bekommt pro Mod ein Feld, das die niedrigste Rolle nennt, die ihn
bekommt. Fehlt es, gilt der Mod für alle:

```json
{ "id": "450814997", "name": "CBA_A3" },
{ "id": "3693562497", "name": "Task Force 11 - Vehicles", "minRole": "mitglied" }
```

Anwärter erhalten dann alles ohne `minRole`, Mitglieder alles. Welche der 50 Mods für
Anwärter wegfallen sollen, entscheidet die Einheitsleitung — technisch ist es eine Zeile
je Mod.

## Wogegen das schützt und wogegen nicht

**Es schützt davor**, dass Außenstehende den Launcher benutzen und dass Anwärter versehentlich
mit dem vollen Modsatz antreten.

**Es schützt nicht** davor, dass jemand mit technischem Wissen den Launcher umgeht: Wer die
Mods kennt, kann sie im Steam Workshop von Hand abonnieren und Arma direkt starten — die
Mods sind öffentlich. Der Launcher ist ein Komfort- und Ordnungswerkzeug, keine Sperre.

Durchsetzen lässt sich das nur auf dem Arma-Server: Er sieht beim Beitritt, welche Mods
geladen sind, und kann Spieler mit dem falschen Satz ablehnen. Wenn die Trennung zwischen
Anwärtern und Mitgliedern wirklich verbindlich sein soll, gehört diese Prüfung dorthin —
der Launcher sorgt dafür, dass niemand aus Versehen falsch antritt.

## Stand

Der Launcher-Teil ist noch nicht gebaut. Sobald die beiden Endpunkte stehen und ihre
Adresse feststeht, ist die Launcher-Seite überschaubar: Anmeldefenster, Token-Ablage,
Sperren der Knöpfe ohne Anmeldung, Filtern der Modliste.
