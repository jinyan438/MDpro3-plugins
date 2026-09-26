param(
    [switch]$SkipCompile,
    [string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.0.24f1/Editor',
    [string]$GameProject = (Join-Path $PSScriptRoot '../../MDPro3')
)
$ErrorActionPreference = 'Stop'
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$SkipCompile) { & (Join-Path $PSScriptRoot 'story-compile.ps1') -UnityEditor $UnityEditor -GameProject $GameProject }
$outputRoot = Join-Path $pluginRoot '.selfcheck/story/local-ai-tests'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$monoRoot = Join-Path $UnityEditor 'Data/MonoBleedingEdge'
$framework = Join-Path $monoRoot 'lib/mono/4.5'
$gameAssembly = Join-Path $pluginRoot '.selfcheck/story/Assembly-CSharp.story.dll'
Copy-Item -LiteralPath $gameAssembly -Destination (Join-Path $outputRoot 'Assembly-CSharp.dll') -Force
$arguments = @('/nologo', '/target:exe', '/langversion:9', '/nostdlib+', ('/out:' + (Join-Path $outputRoot 'StoryLocalAiTests.exe')))
foreach ($name in @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'Facades/netstandard.dll')) {
    $arguments += '/reference:' + (Join-Path $framework $name)
}
$arguments += '/reference:' + $gameAssembly
$arguments += Join-Path $PSScriptRoot 'tests/StoryLocalAiTests.cs'
$arguments += Join-Path $PSScriptRoot 'tests/StoryLocalAiCards.cs'
$arguments += Join-Path $PSScriptRoot 'tests/StoryLocalAiSafetyTests.cs'
$arguments += Join-Path $PSScriptRoot 'tests/StoryLocalAiDevelopmentTests.cs'
& (Join-Path $UnityEditor 'Data/NetCoreRuntime/dotnet.exe') (Join-Path $UnityEditor 'Data/DotNetSdkRoslyn/csc.dll') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Local AI test compilation failed.' }
& (Join-Path $monoRoot 'bin/mono.exe') (Join-Path $outputRoot 'StoryLocalAiTests.exe') |
    Tee-Object -FilePath (Join-Path $outputRoot 'results.log')
if ($LASTEXITCODE -ne 0) { throw 'Local AI regressions failed.' }
