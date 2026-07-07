# release.ps1 - Construit et publie une release LedManager sur GitHub.
# Usage :
#   .\release.ps1                # construit l'archive + release DRAFT
#   .\release.ps1 -Publish      # publie directement (sans draft)
#   .\release.ps1 -PackageOnly  # construit seulement l'archive
param(
    [switch]$Publish,
    [switch]$PackageOnly
)
$ErrorActionPreference = 'Stop'
$sz = @('C:\Program Files\7-Zip\7z.exe','C:\Program Files (x86)\7-Zip\7z.exe') | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $sz) { throw '7-Zip introuvable.' }

$root = Split-Path $PSScriptRoot -Parent
$name = Split-Path $PSScriptRoot -Leaf
$exe  = Join-Path $PSScriptRoot 'LedManager.exe'
$verFull = (Get-Item $exe).VersionInfo.ProductVersion
$ver = ($verFull -split '\+')[0]
Write-Host "Version detectee : $verFull (tag v$ver)"

$out = Join-Path $PSScriptRoot "artifacts\release\v$ver"
New-Item -ItemType Directory -Force $out | Out-Null

$ex = @(
    "-x!$name\.git", "-x!$name\.gitignore", "-x!$name\.github",
    "-x!$name\src", "-x!$name\docs",
    "-x!$name\.archive", "-x!$name\.log", "-x!$name\.temp",
    "-x!$name\.versioning", "-x!$name\state",
    "-x!$name\artifacts", "-x!$name\wiki", "-x!$name\mkdocs.yml", "-x!$name\site",
    "-x!$name\build.bat", "-x!$name\build-LedManager.bat", "-x!$name\build-PicoCommandSender.bat",
    "-x!$name\release.ps1", "-x!$name\LedManager.sln", "-x!$name\Directory.Build.props",
    "-x!$name\build-Setup.bat", "-x!$name\tools\wiki-panels-generator",
    '-xr!CAHIER_DES_CHARGES*', '-xr!*.log', '-xr!__pycache__', '-xr!*.pyc'
)

Set-Location $root
$full = Join-Path $out "$name-$ver-full.7z"
Write-Host 'Construction full.7z...'
& $sz a -t7z $full "$name\" @ex -mx=5 -bsp0 -bso0
# Les schemas de cablage restent utiles hors ligne : on les reinjecte.
& $sz a $full "$name\docs\pico_wiring_diagram.png" "$name\docs\pico_wiring_diagram_fr.png" -bsp0 -bso0

$listing = & $sz l $full
$leaks = $listing | Select-String '\\src\\|\.git|\\state\\|CAHIER|\.sln'
if ($leaks) { throw "FUITE DETECTEE dans l'archive : $($leaks[0])" }
Write-Host 'Controle anti-fuite : OK'

$hashes = Get-FileHash "$out\*.7z" -Algorithm SHA256 | ForEach-Object { '{0}  {1}' -f $_.Hash, (Split-Path $_.Path -Leaf) }
$hashes | Set-Content (Join-Path $out 'SHA256SUMS.txt') -Encoding ascii
Write-Host ($hashes -join "`n")

if ($PackageOnly) { Write-Host 'PackageOnly : archive prete, pas de release.'; exit 0 }

$notes = @"
Voir le wiki pour l'installation : https://nelfe80.github.io/RetroBat-Led-Manager/
Prerequis : APIExpose + runtime .NET 8 Desktop.

Contenu : programme + firmware Pico (fw\) + schemas de cablage (docs\).

### SHA-256
``````
$($hashes -join "`n")
``````
"@
$notesFile = Join-Path $out 'notes.md'
$notes | Set-Content $notesFile -Encoding utf8
$draftFlag = if ($Publish) { @() } else { @('--draft') }
gh release create "v$ver" --repo Nelfe80/RetroBat-Led-Manager --target main @draftFlag --title "LedManager $ver" --notes-file $notesFile $full
Write-Host "Release v$ver creee$(if (-not $Publish) { ' (draft, a publier sur GitHub)' })."
