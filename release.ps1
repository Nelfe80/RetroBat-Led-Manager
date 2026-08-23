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
    '-xr!CAHIER_DES_CHARGES*', '-xr!*.log', '-xr!__pycache__', '-xr!*.pyc',
    # Outillage interne : deploiement du firmware, sondes de latence,
    # balayages de couleur, snapshots de version. Le runtime n'en lit aucun.
    # Ils partaient dans les packs depuis toujours — le controle plus bas
    # ne les cherchait simplement pas. fw\*.py RESTE : c'est le
    # firmware que le joueur flashe sur son Pico, pas un outil.
    "-x!$name\tools\*.ps1", "-x!$name\tools\*.py",
    # Sauvegardes de travail : un .bak d'ini peut porter une config machine.
    '-xr!*.bak',
    # Sorties de build, si l'arbre en garde.
    '-xr!*.pdb', "-x!$name\obj", "-x!$name\bin",
    "-x!$name\tests", "-x!$name\dist", "-x!$name\installer"
)

Set-Location $root
$full = Join-Path $out "$name-$ver-full.7z"
Write-Host 'Construction full.7z...'
& $sz a -t7z $full "$name\" @ex -mx=5 -bsp0 -bso0
# Les schemas de cablage restent utiles hors ligne : on les reinjecte.
& $sz a $full "$name\docs\pico_wiring_diagram.png" "$name\docs\pico_wiring_diagram_fr.png" -bsp0 -bso0

$listing = & $sz l $full
$leaks = $listing | Select-String '\\src\\|\.git|\\state\\|CAHIER|\.sln|\\tools\\.*\.(ps1|py)$|\.bak$|\.pdb$|\\obj\\|\\bin\\|publish-tmp|\.env$'
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
# Les arguments de gh passent par un TABLEAU, jamais par un splat au milieu
# d'une ligne de commande native : PowerShell laisse alors l'expansion de gh
# interpreter le drapeau, et gh repond « no matches found for - ».
$ghArgs = @(
    'release', 'create', "v$ver",
    '--repo', 'Nelfe80/RetroBat-Led-Manager',
    '--target', 'main',
    '--title', "LedManager $ver",
    '--notes-file', $notesFile
)
if (-not $Publish) { $ghArgs += '--draft' }
$ghArgs += $full
& gh @ghArgs
if ($LASTEXITCODE -ne 0) { throw "gh release create a echoue (exit $LASTEXITCODE)." }
Write-Host "Release v$ver creee$(if (-not $Publish) { ' (draft, a publier sur GitHub)' })."
