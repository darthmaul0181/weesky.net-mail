#requires -Version 7
[CmdletBinding()]
param(
    [switch]$SetupOnly,
    [string[]]$Suites,
    [switch]$PrintResponses
)
$ErrorActionPreference = 'Stop'

$testerCommit = 'bed21e5924275552c1561febc8203a9f194cf737'
$pycalendarCommit = 'a12dd4e1ce8822b022d4abf2cfe6cc93902ff03f'
$work = Join-Path $PSScriptRoot '.caldavtester'
$results = Join-Path $PSScriptRoot 'results'

# L'outil est du Python 2 (spec, décision 1) et ne tournera sur rien d'autre.
& py -2.7 -c 'import sys' 2>$null
if ($LASTEXITCODE -ne 0) { throw "Python 2.7 introuvable ('py -2.7'). Installer 2.7.18 : voir README.md." }

function Get-Pinned([string]$Name, [string]$Url, [string]$Commit) {
    $dir = Join-Path $work $Name
    if (-not (Test-Path $dir)) {
        git clone --quiet $Url $dir | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "clone de $Url a échoué" }
    }
    git -C $dir -c advice.detachedHead=false checkout --quiet $Commit
    if ($LASTEXITCODE -ne 0) { throw "checkout $Commit a échoué dans $dir" }
    return $dir
}
$tester = Get-Pinned 'ccs-caldavtester' 'https://github.com/apple/ccs-caldavtester.git' $testerCommit
$pycalendar = Get-Pinned 'ccs-pycalendar' 'https://github.com/apple/ccs-pycalendar.git' $pycalendarCommit

# serverinfo.xml : le gabarit versionné + les trois valeurs du fichier local ignoré.
$localPath = Join-Path $PSScriptRoot 'serverinfo.local.json'
if (-not (Test-Path $localPath)) {
    throw 'serverinfo.local.json manquant : copier serverinfo.local.example.json et le remplir.'
}
$local = Get-Content $localPath -Raw | ConvertFrom-Json
foreach ($key in 'guid', 'email', 'secret') {
    if (-not $local.$key) { throw "serverinfo.local.json : champ '$key' vide." }
}
$serverinfoPath = Join-Path $PSScriptRoot 'serverinfo.xml'
(Get-Content (Join-Path $PSScriptRoot 'serverinfo.template.xml') -Raw).
    Replace('{guid}', $local.guid).Replace('{email}', $local.email).Replace('{secret}', $local.secret) |
    Set-Content -Path $serverinfoPath -NoNewline
if ($SetupOnly) { Write-Host "Prêt : $serverinfoPath"; exit 0 }

if (-not $Suites) {
    $Suites = Get-Content (Join-Path $PSScriptRoot 'suites.txt') |
        ForEach-Object { ($_ -split '#')[0].Trim() } | Where-Object { $_ }
}

# --print-details-onfail imprime la requête entière sur chaque échec, Authorization
# comprise : la sortie est épurée AVANT de toucher le disque (décision 6).
New-Item -ItemType Directory -Force $results | Out-Null
$out = Join-Path $results ("{0:yyyyMMdd-HHmmss}.txt" -f (Get-Date))
$flags = @('--ssl', '--print-details-onfail', '-s', $serverinfoPath)
if ($PrintResponses) { $flags += '--always-print-response' }
$env:PYTHONPATH = Join-Path $pycalendar 'src'
Push-Location $tester
try {
    & py -2.7 testcaldav.py @flags @Suites 2>&1 |
        ForEach-Object { "$_" -replace '(?i)^(.*Authorization:).*$', '$1 [scrubbed]' } |
        Tee-Object -FilePath $out | Select-Object -Last 60
}
finally {
    Pop-Location
    Remove-Item Env:PYTHONPATH -ErrorAction SilentlyContinue
}
Write-Host "`nSortie épurée : $out"
