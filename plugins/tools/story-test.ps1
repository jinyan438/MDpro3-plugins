param(
    [string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.0.24f1/Editor',
    [string]$GameBuild = (Join-Path $PSScriptRoot '../../Build/MDPro3')
)
$ErrorActionPreference = 'Stop'
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = Join-Path $pluginRoot '.selfcheck/story/tests'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$monoRoot = Join-Path $UnityEditor 'Data/MonoBleedingEdge'
$framework = Join-Path $monoRoot 'lib/mono/4.5'
$json = Join-Path $GameBuild 'MDPro3_Data/Managed/Newtonsoft.Json.dll'
Copy-Item -LiteralPath $json -Destination $outputRoot
$arguments = @('/nologo', '/target:exe', '/langversion:9', '/nostdlib+', ('/out:' + (Join-Path $outputRoot 'StoryModeTests.exe')))
foreach ($assembly in @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'Facades/netstandard.dll')) {
    $arguments += '/reference:' + (Join-Path $framework $assembly)
}
$arguments += '/reference:' + $json
$arguments += Join-Path $pluginRoot 'MDPro3Plugins/Runtime/Features/StoryMode/StoryProgress.cs'
$arguments += Join-Path $pluginRoot 'MDPro3Plugins/Runtime/Features/StoryMode/StoryDuelPackets.cs'
$arguments += Join-Path $pluginRoot 'MDPro3Plugins/Runtime/Features/StoryMode/StoryModelConfig.cs'
$arguments += Join-Path $PSScriptRoot 'tests/StoryModelConfigTests.cs'
$arguments += Join-Path $PSScriptRoot 'tests/StoryModeTests.cs'
& (Join-Path $UnityEditor 'Data/NetCoreRuntime/dotnet.exe') (Join-Path $UnityEditor 'Data/DotNetSdkRoslyn/csc.dll') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& (Join-Path $monoRoot 'bin/mono.exe') (Join-Path $outputRoot 'StoryModeTests.exe') (Join-Path $outputRoot 'profiles')
if ($LASTEXITCODE -ne 0) { throw 'Story mode tests failed.' }
