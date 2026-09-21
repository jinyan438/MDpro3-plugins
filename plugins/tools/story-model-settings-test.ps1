param(
    [switch]$SkipCompile,
    [int]$Width = 1600,
    [int]$Height = 900,
    [string]$Node = 'node'
)
$ErrorActionPreference = 'Stop'
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$staging = Join-Path $pluginRoot '.selfcheck/story/player/plugins'
if (!(Test-Path -LiteralPath (Join-Path $staging 'config.json'))) { throw 'Prepare the isolated player with story-stage.ps1 first.' }
$configPath = Join-Path $staging 'story-ai.local.json'
$secretPath = Join-Path $staging 'story-ai.secret.local'
$previousConfig = if (Test-Path -LiteralPath $configPath) { [IO.File]::ReadAllBytes($configPath) } else { $null }
$previousSecret = if (Test-Path -LiteralPath $secretPath) { [IO.File]::ReadAllBytes($secretPath) } else { $null }
$previousUrl = $env:STORY_SETTINGS_TEST_URL
$provider = $null
try {
    $log = Join-Path $pluginRoot '.selfcheck/story/settings-provider.log'
    $errors = Join-Path $pluginRoot '.selfcheck/story/settings-provider-errors.log'
    $provider = Start-Process -FilePath $Node -ArgumentList ('"' + (Join-Path $PSScriptRoot 'tests/story-models-provider.mjs') + '"') -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError $errors
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 100
        if ($provider.HasExited -or $timer.Elapsed.TotalSeconds -gt 10) { throw 'Settings test provider did not start.' }
        $line = Get-Content -LiteralPath $log -ErrorAction SilentlyContinue | Select-Object -First 1
    } until ($line)
    $env:STORY_SETTINGS_TEST_URL = 'http://127.0.0.1:' + ($line | ConvertFrom-Json).port
    $config = Get-Content -LiteralPath (Join-Path $pluginRoot 'story-ai.example.json') -Raw | ConvertFrom-Json
    $config.apiKeyEnv = 'STORY_SETTINGS_TEST_UNUSED_KEY'
    $config.apiKeyFile = ''
    $config.maxModelCalls = 137
    [IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    & (Join-Path $PSScriptRoot 'story-runtime-test.ps1') -ReusePlayer -SkipCompile:$SkipCompile -ModelSettingsOnly -Width $Width -Height $Height
} finally {
    $env:STORY_SETTINGS_TEST_URL = $previousUrl
    if ($provider -and !$provider.HasExited) { Stop-Process -Id $provider.Id }
    if ($null -eq $previousConfig) { Remove-Item -LiteralPath $configPath -ErrorAction SilentlyContinue }
    else { [IO.File]::WriteAllBytes($configPath, $previousConfig) }
    if ($null -eq $previousSecret) { Remove-Item -LiteralPath $secretPath -ErrorAction SilentlyContinue }
    else { [IO.File]::WriteAllBytes($secretPath, $previousSecret) }
}
