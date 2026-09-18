param(
  [string]$Platform = 'StandaloneWindows64',
  [string]$Root = '',
  [switch]$SkipBuildSync,
  [switch]$NoCommit
)

$ErrorActionPreference = 'Stop'

$ProjectId = 4479
$Ref = 'master'
$RemotePath = $Platform
if ([string]::IsNullOrWhiteSpace($Root)) {
  # This script lives in the container folder next to every repository folder.
  $Root = $PSScriptRoot
}
$ProjectDir = Join-Path $Root 'MDPro3'
if (-not (Test-Path -LiteralPath (Join-Path $ProjectDir 'Assets'))) {
  throw "The MDPro3 project folder was not found next to the repositories: $ProjectDir"
}
$RepoDir = Join-Path $Root $Platform
$BuildTarget = Join-Path $Root "Build\MDPro3\$Platform"
$PlatformJunction = Join-Path $ProjectDir "Platforms\$Platform"
$LogsDir = Join-Path $ProjectDir 'Logs'
$LogPrefix = "assetbundles_$($Platform.ToLowerInvariant())"
$ManifestPath = Join-Path $LogsDir "$($LogPrefix)_remote_tree.json"
$UpdatePathList = Join-Path $LogsDir "$($LogPrefix)_update_paths.txt"
$DeletePathList = Join-Path $LogsDir "$($LogPrefix)_delete_paths.txt"

function Require-Command {
  param([string]$Name)
  if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
    throw "$Name was not found in PATH."
  }
}

function Invoke-Git {
  $output = & git -C $RepoDir @args
  if ($LASTEXITCODE -ne 0) {
    throw "git failed: git -C `"$RepoDir`" $args"
  }
  return $output
}

function Get-RemoteTree {
  New-Item -ItemType Directory -Force -Path $LogsDir | Out-Null
  $all = New-Object System.Collections.Generic.List[object]
  $page = 1
  $totalPages = $null

  while ($true) {
    $escapedPath = [uri]::EscapeDataString($RemotePath)
    $url = "https://code.moenext.com/api/v4/projects/$ProjectId/repository/tree?ref=$Ref&path=$escapedPath&recursive=true&per_page=100&page=$page"
    $attempt = 0

    while ($true) {
      try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 90
        break
      } catch {
        $attempt++
        if ($attempt -ge 5) {
          throw
        }
        Write-Host "[AssetBundles] Page $page failed, retry $attempt..."
        Start-Sleep -Seconds ([Math]::Min(30, 3 * $attempt))
      }
    }

    if ($null -eq $totalPages) {
      $totalPages = [int]($response.Headers['X-Total-Pages'] | Select-Object -First 1)
      Write-Host "[AssetBundles] Remote tree pages: $totalPages"
    }

    $items = $response.Content | ConvertFrom-Json
    foreach ($item in $items) {
      $all.Add($item)
    }

    if (($page % 10) -eq 0 -or $page -eq 1 -or $page -eq $totalPages) {
      Write-Host "[AssetBundles] Fetched page $page/$totalPages"
    }

    $next = ($response.Headers['X-Next-Page'] | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($next)) {
      break
    }
    $page = [int]$next
  }

  $all | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ManifestPath -Encoding UTF8
  return $all
}

function Get-LocalBlobMap {
  $map = @{}
  Invoke-Git ls-files -s | ForEach-Object {
    if ($_ -match '^[0-9]+\s+([0-9a-f]{40})\s+[0-9]+\s+(.+)$') {
      $map[$Matches[2]] = $Matches[1]
    }
  }
  return $map
}

function Ensure-LocalRepo {
  if (Test-Path -LiteralPath $RepoDir) {
    if (-not (Test-Path -LiteralPath (Join-Path $RepoDir '.git'))) {
      throw "$RepoDir exists but is not a git repository. Move that folder away, then run this updater again."
    }
    return
  }

  Write-Host "[AssetBundles] Creating local snapshot repository: $RepoDir"
  New-Item -ItemType Directory -Force -Path $RepoDir | Out-Null
  & git -C $RepoDir init | Out-Host
  if ($LASTEXITCODE -ne 0) {
    throw "git init failed for $RepoDir"
  }
  & git -C $RepoDir remote add origin 'https://code.moenext.com/sherry_chaos/mdpro3-assetbundles.git'
  if ($LASTEXITCODE -ne 0) {
    throw "git remote add failed for $RepoDir"
  }
}

function Ensure-PlatformJunction {
  $platformsDir = Join-Path $ProjectDir 'Platforms'
  if (-not (Test-Path -LiteralPath $platformsDir)) {
    New-Item -ItemType Directory -Force -Path $platformsDir | Out-Null
  }

  if (Test-Path -LiteralPath $PlatformJunction) {
    $item = Get-Item -LiteralPath $PlatformJunction
    if ($item.LinkType -eq 'Junction' -or $item.LinkType -eq 'SymbolicLink') {
      return
    }
    throw "$PlatformJunction exists but is not a junction/symlink."
  }

  Write-Host "[AssetBundles] Creating junction: $PlatformJunction -> $RepoDir"
  & cmd /c mklink /J "$PlatformJunction" "$RepoDir" | Out-Host
  if ($LASTEXITCODE -ne 0) {
    throw "Failed to create junction: $PlatformJunction"
  }
}

function Assert-CleanTrackedState {
  & git -C $RepoDir diff --quiet
  if ($LASTEXITCODE -ne 0) {
    throw "Tracked local changes exist in $Platform. Commit/stash them before updating."
  }
  & git -C $RepoDir diff --cached --quiet
  if ($LASTEXITCODE -ne 0) {
    throw "Staged local changes exist in $Platform. Commit/stash them before updating."
  }
}

function Test-InsideRepo {
  param([string]$Path)
  $repoFull = [IO.Path]::GetFullPath($RepoDir).TrimEnd('\') + '\'
  $pathFull = [IO.Path]::GetFullPath($Path)
  return $pathFull.StartsWith($repoFull, [StringComparison]::OrdinalIgnoreCase)
}

function Write-Utf8NoBomLines {
  param(
    [string]$Path,
    [object[]]$Lines
  )

  $encoding = New-Object System.Text.UTF8Encoding($false)
  # An empty list arrives as $null, and WriteAllLines rejects a null array.
  $text = @($Lines | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ })
  [IO.File]::WriteAllLines($Path, [string[]]$text, $encoding)
}

function Download-Blob {
  param(
    [string]$RelativePath,
    [string]$Sha
  )

  $target = Join-Path $RepoDir $RelativePath
  if (-not (Test-InsideRepo $target)) {
    throw "Refusing to write outside repo: $target"
  }

  $dir = Split-Path $target -Parent
  if (-not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
  }

  $tmp = "$target.download"
  $url = "https://code.moenext.com/api/v4/projects/$ProjectId/repository/blobs/$Sha/raw"
  & curl.exe -L --fail --silent --show-error --ssl-no-revoke --retry 5 --retry-delay 3 --connect-timeout 20 -o $tmp $url
  if ($LASTEXITCODE -ne 0) {
    throw "curl failed for $RelativePath"
  }

  $hash = (& git -C $RepoDir hash-object --no-filters -- $tmp).Trim()
  if ($hash -ne $Sha) {
    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    throw "Hash mismatch for $RelativePath. Expected $Sha, got $hash."
  }

  Move-Item -LiteralPath $tmp -Destination $target -Force
}

Require-Command git
Require-Command curl.exe

Ensure-LocalRepo
Ensure-PlatformJunction

Assert-CleanTrackedState

Write-Host "[AssetBundles] Repository: $RepoDir"
Write-Host "[AssetBundles] Platform: $Platform"
Write-Host "[AssetBundles] Querying remote MDPro3-AssetBundles tree..."
$tree = Get-RemoteTree

$prefix = "$RemotePath/"
$remoteMap = @{}
foreach ($item in $tree) {
  if ($item.type -eq 'blob') {
    $relative = $item.path.Substring($prefix.Length)
    $remoteMap[$relative] = $item.id
  }
}

$localMap = Get-LocalBlobMap
$toUpdate = New-Object System.Collections.Generic.List[object]
foreach ($path in ($remoteMap.Keys | Sort-Object)) {
  if (-not $localMap.ContainsKey($path)) {
    $toUpdate.Add([pscustomobject]@{ Path = $path; Sha = $remoteMap[$path]; Status = 'missing' })
  } elseif ($localMap[$path] -ne $remoteMap[$path]) {
    $toUpdate.Add([pscustomobject]@{ Path = $path; Sha = $remoteMap[$path]; Status = 'changed' })
  }
}

$toDelete = New-Object System.Collections.Generic.List[string]
foreach ($path in ($localMap.Keys | Sort-Object)) {
  if (-not $remoteMap.ContainsKey($path)) {
    $toDelete.Add($path)
  }
}

Write-Host "[AssetBundles] Remote blobs: $($remoteMap.Count)"
Write-Host "[AssetBundles] Local tracked blobs: $($localMap.Count)"
Write-Host "[AssetBundles] Changed/missing: $($toUpdate.Count)"
Write-Host "[AssetBundles] Removed upstream: $($toDelete.Count)"

Write-Utf8NoBomLines -Path $UpdatePathList -Lines ($toUpdate | ForEach-Object { $_.Path })
Write-Utf8NoBomLines -Path $DeletePathList -Lines $toDelete

$index = 0
foreach ($item in $toUpdate) {
  $index++
  if ($toUpdate.Count -le 300 -or $index -eq 1 -or $index -eq $toUpdate.Count -or ($index % 25) -eq 0) {
    Write-Host "[AssetBundles] [$index/$($toUpdate.Count)] $($item.Status) $($item.Path)"
  }
  Download-Blob -RelativePath $item.Path -Sha $item.Sha
}

if ($toDelete.Count -gt 0) {
  Write-Host "[AssetBundles] Removing files deleted upstream..."
  foreach ($path in $toDelete) {
    $target = Join-Path $RepoDir $path
    if (-not (Test-InsideRepo $target)) {
      throw "Refusing to remove outside repo: $target"
    }
  }
  Invoke-Git rm --pathspec-from-file=$DeletePathList | Out-Null
}

if ($toUpdate.Count -gt 0) {
  Invoke-Git add --pathspec-from-file=$UpdatePathList | Out-Null
}

& git -C $RepoDir diff --cached --quiet
$hasStagedChanges = ($LASTEXITCODE -ne 0)

if ($hasStagedChanges -and -not $NoCommit) {
  $remoteHead = (& git ls-remote https://code.moenext.com/sherry_chaos/mdpro3-assetbundles.git refs/heads/master).Split()[0]
  $shortRemote = $remoteHead.Substring(0, 7)
  Invoke-Git commit -m "local sync assetbundles $Platform master $shortRemote" | Out-Host
} elseif ($hasStagedChanges) {
  Write-Host "[AssetBundles] Changes are staged but not committed because -NoCommit was used."
} else {
  Write-Host "[AssetBundles] No local assetbundle changes were needed."
}

if (-not $SkipBuildSync -and $Platform -eq 'StandaloneWindows64') {
  if (Test-Path -LiteralPath (Split-Path $BuildTarget -Parent)) {
    Write-Host "[AssetBundles] Syncing to build folder..."
    & robocopy $RepoDir $BuildTarget /MIR /XJ /XD .git /NFL /NDL /NJH /NJS /NP | Out-Host
    $robocopyCode = $LASTEXITCODE
    if ($robocopyCode -ge 8) {
      throw "Robocopy failed with code $robocopyCode."
    }
  } else {
    Write-Host "[AssetBundles] Build folder not found. Build sync skipped."
  }
} else {
  Write-Host "[AssetBundles] Build folder sync skipped."
}

$finalLocalMap = Get-LocalBlobMap
$missing = 0
$changed = 0
$extra = 0
foreach ($path in $remoteMap.Keys) {
  if (-not $finalLocalMap.ContainsKey($path)) {
    $missing++
  } elseif ($finalLocalMap[$path] -ne $remoteMap[$path]) {
    $changed++
  }
}
foreach ($path in $finalLocalMap.Keys) {
  if (-not $remoteMap.ContainsKey($path)) {
    $extra++
  }
}

$head = (Invoke-Git rev-parse --short HEAD).Trim()
Write-Host "[AssetBundles] Verify: missing=$missing changed=$changed extra=$extra"
Write-Host "[AssetBundles] Local HEAD: $head"
Write-Host "[AssetBundles] Done. This script never pushes to the remote repository."
