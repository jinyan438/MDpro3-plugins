param(
    [switch]$SkipCompile,
    [switch]$ReuseAssembly,
    [string]$Label = 'current',
    [string]$Cases = '',
    [ValidateRange(1, 100)][int]$Samples = 12,
    [ValidateRange(0, 100000)][int]$StartSample = 0,
    [ValidateSet('none', 'ash', 'imperm', 'nibiru', 'pressure')][string]$Interruption = 'none',
    [ValidateRange(1, 10)][int]$Turns = 1,
    [string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.0.24f1/Editor',
    [string]$GameProject = (Join-Path $PSScriptRoot '../../MDPro3')
)
$ErrorActionPreference = 'Stop'
if ($Label -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Label must be a simple directory name.' }
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameRoot = [IO.Path]::GetFullPath($GameProject)
if (!$SkipCompile -and !$ReuseAssembly) { & (Join-Path $PSScriptRoot 'story-compile.ps1') -UnityEditor $UnityEditor -GameProject $gameRoot }
$outputRoot = Join-Path $pluginRoot ('.selfcheck/story/core-ai-' + $Label)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$monoRoot = Join-Path $UnityEditor 'Data/MonoBleedingEdge'
$framework = Join-Path $monoRoot 'lib/mono/4.5'
$compiler = Join-Path $UnityEditor 'Data/DotNetSdkRoslyn/csc.dll'
$dotnet = Join-Path $UnityEditor 'Data/NetCoreRuntime/dotnet.exe'
$mono = Join-Path $monoRoot 'bin/mono.exe'
$cecil = Join-Path $gameRoot 'Library/PackageCache/com.unity.nuget.mono-cecil/Mono.Cecil.dll'
Copy-Item -LiteralPath $cecil -Destination $outputRoot -Force
$arguments = @('/nologo', '/target:exe', '/langversion:9', '/nostdlib+', ('/out:' + (Join-Path $outputRoot 'StoryCoreTransport.exe')))
foreach ($name in @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'Facades/netstandard.dll')) { $arguments += '/reference:' + (Join-Path $framework $name) }
$arguments += '/reference:' + $cecil
$arguments += Join-Path $PSScriptRoot 'tests/StoryCoreTransport.cs'
& $dotnet $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Core test transport compilation failed.' }
$assembly = Join-Path $outputRoot 'Assembly-CSharp.dll'
if (!$ReuseAssembly) {
    & $mono (Join-Path $outputRoot 'StoryCoreTransport.exe') (Join-Path $pluginRoot '.selfcheck/story/Assembly-CSharp.story.dll') $assembly (Join-Path $pluginRoot '.selfcheck/story/codegen/references.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Core test transport preparation failed.' }
}
foreach ($name in @('ocgcore.dll', 'sqlite3.dll')) { Copy-Item -LiteralPath (Join-Path $gameRoot ('Assets/Plugins/Windows/' + $name)) -Destination $outputRoot -Force }
Copy-Item -LiteralPath (Join-Path $gameRoot 'Assets/Plugins/Managed/Mono.Data.Sqlite.dll') -Destination $outputRoot -Force
foreach ($reference in Get-Content -LiteralPath (Join-Path $pluginRoot '.selfcheck/story/codegen/references.txt')) {
    if ([IO.Path]::GetFileName($reference) -in @('Newtonsoft.Json.dll', 'UnityEngine.CoreModule.dll')) {
        Copy-Item -LiteralPath $reference -Destination $outputRoot -Force
    }
}
Copy-Item -LiteralPath (Join-Path $gameRoot 'Data/locales/zh-CN/cards.cdb') -Destination $outputRoot -Force
$arguments = @('/nologo', '/target:exe', '/langversion:9', '/unsafe+', '/nostdlib+', ('/out:' + (Join-Path $outputRoot 'StoryCoreAiTests.exe')))
foreach ($name in @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Data.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'Facades/netstandard.dll')) { $arguments += '/reference:' + (Join-Path $framework $name) }
$arguments += '/reference:' + $assembly
$arguments += Join-Path $PSScriptRoot 'tests/StoryCoreAiTests.cs'
& $dotnet $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Core AI test compilation failed.' }
if (!$Cases) {
    $Cases = Join-Path $outputRoot 'cases.tsv'
    & python (Join-Path $PSScriptRoot 'tests/story_core_cases.py') --output $Cases --samples $Samples --start $StartSample
    if ($LASTEXITCODE -ne 0) { throw 'Could not freeze the level-10 story deck openings.' }
}
if (!(Test-Path -LiteralPath $Cases -PathType Leaf)) { throw ('Missing frozen core cases: ' + $Cases) }
$storedCases = Join-Path $outputRoot 'cases.tsv'
if ([IO.Path]::GetFullPath($Cases) -ne $storedCases) { Copy-Item -LiteralPath $Cases -Destination $storedCases -Force }
$Cases = $storedCases
$manifest = [ordered]@{ interruption = $Interruption; turns = $Turns; startedUtc = [DateTime]::UtcNow.ToString('o'); hashes = [ordered]@{} }
foreach ($entry in @(
    @('cases', $Cases), @('assembly', $assembly), @('core', (Join-Path $outputRoot 'ocgcore.dll')),
    @('cards', (Join-Path $outputRoot 'cards.cdb')), @('scripts', (Join-Path $gameRoot 'Data/script.zip')),
    @('harness', (Join-Path $PSScriptRoot 'tests/StoryCoreAiTests.cs'))
)) { $manifest.hashes[$entry[0]] = (Get-FileHash -LiteralPath $entry[1] -Algorithm SHA256).Hash }
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'manifest.json') -Encoding utf8
$runtimeLine = & $dotnet --list-runtimes | Select-Object -First 1
$runtimeVersion = ($runtimeLine -split ' ')[1]
@{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $runtimeVersion } } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'StoryCoreAiTests.runtimeconfig.json') -Encoding utf8
& $dotnet (Join-Path $outputRoot 'StoryCoreAiTests.exe') $outputRoot (Join-Path $gameRoot 'Data/script.zip') $Cases $Interruption $Turns 2>&1 | Tee-Object -FilePath (Join-Path $outputRoot 'run.log')
if ($LASTEXITCODE -ne 0) { throw 'Core AI tests failed.' }
