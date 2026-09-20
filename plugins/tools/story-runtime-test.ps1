param(
    [switch]$ReusePlayer,
    [switch]$SkipCompile,
    [switch]$VisualOnly,
    [switch]$OpeningOnly,
    [Alias('SilverOnly')][switch]$FoilOnly,
    [int]$Width = 1600,
    [int]$Height = 900,
    [string]$GameProject = (Join-Path $PSScriptRoot '../../MDPro3'),
    [string]$PlayerRoot,
    [string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.0.24f1/Editor'
)
$ErrorActionPreference = 'Stop'
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$SkipCompile) { & (Join-Path $PSScriptRoot 'story-compile.ps1') -UnityEditor $UnityEditor -GameProject $GameProject }
if (!$ReusePlayer) { & (Join-Path $PSScriptRoot 'story-stage.ps1') }
if (!$PlayerRoot) { $PlayerRoot = Join-Path $pluginRoot '.selfcheck/story/player' }
$playerRoot = [IO.Path]::GetFullPath($PlayerRoot)
if (!$playerRoot.Replace('\', '/').EndsWith('/plugins/.selfcheck/story/player')) {
    throw 'PlayerRoot must be the isolated plugins/.selfcheck/story/player directory.'
}
$managed = Join-Path $playerRoot 'MDPro3_Data/Managed'
$iconRelative = 'MDPro3Plugins/Resources/MDPro3Plugins/StoryMode/Icon_Rarity_SR.png'
$iconTarget = Join-Path (Join-Path $playerRoot 'plugins') $iconRelative
New-Item -ItemType Directory -Path (Split-Path -Parent $iconTarget) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $pluginRoot $iconRelative) -Destination $iconTarget
# Normal Unity builds remove this compiler-only module attribute during linking. Match that
# operation when loading our unlinked compile into the existing, stripped test player framework.
[Reflection.Assembly]::LoadFile((Join-Path $UnityEditor 'Data/Managed/Unity.Cecil.dll')) | Out-Null
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($managed)
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $pluginRoot '.selfcheck/story/Assembly-CSharp.story.dll'), $parameters)
try {
    for ($i = $assembly.MainModule.CustomAttributes.Count - 1; $i -ge 0; $i--) {
        if ($assembly.MainModule.CustomAttributes[$i].AttributeType.FullName -eq 'System.Security.UnverifiableCodeAttribute') {
            $assembly.MainModule.CustomAttributes.RemoveAt($i)
        }
    }
    $assembly.Write((Join-Path $managed 'Assembly-CSharp.dll'))
} finally { $assembly.Dispose() }
$log = Join-Path $playerRoot 'story-runtime.log'
$launchArgs = @(
    '-screen-fullscreen', '0', '-screen-width', $Width, '-screen-height', $Height, '-mdpro3-story-selftest', '-logFile', $log
)
if ($VisualOnly) { $launchArgs += '-story-visual-only' }
if ($OpeningOnly) { $launchArgs += '-story-opening-only' }
if ($FoilOnly) { $launchArgs += '-story-foil-only' }
$process = Start-Process -FilePath (Join-Path $playerRoot 'MDPro3.exe') -WorkingDirectory $playerRoot -WindowStyle Hidden -PassThru -ArgumentList $launchArgs
Write-Output ('Isolated test process: ' + $process.Id)
if (!$process.WaitForExit(270000)) {
    Stop-Process -Id $process.Id
    throw 'Isolated story test timed out; see story-runtime.log.'
}
$result = Get-Content -LiteralPath $log | Select-String 'StoryMode runtime test:'
$result | Write-Output
if (!$result -or !($result | Where-Object { $_.Line -match 'StoryMode runtime test: PASS ' })) {
    throw 'Isolated story test failed; see story-runtime.log.'
}
