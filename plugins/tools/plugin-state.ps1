<#
    MDPro3 plugin state helper.

    Keeps the plugin sources of <repo>\plugins\MDPro3Plugins in sync with the
    Unity project folder <repo>\MDPro3\Assets\MDPro3Plugins (that is the only
    location Unity compiles into Assembly-CSharp) and tracks whether the built
    player already contains the current plugin sources.

    Actions
      sync   copy the sources into the Unity project and print the state
      status same as sync but without writing anything (used by --check)
      stamp  remember that the built player was built with the current sources
      clean  remove the copied sources from the Unity project

    The last line of the output is always "PLUGINSTATUS=<state>" with one of
      OK       synced and the built player is up to date
      CHANGED  synced, the built player has to be rebuilt
      MISSING  synced, but there is no built player yet
      OFF      plugin removed from the Unity project
      ERROR    something went wrong, see PLUGINMESSAGE

    The scripts only touch <repo>\plugins and <repo>\MDPro3\Assets\MDPro3Plugins.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('sync', 'status', 'stamp', 'wait', 'clean')]
    [string]$Action,

    [string]$Source,
    [string]$Target,
    [string]$State,
    [string]$BuiltExe,
    [string]$Signature,
    [int]$TimeoutSeconds = 30,
    [switch]$PluginOff
)

$ErrorActionPreference = 'Stop'

function Write-Result {
    param([string]$State, [string]$Message)

    if (-not [string]::IsNullOrEmpty($Message)) {
        Write-Output ("PLUGINMESSAGE=" + $Message)
    }
    Write-Output ("PLUGINSTATUS=" + $State)
}

function Get-PluginFiles {
    param([string]$Root)

    if ([string]::IsNullOrEmpty($Root) -or -not (Test-Path -LiteralPath $Root)) {
        return @()
    }

    # Source-authored metadata (e.g. transparent wrapper texture import settings) is
    # part of the plugin. Unity-generated metadata only in Target is still preserved.
    return @(Get-ChildItem -LiteralPath $Root -Recurse -File |
        Sort-Object -Property FullName)
}

function Get-PluginSignature {
    param([string]$Root)

    $builder = New-Object System.Text.StringBuilder
    foreach ($file in Get-PluginFiles -Root $Root) {
        $relative = $file.FullName.Substring($Root.Length).TrimStart('\', '/')
        [void]$builder.Append($relative)
        [void]$builder.Append('|')
        [void]$builder.Append($file.Length)
        [void]$builder.Append('|')
        [void]$builder.Append($file.LastWriteTimeUtc.Ticks)
        [void]$builder.Append("`n")
    }

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($builder.ToString())
        $hash = $sha.ComputeHash($bytes)
    }
    finally {
        $sha.Dispose()
    }

    return (($hash | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-NewestWriteTime {
    param([string]$Root)

    $newest = [DateTime]::MinValue
    foreach ($file in Get-PluginFiles -Root $Root) {
        if ($file.LastWriteTimeUtc -gt $newest) {
            $newest = $file.LastWriteTimeUtc
        }
    }
    return $newest
}

# A running player keeps the built assemblies mapped, a rebuild would fail on those files.
function Test-FileInUse {
    param([string]$Path)

    if ([string]::IsNullOrEmpty($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    try {
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $stream.Dispose()
        return $false
    }
    catch {
        return $true
    }
}

function Sync-Plugin {
    param([string]$SourceRoot, [string]$TargetRoot)

    if (-not (Test-Path -LiteralPath $TargetRoot)) {
        New-Item -ItemType Directory -Path $TargetRoot -Force | Out-Null
    }

    $sourceFiles = Get-PluginFiles -Root $SourceRoot
    $sourceRelative = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

    $copied = 0
    foreach ($file in $sourceFiles) {
        $relative = $file.FullName.Substring($SourceRoot.Length).TrimStart('\', '/')
        [void]$sourceRelative.Add($relative)

        $destination = Join-Path $TargetRoot $relative
        $destinationDirectory = Split-Path -Parent $destination
        if (-not (Test-Path -LiteralPath $destinationDirectory)) {
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        }

        $needsCopy = $true
        if (Test-Path -LiteralPath $destination) {
            $existing = Get-Item -LiteralPath $destination
            if ($existing.Length -eq $file.Length -and $existing.LastWriteTimeUtc -eq $file.LastWriteTimeUtc) {
                $needsCopy = $false
            }
        }

        if ($needsCopy) {
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
            $copied++
        }
    }

    # Remove code files of the plugin that were deleted from the sources, keep
    # the .meta files Unity generated for the remaining ones.
    $removed = 0
    foreach ($file in @(Get-ChildItem -LiteralPath $TargetRoot -Recurse -File)) {
        if ($file.Extension -eq '.meta') {
            continue
        }

        $relative = $file.FullName.Substring($TargetRoot.Length).TrimStart('\', '/')
        if (-not $sourceRelative.Contains($relative)) {
            Remove-Item -LiteralPath $file.FullName -Force
            $removed++
        }
    }

    # Folders and names that only exist in the Unity project (for example after a feature moved
    # into another folder) leave empty folders and orphan .meta files behind. Remove both so the
    # generated folder keeps matching the plugin sources.
    $removedDirectories = 0
    $directories = Get-ChildItem -LiteralPath $TargetRoot -Recurse -Directory |
        Sort-Object -Property FullName -Descending
    foreach ($directory in @($directories)) {
        $relative = $directory.FullName.Substring($TargetRoot.Length).TrimStart('\', '/')
        if ([string]::IsNullOrEmpty($relative)) {
            continue
        }
        if (Test-Path -LiteralPath (Join-Path $SourceRoot $relative)) {
            continue
        }
        if (@(Get-ChildItem -LiteralPath $directory.FullName -Recurse -File).Count -gt 0) {
            continue
        }

        Remove-Item -LiteralPath $directory.FullName -Recurse -Force
        $removedDirectories++
    }

    $removedMeta = 0
    foreach ($meta in @(Get-ChildItem -LiteralPath $TargetRoot -Recurse -File -Filter *.meta)) {
        $assetPath = $meta.FullName.Substring(0, $meta.FullName.Length - $meta.Extension.Length)
        if (Test-Path -LiteralPath $assetPath) {
            continue
        }

        Remove-Item -LiteralPath $meta.FullName -Force
        $removedMeta++
    }

    $report = "$copied file(s) copied, $removed stale file(s) removed"
    if ($removedDirectories -gt 0 -or $removedMeta -gt 0) {
        $report += ", $removedDirectories stale folder(s) and $removedMeta orphan .meta file(s) removed"
    }

    return $report
}

function Get-BuildState {
    param([string]$Signature, [string]$BuiltExePath, [string]$StateDirectory, [string]$SourceRoot, [string]$TargetRoot)

    $stampFile = Join-Path $StateDirectory 'built-signature.txt'
    if (Test-Path -LiteralPath $stampFile) {
        # A stamp file is the reliable record of the last build, unless it was written by a
        # build without the plugin (value "off").
        $stamp = (Get-Content -LiteralPath $stampFile -Raw).Trim()
        if ($stamp -eq $Signature) {
            return 'OK'
        }
        return 'CHANGED'
    }

    if ([string]::IsNullOrEmpty($BuiltExePath) -or -not (Test-Path -LiteralPath $BuiltExePath)) {
        return 'MISSING'
    }

    # A build that was started by hand (Build_MDPro3_Windows64.bat) does not write the
    # stamp file, so treat a player that is newer than every plugin file as up to date.
    $playerTime = (Get-Item -LiteralPath $BuiltExePath).LastWriteTimeUtc
    $sourceTime = Get-NewestWriteTime -Root $SourceRoot
    $targetTime = Get-NewestWriteTime -Root $TargetRoot
    $newestPluginTime = if ($sourceTime -gt $targetTime) { $sourceTime } else { $targetTime }

    if ($newestPluginTime -ne [DateTime]::MinValue -and $playerTime -gt $newestPluginTime) {
        return 'OK'
    }

    return 'CHANGED'
}

# Prints the feature switches of config.json, so --check and the launcher output show what is on.
function Write-ConfigReport {
    param([string]$ConfigPath, [string]$ConfigFileName)

    Write-Output ("PLUGINCONFIG=" + $ConfigPath)

    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        Write-Output ("PLUGINCONFIGMESSAGE=" + $ConfigFileName + " was not found, every feature uses its default (enabled)")
        Write-Output "PLUGINFEATURES=(defaults, all on)"
        return
    }

    try {
        # config.json is UTF-8, Windows PowerShell reads text files as ANSI by default.
        $config = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        Write-Output ("PLUGINCONFIGERROR=" + $ConfigFileName + " cannot be parsed: " + $_.Exception.Message)
        Write-Output "PLUGINFEATURES=(unreadable config, defaults are used)"
        return
    }

    $entries = New-Object System.Collections.Generic.List[string]
    if ($null -ne $config.features) {
        foreach ($entry in @($config.features)) {
            if ($null -eq $entry -or [string]::IsNullOrEmpty($entry.id)) {
                continue
            }

            $stateText = if ($entry.enabled -eq $false) { 'off' } else { 'on' }
            [void]$entries.Add($entry.id + '=' + $stateText)
        }
    }

    if ($entries.Count -eq 0) {
        [void]$entries.Add('no entries, all features on')
    }

    Write-Output ("PLUGINFEATURES=" + ($entries -join ', '))

    $flags = New-Object System.Collections.Generic.List[string]
    foreach ($name in @('logFeatureTicks', 'logEvents')) {
        $property = $config.PSObject.Properties[$name]
        if ($null -ne $property -and $property.Value -eq $true) {
            [void]$flags.Add($name)
        }
    }
    if ($flags.Count -gt 0) {
        Write-Output ("PLUGINCONFIGFLAGS=" + ($flags -join ','))
    }
}

try {
    if ([string]::IsNullOrEmpty($Source)) {
        throw 'the -Source directory is missing'
    }
    if (-not (Test-Path -LiteralPath $Source)) {
        throw "the plugin source directory was not found: $Source"
    }

    switch ($Action) {
        'clean' {
            if (-not [string]::IsNullOrEmpty($Target) -and (Test-Path -LiteralPath $Target)) {
                Remove-Item -LiteralPath $Target -Recurse -Force
            }
            if (-not [string]::IsNullOrEmpty($State)) {
                $stampFile = Join-Path $State 'built-signature.txt'
                if (Test-Path -LiteralPath $stampFile) {
                    Remove-Item -LiteralPath $stampFile -Force
                }
            }
            Write-Result -State 'OFF' -Message 'plugin removed from the Unity project'
            exit 0
        }

        'wait' {
            if ([string]::IsNullOrEmpty($BuiltExe)) {
                throw 'the -BuiltExe path is missing'
            }

            $builtDirectory = Split-Path -Parent $BuiltExe
            $candidates = @(
                $BuiltExe,
                (Join-Path $builtDirectory 'MDPro3_Data\Managed\Assembly-CSharp.dll'),
                (Join-Path $builtDirectory 'MDPro3_Data\Managed\Assembly-CSharp.pdb')
            )

            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            $waited = $false
            while ($true) {
                $blocked = $null
                foreach ($path in $candidates) {
                    if (Test-FileInUse -Path $path) {
                        $blocked = $path
                        break
                    }
                }

                if ([string]::IsNullOrEmpty($blocked)) {
                    if ($waited) {
                        Write-Output "PLUGINMESSAGE=the built files are free again"
                    }
                    Write-Result -State 'OK' -Message 'the built files can be replaced'
                    exit 0
                }

                if ((Get-Date) -ge $deadline) {
                    Write-Result -State 'LOCKED' -Message ("the built files are still in use: " + $blocked)
                    exit 0
                }

                if (-not $waited) {
                    Write-Output "PLUGINMESSAGE=waiting for the running game to release the built files"
                    $waited = $true
                }
                Start-Sleep -Milliseconds 1000
            }
        }

        'stamp' {
            if ([string]::IsNullOrEmpty($State)) {
                throw 'the -State directory is missing'
            }
            if (-not (Test-Path -LiteralPath $State)) {
                New-Item -ItemType Directory -Path $State -Force | Out-Null
            }

            $value = $Signature
            if ([string]::IsNullOrEmpty($value)) {
                $value = Get-PluginSignature -Root $Source
            }

            Set-Content -LiteralPath (Join-Path $State 'built-signature.txt') -Value $value -Encoding ASCII
            Write-Result -State 'OK' -Message "recorded plugin signature $($value.Substring(0, [Math]::Min(12, $value.Length)))"
            exit 0
        }

        default {
            if ([string]::IsNullOrEmpty($Target)) {
                throw 'the -Target directory is missing'
            }
            if ([string]::IsNullOrEmpty($State)) {
                throw 'the -State directory is missing'
            }

            if ($PluginOff) {
                if (Test-Path -LiteralPath $Target) {
                    Remove-Item -LiteralPath $Target -Recurse -Force
                }
                $stampFile = Join-Path $State 'built-signature.txt'
                if (Test-Path -LiteralPath $stampFile) {
                    Remove-Item -LiteralPath $stampFile -Force
                }
                Write-Result -State 'OFF' -Message 'plugins are disabled for this run'
                exit 0
            }

            $signature = Get-PluginSignature -Root $Source
            $copyReport = 'status only'
            if ($Action -eq 'sync') {
                $copyReport = Sync-Plugin -SourceRoot $Source -TargetRoot $Target
            }

            $state = Get-BuildState -Signature $signature -BuiltExePath $BuiltExe -StateDirectory $State `
                -SourceRoot $Source -TargetRoot $Target

            $message = "plugin signature $($signature.Substring(0, 12)) ($copyReport)"
            if ($state -eq 'CHANGED') {
                $message += ' - the built player does not contain the current plugin sources'
            }
            elseif ($state -eq 'MISSING') {
                $message += ' - no built player found'
            }

            # The switches of config.json are shown in every sync/status run.
            Write-ConfigReport -ConfigPath (Join-Path (Split-Path -Parent $Source) 'config.json') `
                -ConfigFileName 'config.json'

            Write-Result -State $state -Message $message
            exit 0
        }
    }
}
catch {
    Write-Result -State 'ERROR' -Message $_.Exception.Message
    exit 1
}
