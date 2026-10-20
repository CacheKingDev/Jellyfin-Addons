# Movie Code

<img src="assets/movie-code.png" alt="Movie Code" width="180">

Ein Jellyfin-Plugin, mit dem ein Administrator für einen Benutzer ein Paket aus **10 Einmalcodes** erzeugt. Der Benutzer kann die Mediathek normal durchsuchen und Filme ansehen. Sobald er einen Film startet, fragt das Plugin einen **6-stelligen Einmalcode** in einem Popup im Web-Client ab. Jeder Code gilt genau einmal und gibt genau den Film frei, der danach gestartet wird. Es ist immer nur **ein Film gleichzeitig** freigegeben.

Zielplattform: **Jellyfin 12.1** mit **.NET 10**.

## Funktionsweise

1. Der Administrator öffnet **Benutzer → [Benutzer] → Kindersicherung** und erzeugt dort **10 Einmalcodes**. Das Plugin zeigt die Liste **einmalig** an. Der Administrator kopiert oder speichert sie und gibt dem Benutzer bei Bedarf einen Code weiter.
2. Der Benutzer durchsucht die Mediathek wie gewohnt. Beim Drücken auf **Abspielen** erscheint das Code-Popup.
3. Nach Eingabe eines noch offenen Codes wird die Freigabe an den tatsächlich gestarteten Film gebunden.
4. Der Film startet. Jede andere Video-Wiedergabe wird beendet, solange dieser Film aktiv ist.
5. Wird der Film vollständig angesehen (oder zu mindestens 95 %), gilt dieser Code als verbraucht. Die übrigen Codes aus dem Paket bleiben gültig; der Benutzer kann mit dem nächsten Code einen weiteren Film freischalten. Sind keine Codes mehr übrig, bleibt der Benutzer gesperrt, bis der Administrator ein neues Paket erzeugt.
6. Ein neues Code-Paket für denselben Benutzer ersetzt alle bisherigen Codes. Noch nicht verwendete alte Codes werden dadurch ungültig.

Der Film wird erst beim Einlösen des Codes bzw. beim ersten Wiedergabestart festgelegt; der Administrator muss ihn nicht auswählen.

### Popup und native Apps

Das Popup wird per Script-Injection in den Jellyfin-Web-Client eingebunden. Es funktioniert in:

- Browsern (Desktop und mobil)
- Jellyfin Media Player
- installierbaren PWAs

Native Apps (Android TV, Roku, iOS/Android-App) können kein Popup anzeigen. Dort erscheint weiterhin die serverseitige Sperrmeldung mit dem Link auf die Freigabeseite. Über diese Seite kann der Code eingegeben werden; der erste danach gestartete Film wird gebunden.

> Die Script-Injection für Popup und Admin-Bereich ist kein offizieller Jellyfin-Plugin-Hook. Sie kann durch Änderungen am Web-Client brechen. Die serverseitige Sperre bleibt davon unabhängig bestehen.

## Installation

1. `build.ps1` ausführen oder die Release-DLL aus `Jellyfin.Plugin.MovieCode/bin/Release/net10.0/` verwenden.
2. Im Jellyfin-Datenverzeichnis den Ordner `plugins/MovieCode` anlegen.
3. `Jellyfin.Plugin.MovieCode.dll` hineinkopieren.
4. Jellyfin neu starten.
5. Unter **Dashboard → Plugins** erscheint „Movie Code“. Die globalen Einstellungen liegen unter **Dashboard → Plugins → Movie Code**; die Einmalcodes werden pro Benutzer unter **Benutzer → [Benutzer] → Kindersicherung** verwaltet.

Das Skript `build.ps1` erzeugt zusätzlich ein installierbares ZIP unter `dist/`.

## Konfiguration

| Einstellung | Standard | Bedeutung |
| --- | --- | --- |
| `UnlockPageUrl` | leer | Öffentliche Adresse der Freigabeseite. Leer = automatisch aus der Anfrage ermitteln. |
| `CodeValidityHours` | `24` | Gültigkeit der Einmalcodes in Stunden. `0` = unbegrenzt, bis die Codes verwendet oder aufgehoben werden. |
| `MaxFailedAttemptsPerTenMinutes` | `5` | Maximale Fehlversuche pro IP in 10 Minuten. `0` = unbegrenzt. |
| `ExpireOnEarlyStop` | `false` | Freigabe auch dann verbrauchen, wenn der Film vorzeitig gestoppt wird. |
| `ReleaseAfterCompletion` | `false` | Benutzer nach dem Filmende automatisch entsperren. Dabei werden alle übrigen Codes des Pakets ungültig. |
| `RestrictOnlyVideo` | `true` | Nur Videos sperren. Musik und Hörbücher bleiben erlaubt. |
| `EnableWebPopup` | `true` | Code-Popup im Jellyfin-Web-Client anzeigen. Die Code-Verwaltung in der Kindersicherung ist davon nicht betroffen. |
| `LockedMessageHeader` | `Film gesperrt` | Überschrift der Meldung, die bei einer blockierten Wiedergabe erscheint. |
| `LockedMessageText` | siehe unten | Text der Meldung. |

Standardtext der Sperrmeldung:

> Bitte gib zuerst einen deiner 6-stelligen Einmalcodes auf der Freigabeseite ein.

## Benutzung

### Als Administrator

1. **Benutzer** öffnen und beim gewünschten Benutzer das Drei-Punkte-Menü anklicken.
2. **Kindersicherung** öffnen. Dort erscheint der Bereich **Movie Code – Einmalcodes**.
3. **10 Einmalcodes erzeugen** – die Liste wird nur jetzt angezeigt. Ein neues Paket ersetzt alle bisherigen Codes dieses Benutzers.
4. Liste über **Alle Codes kopieren** oder **Liste als TXT herunterladen** sichern und sicher verwahren.
5. Dem Benutzer jeweils einen Code weitergeben (z. B. persönlich oder über einen sicheren Kanal). Nach einem Film kann der nächste Code aus der Liste folgen.
6. Unter **Codes anzeigen** siehst du pro Code, welche Nummern offen, aktiv, verbraucht oder abgelaufen sind. **Paket aufheben** macht alle noch offenen Codes ungültig und hebt die Sperre des Benutzers auf.
7. Die globalen Optionen (Gültigkeit, Fehlversuche, Popup, Sperrmeldung) bleiben unter **Dashboard → Plugins → Movie Code**. Von dort führt die Schaltfläche **Benutzerverwaltung öffnen** direkt zur Benutzerliste.

Ein neues Paket für denselben Benutzer ersetzt alle bisherigen Codes. Die alte Liste wird dadurch ungültig, auch wenn sie noch nicht vollständig verbraucht war.

### Als Benutzer

1. In Jellyfin einen Film auswählen und **Abspielen** drücken.
2. Im Popup einen noch nicht verwendeten 6-stelligen Einmalcode eingeben und **Starten** drücken.
3. Der Film startet. Andere Filme bleiben gesperrt, solange dieser Film aktiv ist.
4. Nach dem Filmende kann der Benutzer mit dem nächsten Code aus dem Paket einen weiteren Film freischalten.

**Fallback für native Apps:** `http://<server>:8096/MovieCode/` öffnen, Code eingeben und danach den gewünschten Film starten.

## Status eines Codes

| Status | Bedeutung |
| --- | --- |
| `Pending` | Code erzeugt, aber noch nicht eingelöst. |
| `Active` | Code eingelöst; der Film ist gebunden und darf abgespielt werden. |
| `Completed` | Film wurde zu Ende geschaut oder der Code wurde verbraucht. |
| `Expired` | Code ist abgelaufen, ohne eingelöst zu werden. |

Ein Paket enthält 10 Codes. Der Status wird pro Code geführt; in der Admin-Oberfläche siehst du zusätzlich die Summe pro Paket.

## HTTP-Endpunkte

| Methode | Pfad | Zugriff | Zweck |
| --- | --- | --- | --- |
| `GET` | `/MovieCode/` bzw. `/MovieCode/Unlock` | anonym | Freigabeseite als Fallback (HTML). |
| `POST` | `/MovieCode/Unlock` | anonym | Code einlösen. Body: `{ "code": "123456", "itemId": "..." }` – `itemId` ist optional. |
| `GET` | `/MovieCode/Status?itemId=...` | angemeldeter Benutzer | Status für das Popup. Liefert `restricted`, `allowed`, `requiresCode`, `itemName` und `message`. |
| `GET` | `/MovieCode/client.js` | anonym | Popup-Skript für den Web-Client. |
| `GET` | `/MovieCode/admin.js` | anonym | Skript, das den Code-Bereich in der Benutzer-Kindersicherung einblendet. Die eigentliche Verwaltung ist nur mit Admin-Rechten möglich. |
| `GET` | `/MovieCode/Admin/Grants` | Admin | Alle Codes aller Pakete auflisten. |
| `POST` | `/MovieCode/Admin/Grants` | Admin | Neues Paket aus 10 Einmalcodes erzeugen. Body: `{ "userId": "..." }`; Antwort enthält `batchId` und `codes` (nur einmalig). |
| `POST` | `/MovieCode/Admin/Grants/{id}/Revoke` | Admin | Einzelnen Code aufheben. |
| `POST` | `/MovieCode/Admin/Grants/Batch/{batchId}/Revoke` | Admin | Gesamtes Code-Paket aufheben. |

Die JSON-Antworten verwenden durchgängig camelCase.

## Sicherheit und Datenschutz

- Codes werden **nicht im Klartext** gespeichert, sondern als PBKDF2-SHA256-Hash mit 16-Byte-Salt und 100.000 Iterationen.
- Für schnelle Lookups und zur Vermeidung doppelter Codes wird zusätzlich ein HMAC-SHA256-Lookup-Hash gespeichert. Der zugehörige Schlüssel liegt in einer separaten Datei, damit ein reines Backup der Freigabedaten keine Rückschlüsse auf gültige Codes erlaubt.
- Jeder Code kann nur einmal eingelöst werden. Nach dem Einlösen wird sein Hash bei Abschluss oder Ablauf entfernt; die übrigen Codes des Pakets bleiben unabhängig davon gültig.
- Ein neues Paket ersetzt alle bisherigen Codes des Benutzers. Die Liste wird nur einmal angezeigt und kann danach nicht erneut aus dem Plugin gelesen werden.
- Fehlversuche werden pro IP begrenzt (Standard: 5 in 10 Minuten).
- Die Freigabeseite ist anonym erreichbar, aktiviert aber nur die Freigabe des zugeordneten Kontos. Der Code selbst ist das Geheimnis.
- Für den produktiven Einsatz wird HTTPS empfohlen, damit Codes nicht im Klartext übertragen werden.

## Bewusste Grenzen

- Das Plugin wertet die von Jellyfin gemeldeten Wiedergaben aus (`ISessionManager`). **Direkte Stream-URLs oder Downloads werden nicht blockiert.** Ein wirksamer Kopierschutz ist damit nicht möglich.
- Das Popup ist nur im Web-Client (Browser, Jellyfin Media Player, PWA) verfügbar. Native Apps zeigen die serverseitige Sperrmeldung mit Link zur Freigabeseite.
- Die Script-Injection ist nicht offiziell und kann durch Web-Client-Updates brechen. Die serverseitige Sperre funktioniert unabhängig davon.
- Standardmäßig werden nur Videos eingeschränkt. Musik, Hörbücher und andere Medien bleiben verfügbar.
- Es ist immer nur ein Film gleichzeitig freigegeben. Ein zweiter Code kann erst nach dem Ende des aktuellen Films eingelöst werden.
- Die Sperre gilt für das ausgewählte Benutzerkonto, nicht für einzelne Geräte.

## Auf GitHub veröffentlichen und als Jellyfin-Repository einbinden

Das Projekt ist so vorbereitet, dass GitHub Actions ein Release mit ZIP und `manifest.json` erzeugt. Dieses Manifest kann Jellyfin direkt als Plugin-Repository hinzufügen.

### 1. Repository anlegen und pushen

```powershell
git init
git add .
git commit -m "Movie Code 1.0.2.1"
git branch -M main
git remote add origin https://github.com/CacheKingDev/Jellyfin-Addons.git
git push -u origin main
```

### 2. Release erzeugen

Vor dem Tag die Version in `Jellyfin.Plugin.MovieCode/Jellyfin.Plugin.MovieCode.csproj` und `build.yaml` erhöhen, dann:

```powershell
git tag v1.0.2.1
git push origin v1.0.2.1
```

Der Workflow `.github/workflows/release.yml` baut und testet das Plugin, erzeugt `MovieCode_1.0.2.1.zip` sowie `manifest.json` und veröffentlicht beides als GitHub-Release.

### 3. Repository in Jellyfin hinzufügen

1. **Dashboard → Plugins → Repositories** öffnen.
2. Folgende URL hinzufügen:

   ```
   https://github.com/CacheKingDev/Jellyfin-Addons/releases/latest/download/manifest.json
   ```

3. Speichern und unter **Dashboard → Plugins → Katalog** nach „Movie Code“ suchen.
4. Plugin installieren und Jellyfin neu starten.

Die URL zeigt immer auf das neueste Release, sodass weitere Versionen automatisch angeboten werden. Bei einem Fork müssen `CacheKingDev/Jellyfin-Addons` durch den eigenen Benutzernamen und Repository-Namen ersetzt werden.

### Hinweise

- Der Workflow benötigt keine zusätzlichen Secrets; das mitgelieferte `GITHUB_TOKEN` besitzt durch `permissions: contents: write` die nötigen Rechte.
- Der Tag muss mit der Plugin-Version übereinstimmen (z. B. `v1.0.2.1`).
- Releases dürfen nicht als Pre-Release markiert werden, sonst zeigt `releases/latest` nicht darauf.
- Alternativ kann `manifest.json` auch über GitHub Pages bereitgestellt werden; die Release-Variante funktioniert jedoch ohne zusätzliche Einrichtung.

## Entwicklung

Voraussetzungen: .NET SDK 10.

```powershell
# Bauen
dotnet build Jellyfin.Plugin.MovieCode.slnx -c Release

# Tests
dotnet test Jellyfin.Plugin.MovieCode.slnx -c Release

# Paket erzeugen (dist/MovieCode_1.0.2.1.zip)
.\build.ps1
```

### Projektstruktur

```
Jellyfin.Plugin.MovieCode/
  Plugin.cs                        Plugin-Klasse, GUID, Konfigurationsseite
  PluginServiceRegistrator.cs      Dependency Injection
  Configuration/                   Globale Einstellungen und Plugin-Konfigurationsseite
  Controllers/                     Admin-API, Status, Freigabeseite, client.js und admin.js
  Models/                          DTOs, Freigabe und Status
  Services/                        Freigaben, Hashing, Rate-Limit, Wiedergabesperre,
                                   Script-Injection in den Web-Client
  Web/                             Freigabeseite, Popup-Skript und Admin-Panel für die Kindersicherung
Jellyfin.Plugin.MovieCode.Tests/   Unit-Tests
assets/                            Plugin-Bild für Katalog und README
```

### Freigabedaten

Die Freigaben liegen in:

```
<Jellyfin-Datenverzeichnis>/plugins/configurations/Jellyfin.Plugin.MovieCode.grants.json
```

Der separate Lookup-Schlüssel liegt daneben in:

```
<Jellyfin-Datenverzeichnis>/plugins/configurations/Jellyfin.Plugin.MovieCode.lookup.key
```

Beide Dateien werden atomar geschrieben und überstehen Neustarts. Der Schlüssel darf nicht gelöscht werden, solange noch Codes eingelöst werden sollen; ohne ihn greift die langsamere PBKDF2-Prüfung.

## Getestet mit

- Jellyfin 12.1.0 (Docker) unter .NET 10
- Unit-Tests: 10er-Code-Pakete, Einmaligkeit, Film-Bindung, nur ein aktiver Film, Restcodes nach Filmende, Paket-Aufhebung, Ablauf, Hashing, Rate-Limit, Persistenz
- Integrationstest: Anmeldung, Bibliotheksscan, Code-Paket erzeugen, Code einlösen, erlaubte und blockierte Wiedergabe, Abschluss nach Filmende, nächster Code, Aufheben der Freigabe, Persistenz über Neustart
- Browsertest (Edge/Playwright): Popup beim Play, Code-Eingabe, Fehlercode, Abbrechen, Sperr-Dialog bei anderem Film, tatsächlicher Videostart nach Freigabe
- Browsertest (Edge/Playwright): Code-Verwaltung in **Benutzer → Kindersicherung**, 10er-Paket erzeugen, Einmalanzeige, Kopieren, TXT-Download, Paketübersicht und Aufheben
