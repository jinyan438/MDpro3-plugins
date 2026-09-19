param(
    [string]$GameProject = (Join-Path $PSScriptRoot '../../MDPro3'),
    [string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.0.24f1/Editor'
)
$ErrorActionPreference = 'Stop'
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameRoot = [IO.Path]::GetFullPath($GameProject)
$outputRoot = Join-Path $pluginRoot '.selfcheck/story'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
# Reuse Unity's real references/defines, compiling all game sources read-only.
# All outputs and replacement plugin source paths belong to plugins.
$original = Get-ChildItem -LiteralPath (Join-Path $gameRoot 'Library/Bee/artifacts') -Filter 'Assembly-CSharp.rsp' -Recurse |
    Where-Object { $_.Directory.Name -match 'P\.dag$' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (!$original) { throw 'Build the base game once to generate its compiler response file.' }
$lines = [Collections.Generic.List[string]]::new()
foreach ($line in [IO.File]::ReadAllLines($original.FullName)) {
    if ($line -match '^[-/]out:' -or $line -match '^[-/]refout:' -or $line -match 'Assets[/\\]MDPro3Plugins[/\\]') { continue }
    $lines.Add($line)
}
$lines.Add('-out:"' + (Join-Path $outputRoot 'Assembly-CSharp.dll') + '"')
$lines.Add('-refout:"' + (Join-Path $outputRoot 'Assembly-CSharp.ref.dll') + '"')
Get-ChildItem -LiteralPath (Join-Path $pluginRoot 'MDPro3Plugins/Runtime') -Filter '*.cs' -Recurse |
    ForEach-Object { $lines.Add('"' + $_.FullName + '"') }
$response = Join-Path $outputRoot 'compile.rsp'
[IO.File]::WriteAllLines($response, $lines)
Push-Location $gameRoot
try {
    & (Join-Path $UnityEditor 'Data/NetCoreRuntime/dotnet.exe') (Join-Path $UnityEditor 'Data/DotNetSdkRoslyn/csc.dll') ('@' + $response) 2>&1 |
        Tee-Object -FilePath (Join-Path $outputRoot 'compile.log') | Select-String 'error |StoryMode.*warning'
    if ($LASTEXITCODE -ne 0) { throw 'Story mode compilation failed.' }
} finally { Pop-Location }
Write-Output ('Compiled with real game sources: ' + (Join-Path $outputRoot 'Assembly-CSharp.dll'))
& (Join-Path $PSScriptRoot 'story-weave.ps1') -GameProject $GameProject -UnityEditor $UnityEditor
