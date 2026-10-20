#Requires -Version 5.1
<#
.SYNOPSIS
    Baut, testet und paketiert das Plugin "Movie Code".

.DESCRIPTION
    Erzeugt unter "dist" ein installierbares ZIP mit der Plugin-DLL.
    Das ZIP kann direkt in Jellyfins Plugin-Verzeichnis entpackt werden
    (Unterordner "MovieCode") oder über den Plugin-Katalog verteilt werden.

.PARAMETER Configuration
    Build-Konfiguration, standardmäßig Release.

.PARAMETER SkipTests
    Überspringt die Unit-Tests.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "Jellyfin.Plugin.MovieCode\Jellyfin.Plugin.MovieCode.csproj"
$solution = Join-Path $root "Jellyfin.Plugin.MovieCode.slnx"
$dist = Join-Path $root "dist"

Write-Host "Wiederherstellen..." -ForegroundColor Cyan
dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw "dotnet restore ist fehlgeschlagen." }

Write-Host "Bauen ($Configuration)..." -ForegroundColor Cyan
dotnet build $solution -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build ist fehlgeschlagen." }

if (-not $SkipTests) {
    Write-Host "Tests ausführen..." -ForegroundColor Cyan
    dotnet test $solution -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "Die Tests sind fehlgeschlagen." }
}

[xml]$projectXml = Get-Content $project
$version = @($projectXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) { $version = "1.0.0.0" }

$assembly = Join-Path $root "Jellyfin.Plugin.MovieCode\bin\$Configuration\net10.0\Jellyfin.Plugin.MovieCode.dll"
if (-not (Test-Path $assembly)) {
    throw "Plugin-DLL nicht gefunden: $assembly"
}

if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Path $dist | Out-Null

$staging = Join-Path $dist "MovieCode"
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item $assembly $staging

$meta = [ordered]@{
    category    = "General"
    guid        = "6f2b1c9e-4a3d-4f7b-9c21-8e5d0a7b3c64"
    name        = "Movie Code"
    description = "Erlaubt einem ausgewählten Benutzer die Wiedergabe eines Films, nachdem er beim Start einen 6-stelligen Einmalcode eingegeben hat."
    overview    = "Code-Popup beim Filmstart: Ein Einmalcode gibt genau einen Film für einen Benutzer frei."
    owner       = "ben"
    targetAbi   = "12.1.0.0"
    version     = $version
    status      = "Active"
    autoUpdate  = $true
} | ConvertTo-Json

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $staging "meta.json"), $meta, $utf8NoBom)

$zip = Join-Path $dist "MovieCode_$version.zip"
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zip
Remove-Item -Recurse -Force $staging

Write-Host ""
Write-Host "Fertig: $zip" -ForegroundColor Green
Write-Host "Installation: Inhalt nach <Jellyfin-Datenverzeichnis>/plugins/MovieCode entpacken und Jellyfin neu starten."
