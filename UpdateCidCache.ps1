<#
  UpdateCidCache.ps1

  Rebuilds Data\cards_Lite.json, the CID to Ydk lookup table that Cid2Ydk loads
  on the first deck import or voice lookup.

  Cid2Ydk builds that table by merging Data\cards.json with Data\cards_Alt.json,
  then caches the merged result in Data\cards_Lite.json. cards.json has been
  dropped from the repository, so from that point on the cache file is the only
  mapping data on disk and it can never be regenerated from scratch. This script
  refreshes the cache instead of throwing it away: the current cards_Alt.json is
  laid over the existing entries, so every mapping cards.json contributed is kept
  and newly added alternative art entries are picked up.

  Order matters. cards_Alt.json wins over the base table, exactly like the
  foreach loop in Cid2Ydk.Initialize.

  Options:
    -Root <dir>      Repository root. Defaults to the MDPro3 project folder next
                     to this script.
    -BuildDir <dir>  Also refresh the copy inside <dir>\Data when that folder exists.
    -Quiet           Print nothing but failures.

  Exit codes:
    0  Cache written or already up to date.
    2  No usable base table, the existing cache is left untouched.
#>
param(
  [string]$Root = '',
  [string]$BuildDir = '',
  [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

function Write-Note {
  param([string]$Text)
  if (-not $Quiet) { Write-Host "[CidCache] $Text" }
}

function Get-CardPairs {
  param([string]$Text, [string]$Source)

  $pattern = '"(\d+)"\s*:\s*\{\s*"cid"\s*:\s*(\d+)\s*,\s*"id"\s*:\s*(\d+)'
  $pairs = New-Object System.Collections.Generic.List[object]
  foreach ($match in [regex]::Matches($Text, $pattern)) {
    $pairs.Add([pscustomobject]@{
      Key = $match.Groups[1].Value
      Cid = $match.Groups[2].Value
      Id  = $match.Groups[3].Value
    })
  }

  if ($pairs.Count -eq 0) { throw "No card entries found in $Source" }
  return $pairs
}

if ([string]::IsNullOrWhiteSpace($Root)) {
  # This script lives in the container folder; the MDPro3 project folder next to
  # it is the repository that holds Data.
  $projectDir = Join-Path $PSScriptRoot 'MDPro3'
  $Root = if (Test-Path -LiteralPath (Join-Path $projectDir 'Data')) { $projectDir } else { $PSScriptRoot }
}

$dataDir = Join-Path $Root 'Data'
$litePath = Join-Path $dataDir 'cards_Lite.json'
$cardsPath = Join-Path $dataDir 'cards.json'
$altPath = Join-Path $dataDir 'cards_Alt.json'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

if (-not (Test-Path -LiteralPath $altPath)) {
  Write-Note "cards_Alt.json is missing, the cache is left untouched: $altPath"
  exit 2
}

$altPairs = Get-CardPairs -Text ([System.IO.File]::ReadAllText($altPath, [System.Text.Encoding]::UTF8)) -Source 'cards_Alt.json'

if (Test-Path -LiteralPath $cardsPath) {
  $basePairs = Get-CardPairs -Text ([System.IO.File]::ReadAllText($cardsPath, [System.Text.Encoding]::UTF8)) -Source 'cards.json'
  $baseName = 'cards.json'
}
elseif (Test-Path -LiteralPath $litePath) {
  $basePairs = Get-CardPairs -Text ([System.IO.File]::ReadAllText($litePath, [System.Text.Encoding]::UTF8)) -Source 'cards_Lite.json'
  $baseName = 'the cached cards_Lite.json'
}
else {
  Write-Note 'Neither cards.json nor cards_Lite.json is available, the cache cannot be rebuilt.'
  exit 2
}

$table = [ordered]@{}
foreach ($pair in $basePairs) { $table[$pair.Key] = $pair }
foreach ($pair in $altPairs) { $table[$pair.Key] = $pair }

$builder = New-Object System.Text.StringBuilder
[void]$builder.Append('{')
$isFirst = $true
foreach ($key in $table.Keys) {
  $pair = $table[$key]
  if (-not $isFirst) { [void]$builder.Append(',') }
  $isFirst = $false
  [void]$builder.AppendFormat('"{0}":{{"cid":{1},"id":{2}}}', $pair.Key, $pair.Cid, $pair.Id)
}
[void]$builder.Append('}')
$json = $builder.ToString()

function Save-CidCache {
  param([string]$Path, [string]$Label)

  if (Test-Path -LiteralPath $Path) {
    $current = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    if ($current -eq $json) {
      Write-Note "$Label is already up to date: $Path"
      return
    }
  }

  $folder = Split-Path -Parent $Path
  if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Force -Path $folder | Out-Null }
  [System.IO.File]::WriteAllText($Path, $json, $utf8NoBom)
  Write-Note "$Label refreshed: $Path"
}

Save-CidCache -Path $litePath -Label 'cards_Lite.json'

if (-not [string]::IsNullOrWhiteSpace($BuildDir)) {
  $buildDataDir = Join-Path $BuildDir 'Data'
  if (Test-Path -LiteralPath $buildDataDir) {
    Save-CidCache -Path (Join-Path $buildDataDir 'cards_Lite.json') -Label 'Build cards_Lite.json'
  }
}

Write-Note "$($table.Count) entries, base table: $baseName, $($altPairs.Count) cards_Alt.json entries overlaid."
exit 0
