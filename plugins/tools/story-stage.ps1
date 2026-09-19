param([string]$BasePlayer = (Join-Path $PSScriptRoot '../../Build/MDPro3'))
$ErrorActionPreference = 'Stop'
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = Join-Path $pluginRoot '.selfcheck/story/player'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
# Real copies, not links: the test player's caches/config/updates cannot touch the installed game.
foreach ($part in @('MDPro3_Data','MonoBleedingEdge','Data','Deck','Expansions','StandaloneWindows64','Picture','D3D12')) {
    & robocopy (Join-Path $BasePlayer $part) (Join-Path $destination $part) /E /COPY:DAT /R:0 /W:0 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw ('Failed to stage ' + $part) }
}
foreach ($name in @('MDPro3.exe','UnityPlayer.dll','UnityCrashHandler64.exe')) {
    Copy-Item -LiteralPath (Join-Path $BasePlayer $name) -Destination $destination
}
foreach ($name in @('replay','Sound','Video','specials','plugins')) {
    New-Item -ItemType Directory -Path (Join-Path $destination $name) -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $pluginRoot '.selfcheck/story/Assembly-CSharp.dll') -Destination (Join-Path $destination 'MDPro3_Data/Managed/Assembly-CSharp.dll')
Copy-Item -LiteralPath (Join-Path $pluginRoot 'config.json') -Destination (Join-Path $destination 'plugins/config.json')
Copy-Item -LiteralPath (Join-Path $pluginRoot 'story-mode.json') -Destination (Join-Path $destination 'plugins/story-mode.json')
Write-Output ('Isolated story test player: ' + $destination)
