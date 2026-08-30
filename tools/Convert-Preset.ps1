<#
.SYNOPSIS
    Erzeugt aus einem Arma-3-Launcher-Preset (HTML) die Modlisten-Dateien der Einheit.

.DESCRIPTION
    Liest die <tr data-type="ModContainer">-Eintraege eines exportierten Presets und
    schreibt daraus:

      data/workshop.json  - der Feed, den der TF11 Launcher zur Laufzeit liest
      data/modlist.md     - die menschenlesbare Modliste fuers Repo

    Die Reihenfolge des Presets bleibt erhalten - sie ist die Ladereihenfolge.

.PARAMETER PresetPath
    Pfad zur exportierten Preset-HTML-Datei.

.PARAMETER OutputDir
    Zielordner fuer workshop.json / modlist.md. Standard: data/ neben diesem Skript.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/Convert-Preset.ps1 -PresetPath "data/presets/TaskForce11_Realism_V4.html"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PresetPath,

    [string] $OutputDir
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'data' }

if (-not (Test-Path -LiteralPath $PresetPath)) {
    throw "Preset nicht gefunden: $PresetPath"
}
if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Das Preset ist UTF-8 (mit BOM, vom Arma 3 Launcher so exportiert) - explizit als
# UTF-8 lesen, sonst landen die Umlaute der TF11-Mods als Mojibake in der Ausgabe.
$html = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $PresetPath), [System.Text.Encoding]::UTF8)

$presetName = 'Unbenannt'
$nameMeta = [regex]::Match($html, '<meta\s+name="arma:PresetName"\s+content="([^"]+)"')
if ($nameMeta.Success) { $presetName = $nameMeta.Groups[1].Value }

# Pro Mod-Zeile stehen Anzeigename und Workshop-Link im selben <tr>-Block. Auf den
# Block matchen (statt getrennt auf Name und ID) haelt beide zwangslaeufig gepaart,
# auch wenn ein Eintrag mal ohne Link exportiert wird.
$pattern = '(?s)<td data-type="DisplayName">(?<name>.*?)</td>.*?filedetails/\?id=(?<id>\d+)'
$modMatches = [regex]::Matches($html, $pattern)
if ($modMatches.Count -eq 0) {
    throw "Keine Mods im Preset gefunden - ist $PresetPath wirklich ein Arma-3-Launcher-Preset?"
}

$mods = New-Object System.Collections.Generic.List[object]
$seen = New-Object 'System.Collections.Generic.HashSet[string]'

foreach ($m in $modMatches) {
    $id = $m.Groups['id'].Value
    # Ein Mod kann in einem Preset doppelt auftauchen (z. B. nach einem Merge zweier
    # Presets). Der Launcher wuerde ihn dann zweimal pruefen und zweimal per -mod=
    # uebergeben - einmal reicht.
    if (-not $seen.Add($id)) { continue }

    $name = [System.Net.WebUtility]::HtmlDecode($m.Groups['name'].Value).Trim()
    if ([string]::IsNullOrWhiteSpace($name)) { $name = $id }

    $mods.Add([pscustomobject]@{ id = $id; name = $name })
}

$today = (Get-Date).ToString('yyyy-MM-dd')

# --- workshop.json --------------------------------------------------------------
# Von Hand gebaut statt ConvertTo-Json: das liefert einen stabilen, diff-freundlichen
# Ein-Zeilen-pro-Mod-Stil, und ConvertTo-Json escapt in Windows PowerShell 5.1
# ausserdem Nicht-ASCII als \uXXXX - die Umlaute sollen im Repo lesbar bleiben.
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('{')
[void]$sb.AppendLine("  ""presetName"": ""$presetName"",")
[void]$sb.AppendLine("  ""updated"": ""$today"",")
[void]$sb.AppendLine('  "workshopAddons": [')

for ($i = 0; $i -lt $mods.Count; $i++) {
    $escaped = $mods[$i].name -replace '\\', '\\' -replace '"', '\"'
    $comma = if ($i -lt $mods.Count - 1) { ',' } else { '' }
    [void]$sb.AppendLine("    { ""id"": ""$($mods[$i].id)"", ""name"": ""$escaped"" }$comma")
}

[void]$sb.AppendLine('  ]')
[void]$sb.AppendLine('}')

# UTF8Encoding($false) = ohne BOM. Ein BOM wuerde json-Parser und Git-Diffs nur stoeren.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$workshopPath = Join-Path $OutputDir 'workshop.json'
[System.IO.File]::WriteAllText($workshopPath, $sb.ToString(), $utf8NoBom)

# --- modlist.md -----------------------------------------------------------------
$presetRel = 'presets/' + (Split-Path -Leaf $PresetPath)

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine('# Task Force 11 — Modliste')
[void]$md.AppendLine('')
[void]$md.AppendLine("**Preset:** ``$presetName`` · **Mods:** $($mods.Count) · **Stand:** $today")
[void]$md.AppendLine('')
[void]$md.AppendLine('Diese Liste ist die Referenz für die Pflichtmods der Einheit. Sie wird aus dem')
[void]$md.AppendLine("Arma-3-Launcher-Preset unter [$presetRel]($presetRel)")
[void]$md.AppendLine('erzeugt und in [workshop.json](workshop.json) gespiegelt — das ist der Feed, den der')
[void]$md.AppendLine('TF11 Launcher zur Laufzeit liest.')
[void]$md.AppendLine('')
[void]$md.AppendLine('## Übernehmen ohne Launcher')
[void]$md.AppendLine('')
[void]$md.AppendLine('Preset-Datei herunterladen und im Arma 3 Launcher auf das Fenster ziehen')
[void]$md.AppendLine('(oder: MODS → PRESET → IMPORT).')
[void]$md.AppendLine('')
[void]$md.AppendLine('## Modliste')
[void]$md.AppendLine('')
[void]$md.AppendLine('Die Reihenfolge entspricht der Ladereihenfolge im Preset.')
[void]$md.AppendLine('')
[void]$md.AppendLine('| # | Mod | Workshop-ID | Link |')
[void]$md.AppendLine('|---|-----|-------------|------|')

for ($i = 0; $i -lt $mods.Count; $i++) {
    $id = $mods[$i].id
    # "|" im Anzeigenamen wuerde die Markdown-Tabellenspalte sprengen.
    $name = $mods[$i].name -replace '\|', '\|'
    [void]$md.AppendLine("| $($i + 1) | $name | ``$id`` | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=$id) |")
}

[void]$md.AppendLine('')
[void]$md.AppendLine('## Liste aktualisieren')
[void]$md.AppendLine('')
[void]$md.AppendLine('Neues Preset aus dem Arma 3 Launcher exportieren, nach `data/presets/` legen und')
[void]$md.AppendLine('das Konvertierungsskript laufen lassen — es schreibt `workshop.json` und diese')
[void]$md.AppendLine('Datei neu:')
[void]$md.AppendLine('')
[void]$md.AppendLine('```powershell')
[void]$md.AppendLine('powershell -ExecutionPolicy Bypass -File tools/Convert-Preset.ps1 -PresetPath "data/presets/<neues-preset>.html"')
[void]$md.AppendLine('```')
[void]$md.AppendLine('')
[void]$md.AppendLine('Danach committen und pushen. Der Launcher zieht die neue Liste beim nächsten')
[void]$md.AppendLine('Start automatisch — es braucht kein neues Launcher-Release dafür.')

$modlistPath = Join-Path $OutputDir 'modlist.md'
[System.IO.File]::WriteAllText($modlistPath, $md.ToString(), $utf8NoBom)

Write-Host "Preset : $presetName"
Write-Host "Mods   : $($mods.Count)"
Write-Host "Erzeugt: $workshopPath"
Write-Host "Erzeugt: $modlistPath"
