# Movie Code

Ein Jellyfin-Plugin, mit dem ein Administrator **genau einen Film für genau einen Benutzer** freigibt. Der Benutzer erhält einen **6-stelligen Einmalcode**, löst ihn auf einer separaten Freigabeseite ein und kann danach ausschließlich diesen Film abspielen. Sobald der Film zu Ende ist, verfällt die Freigabe.

Zielplattform: **Jellyfin 12.1** mit **.NET 10**.

## Funktionsweise

1. Der Administrator wählt in der Plugin-Konfiguration einen Benutzer und einen Film aus.
2. Das Plugin erzeugt einen 6-stelligen Code und zeigt ihn **einmalig** an.
3. Der Benutzer öffnet `http://<server>:8096/MovieCode/` und gibt den Code ein.
4. Die Freigabe wechselt von `Pending` zu `Active`.
5. Der Benutzer kann nun in Jellyfin den freigegebenen Film starten. Jede andere Video-Wiedergabe wird beendet.
6. Wird der Film vollständig angesehen (oder zu mindestens 95 %), wechselt die Freigabe auf `Completed`. Der Benutzer bleibt gesperrt, bis der Administrator die Freigabe aufhebt – optional kann er automatisch wieder freigeschaltet werden.

Eine neue Freigabe für denselben Benutzer ersetzt die vorherige.

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
| `LockedMessageHeader` | `Film gesperrt` | Überschrift der Meldung, die bei einer blockierten Wiedergabe erscheint. |
| `LockedMessageText` | siehe unten | Text der Meldung. |

Standardtext der Sperrmeldung:

> Bitte gib zuerst deinen 6-stelligen Freigabecode auf der Freigabeseite ein.

## Benutzung

### Als Administrator

1. **Dashboard → Plugins → Movie Code** öffnen.
2. Benutzer auswählen.
3. Nach dem Film suchen und ihn auswählen.
4. **Freigabe erzeugen** – der 6-stellige Code wird nur jetzt angezeigt.
5. Code an den Benutzer weitergeben (z. B. persönlich oder über einen sicheren Kanal).
6. Unter „Bestehende Freigaben“ kann der Status eingesehen und eine Freigabe aufgehoben werden.

### Als Benutzer

1. Freigabeseite öffnen: `http://<server>:8096/MovieCode/`
2. 6-stelligen Code eingeben.
3. Nach der Bestätigung den freigegebenen Film in Jellyfin starten.

## Status einer Freigabe

| Status | Bedeutung |
| --- | --- |
| `Pending` | Code erzeugt, aber noch nicht eingelöst. |
| `Active` | Code eingelöst, Film darf abgespielt werden. |
| `Completed` | Film wurde zu Ende geschaut oder die Freigabe wurde verbraucht. |
| `Expired` | Code ist abgelaufen, ohne eingelöst zu werden. |

## HTTP-Endpunkte

| Methode | Pfad | Zugriff | Zweck |
| --- | --- | --- | --- |
| `GET` | `/MovieCode/` bzw. `/MovieCode/Unlock` | anonym | Freigabeseite (HTML). |
| `POST` | `/MovieCode/Unlock` | anonym | Code einlösen. Body: `{ "Code": "123456" }` |
| `GET` | `/MovieCode/Admin/Grants` | Admin | Alle Freigaben auflisten. |
| `POST` | `/MovieCode/Admin/Grants` | Admin | Freigabe erzeugen. Body: `{ "UserId": "...", "ItemId": "..." }` |
| `POST` | `/MovieCode/Admin/Grants/{id}/Revoke` | Admin | Freigabe aufheben. |

## Sicherheit und Datenschutz

- Codes werden **nicht im Klartext** gespeichert, sondern als PBKDF2-SHA256-Hash mit 16-Byte-Salt und 100.000 Iterationen.
- Nach dem Einlösen wird der Hash entfernt; der Code kann nicht erneut verwendet werden.
- Fehlversuche werden pro IP begrenzt (Standard: 5 in 10 Minuten).
- Die Freigabeseite ist anonym erreichbar, aktiviert aber nur die Freigabe des zugeordneten Kontos. Der Code selbst ist das Geheimnis.
- Für den produktiven Einsatz wird HTTPS empfohlen, damit Codes nicht im Klartext übertragen werden.

## Bewusste Grenzen

- Das Plugin wertet die von Jellyfin gemeldeten Wiedergaben aus (`ISessionManager`). **Direkte Stream-URLs oder Downloads werden nicht blockiert.** Ein wirksamer Kopierschutz ist damit nicht möglich.
- Die native Jellyfin-Oberfläche wird nicht verändert. Die Code-Eingabe erfolgt über die separate Seite `/MovieCode/`.
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
  Controllers/                     Admin-API und öffentliche Freigabeseite
  Models/                          DTOs, Freigabe und Status
  Services/                        Freigaben, Hashing, Rate-Limit, Wiedergabesperre
  Web/                             Eingebettete Freigabeseite
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
- Unit-Tests: Freigabe-Lebenszyklus, Hashing, Rate-Limit
- Integrationstest: Anmeldung, Bibliotheksscan, Freigabe erzeugen, Code einlösen, erlaubte und blockierte Wiedergabe, Abschluss nach Filmende, Aufheben der Freigabe, Persistenz über Neustart
