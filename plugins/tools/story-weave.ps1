param(
    [string]$GameProject = (Join-Path $PSScriptRoot '../../MDPro3'),
    [string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.0.24f1/Editor'
)
$ErrorActionPreference = 'Stop'
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameRoot = [IO.Path]::GetFullPath($GameProject)
$outputRoot = Join-Path $pluginRoot '.selfcheck/story'
$codegenRoot = Join-Path $outputRoot 'codegen'
New-Item -ItemType Directory -Path $codegenRoot -Force | Out-Null
$monoRoot = Join-Path $UnityEditor 'Data/MonoBleedingEdge'
$framework = Join-Path $monoRoot 'lib/mono/4.5'
$cecil = Join-Path $gameRoot 'Library/PackageCache/com.unity.nuget.mono-cecil/Mono.Cecil.dll'
$common = Join-Path $UnityEditor 'Data/Managed/Unity.CompilationPipeline.Common.dll'
Copy-Item -LiteralPath $cecil, $common -Destination $codegenRoot -Force
$arguments = @('/nologo', '/target:exe', '/langversion:9', '/nostdlib+', ('/out:' + (Join-Path $codegenRoot 'StoryEditorWeaverTests.exe')))
foreach ($assembly in @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'Facades/netstandard.dll')) {
    $arguments += '/reference:' + (Join-Path $framework $assembly)
}
$arguments += '/reference:' + $cecil
$arguments += '/reference:' + $common
$arguments += Join-Path $pluginRoot 'MDPro3Plugins/Editor/StoryMode.CodeGen/StoryEditorPostProcessor.cs'
$arguments += Join-Path $PSScriptRoot 'tests/StoryEditorWeaverTests.cs'
& (Join-Path $UnityEditor 'Data/NetCoreRuntime/dotnet.exe') (Join-Path $UnityEditor 'Data/DotNetSdkRoslyn/csc.dll') @arguments
if ($LASTEXITCODE -ne 0) { throw 'IL postprocessor test compilation failed.' }
$references = [Collections.Generic.List[string]]::new()
foreach ($line in [IO.File]::ReadAllLines((Join-Path $outputRoot 'compile.rsp'))) {
    if ($line -match '^[-/](r|reference):"?([^"\r\n]+)"?$') {
        $reference = $Matches[2]
        if (![IO.Path]::IsPathRooted($reference)) { $reference = Join-Path $gameRoot $reference }
        $references.Add([IO.Path]::GetFullPath($reference))
    }
}
$referenceFile = Join-Path $codegenRoot 'references.txt'
[IO.File]::WriteAllLines($referenceFile, $references)
& (Join-Path $monoRoot 'bin/mono.exe') (Join-Path $codegenRoot 'StoryEditorWeaverTests.exe') (Join-Path $outputRoot 'Assembly-CSharp.dll') (Join-Path $outputRoot 'Assembly-CSharp.story.dll') $referenceFile
if ($LASTEXITCODE -ne 0) { throw 'IL postprocessor verification failed.' }
