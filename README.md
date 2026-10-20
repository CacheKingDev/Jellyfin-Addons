# Movie Code

Ein Jellyfin-Plugin, mit dem ein Administrator **einen Benutzer** für **einen Film** freigibt. Der Benutzer kann die Mediathek normal durchsuchen und Filme ansehen. Sobald er einen Film startet, fragt das Plugin den **6-stelligen Einmalcode** in einem Popup im Web-Client ab. Nach der Eingabe ist genau dieser Film freigegeben. Sobald der Film zu Ende ist, verfällt die Freigabe.

Zielplattform: **Jellyfin 12.1** mit **.NET 10**.

## Funktionsweise

1. Der Administrator wählt in der Plugin-Konfiguration nur den Benutzer aus. Das Plugin erzeugt einen 6-stelligen Code und zeigt ihn **einmalig** an.
2. Der Benutzer durchsucht die Mediathek wie gewohnt. Beim Drücken auf **Abspielen** erscheint das Code-Popup.
3. Nach Eingabe des Codes wird die Freigabe an den tatsächlich gestarteten Film gebunden.
4. Der Film startet. Jede andere Video-Wiedergabe wird beendet.
5. Wird der Film vollständig angesehen (oder zu mindestens 95 %), wechselt die Freigabe auf `Completed`. Der Benutzer bleibt gesperrt, bis der Administrator die Freigabe aufhebt – optional kann er automatisch wieder freigeschaltet werden.

Eine neue Freigabe für denselben Benutzer ersetzt die vorherige. Der Film wird erst beim Einlösen des Codes bzw. beim ersten Wiedergabestart festgelegt; der Administrator muss ihn nicht auswählen.

### Popup und native Apps

Das Popup wird per Script-Injection in den Jellyfin-Web-Client eingebunden. Es funktioniert in:

- Browsern (Desktop und mobil)
- Jellyfin Media Player
- installierbaren PWAs

Native Apps (Android TV, Roku, iOS/Android-App) können kein Popup anzeigen. Dort erscheint weiterhin die serverseitige Sperrmeldung mit dem Link auf die Freigabeseite. Über diese Seite kann der Code eingegeben werden; der erste danach gestartete Film wird gebunden.

> Die Script-Injection ist kein offizieller Jellyfin-Plugin-Hook. Sie kann durch Änderungen am Web-Client brechen. Die serverseitige Sperre bleibt davon unabhängig bestehen.

## Installation

1. `build.ps1` ausführen oder die Release-DLL aus `Jellyfin.Plugin.MovieCode/bin/Release/net10.0/` verwenden.
2. Im Jellyfin-Datenverzeichnis den Ordner `plugins/MovieCode` anlegen.
3. `Jellyfin.Plugin.MovieCode.dll` hineinkopieren.
4. Jellyfin neu starten.
5. Unter **Dashboard → Plugins** erscheint „Movie Code“. Die Konfiguration liegt unter **Dashboard → Plugins → Movie Code**.

Das Skript `build.ps1` erzeugt zusätzlich ein installierbares ZIP unter `dist/`.

## Konfiguration

| Einstellung | Standard | Bedeutung |
| --- | --- | --- |
| `UnlockPageUrl` | leer | Öffentliche Adresse der Freigabeseite. Leer = automatisch aus der Anfrage ermitteln. |
| `CodeValidityHours` | `24` | Gültigkeit des Codes in Stunden. `0` = unbegrenzt, bis der Film endet. |
| `MaxFailedAttemptsPerTenMinutes` | `5` | Maximale Fehlversuche pro IP in 10 Minuten. `0` = unbegrenzt. |
| `ExpireOnEarlyStop` | `false` | Freigabe auch dann verbrauchen, wenn der Film vorzeitig gestoppt wird. |
| `ReleaseAfterCompletion` | `false` | Benutzer nach dem Filmende automatisch wieder uneingeschränkt freischalten. |
| `RestrictOnlyVideo` | `true` | Nur Videos sperren. Musik und Hörbücher bleiben erlaubt. |
| `EnableWebPopup` | `true` | Code-Popup im Jellyfin-Web-Client anzeigen. |
| `LockedMessageHeader` | `Film gesperrt` | Überschrift der Meldung, die bei einer blockierten Wiedergabe erscheint. |
| `LockedMessageText` | siehe unten | Text der Meldung. |

Standardtext der Sperrmeldung:

> Bitte gib zuerst deinen 6-stelligen Freigabecode auf der Freigabeseite ein.

## Benutzung

### Als Administrator

1. **Dashboard → Plugins → Movie Code** öffnen.
2. Benutzer auswählen.
3. **6-stelligen Code erzeugen** – der Code wird nur jetzt angezeigt.
4. Code an den Benutzer weitergeben (z. B. persönlich oder über einen sicheren Kanal).
5. Unter „Bestehende Freigaben“ kann der Status eingesehen und eine Freigabe aufgehoben werden. Solange der Benutzer noch keinen Film gestartet hat, steht dort „Film noch nicht gewählt“.

### Als Benutzer

1. In Jellyfin einen Film auswählen und **Abspielen** drücken.
2. Im Popup den 6-stelligen Code eingeben und **Starten** drücken.
3. Der Film startet. Andere Filme bleiben gesperrt, bis die Freigabe endet oder der Administrator sie aufhebt.

**Fallback für native Apps:** `http://<server>:8096/MovieCode/` öffnen, Code eingeben und danach den gewünschten Film starten.

## Status einer Freigabe

| Status | Bedeutung |
| --- | --- |
| `Pending` | Code erzeugt, aber noch nicht eingelöst. |
| `Active` | Code eingelöst; der Film ist gebunden und darf abgespielt werden. |
| `Completed` | Film wurde zu Ende geschaut oder die Freigabe wurde verbraucht. |
| `Expired` | Code ist abgelaufen, ohne eingelöst zu werden. |

## HTTP-Endpunkte

| Methode | Pfad | Zugriff | Zweck |
| --- | --- | --- | --- |
| `GET` | `/MovieCode/` bzw. `/MovieCode/Unlock` | anonym | Freigabeseite als Fallback (HTML). |
| `POST` | `/MovieCode/Unlock` | anonym | Code einlösen. Body: `{ "code": "123456", "itemId": "..." }` – `itemId` ist optional. |
| `GET` | `/MovieCode/Status?itemId=...` | angemeldeter Benutzer | Status für das Popup. Liefert `restricted`, `allowed`, `requiresCode`, `itemName` und `message`. |
| `GET` | `/MovieCode/client.js` | anonym | Popup-Skript für den Web-Client. |
| `GET` | `/MovieCode/Admin/Grants` | Admin | Alle Freigaben auflisten. |
| `POST` | `/MovieCode/Admin/Grants` | Admin | Freigabe erzeugen. Body: `{ "userId": "..." }` |
| `POST` | `/MovieCode/Admin/Grants/{id}/Revoke` | Admin | Freigabe aufheben. |

Die JSON-Antworten verwenden durchgängig camelCase.

## Sicherheit und Datenschutz

- Codes werden **nicht im Klartext** gespeichert, sondern als PBKDF2-SHA256-Hash mit 16-Byte-Salt und 100.000 Iterationen.
- Ein Code kann nur einmal eingelöst werden. Nach dem Einlösen akzeptiert die Freigabe keinen weiteren Code; der Hash wird bei Abschluss oder Ablauf entfernt.
- Fehlversuche werden pro IP begrenzt (Standard: 5 in 10 Minuten).
- Die Freigabeseite ist anonym erreichbar, aktiviert aber nur die Freigabe des zugeordneten Kontos. Der Code selbst ist das Geheimnis.
- Für den produktiven Einsatz wird HTTPS empfohlen, damit Codes nicht im Klartext übertragen werden.

## Bewusste Grenzen

- Das Plugin wertet die von Jellyfin gemeldeten Wiedergaben aus (`ISessionManager`). **Direkte Stream-URLs oder Downloads werden nicht blockiert.** Ein wirksamer Kopierschutz ist damit nicht möglich.
- Das Popup ist nur im Web-Client (Browser, Jellyfin Media Player, PWA) verfügbar. Native Apps zeigen die serverseitige Sperrmeldung mit Link zur Freigabeseite.
- Die Script-Injection ist nicht offiziell und kann durch Web-Client-Updates brechen. Die serverseitige Sperre funktioniert unabhängig davon.
- Standardmäßig werden nur Videos eingeschränkt. Musik, Hörbücher und andere Medien bleiben verfügbar.
- Die Sperre gilt für das ausgewählte Benutzerkonto, nicht für einzelne Geräte.

## Auf GitHub veröffentlichen und als Jellyfin-Repository einbinden

Das Projekt ist so vorbereitet, dass GitHub Actions ein Release mit ZIP und `manifest.json` erzeugt. Dieses Manifest kann Jellyfin direkt als Plugin-Repository hinzufügen.

### 1. Repository anlegen und pushen

```powershell
git init
git add .
git commit -m "Movie Code 1.0.0.0"
git branch -M main
git remote add origin https://github.com/CacheKingDev/Jellyfin-Addons.git
git push -u origin main
```

### 2. Release erzeugen

Vor dem Tag die Version in `Jellyfin.Plugin.MovieCode/Jellyfin.Plugin.MovieCode.csproj` und `build.yaml` erhöhen, dann:

```powershell
git tag v1.0.0.0
git push origin v1.0.0.0
```

Der Workflow `.github/workflows/release.yml` baut und testet das Plugin, erzeugt `MovieCode_1.0.0.0.zip` sowie `manifest.json` und veröffentlicht beides als GitHub-Release.

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
- Der Tag muss mit der Plugin-Version übereinstimmen (z. B. `v1.0.0.0`).
- Releases dürfen nicht als Pre-Release markiert werden, sonst zeigt `releases/latest` nicht darauf.
- Alternativ kann `manifest.json` auch über GitHub Pages bereitgestellt werden; die Release-Variante funktioniert jedoch ohne zusätzliche Einrichtung.

## Entwicklung

Voraussetzungen: .NET SDK 10.

```powershell
# Bauen
dotnet build Jellyfin.Plugin.MovieCode.slnx -c Release

# Tests
dotnet test Jellyfin.Plugin.MovieCode.slnx -c Release

# Paket erzeugen (dist/MovieCode_1.0.0.0.zip)
.\build.ps1
```

### Projektstruktur

```
Jellyfin.Plugin.MovieCode/
  Plugin.cs                        Plugin-Klasse, GUID, Konfigurationsseite
  PluginServiceRegistrator.cs      Dependency Injection
  Configuration/                   Einstellungen und Admin-Oberfläche
  Controllers/                     Admin-API, Status, Freigabeseite und client.js
  Models/                          DTOs, Freigabe und Status
  Services/                        Freigaben, Hashing, Rate-Limit, Wiedergabesperre,
                                   Script-Injection in den Web-Client
  Web/                             Eingebettete Freigabeseite und Popup-Skript
Jellyfin.Plugin.MovieCode.Tests/   Unit-Tests
```

### Freigabedaten

Die Freigaben liegen in:

```
<Jellyfin-Datenverzeichnis>/plugins/configurations/Jellyfin.Plugin.MovieCode.grants.json
```

Die Datei wird atomar geschrieben und übersteht Neustarts.

## Getestet mit

- Jellyfin 12.1.0 (Docker) unter .NET 10
- Unit-Tests: Freigabe-Lebenszyklus, Film-Bindung, Hashing, Rate-Limit
- Integrationstest: Anmeldung, Bibliotheksscan, Freigabe erzeugen, Code einlösen, erlaubte und blockierte Wiedergabe, Abschluss nach Filmende, Aufheben der Freigabe, Persistenz über Neustart
- Browsertest (Edge/Playwright): Popup beim Play, Code-Eingabe, Fehlercode, Abbrechen, Sperr-Dialog bei anderem Film, tatsächlicher Videostart nach Freigabe
