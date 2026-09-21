param(
    [switch]$RealModel,
    [switch]$SkipCompile,
    [switch]$VerifyAgentRoute,
    [string]$ComboDeck,
    [string]$ComboHand,
    [int]$Width = 1600,
    [int]$Height = 900,
    [string]$Node = 'node',
    [string]$YgoAiRoot = (Join-Path $PSScriptRoot '../../ygo-ai')
)
$ErrorActionPreference = 'Stop'
if ($VerifyAgentRoute -and $RealModel) { throw 'Choose a local route fixture or the real model, not both.' }
if ($VerifyAgentRoute -and (!$ComboDeck -or !$ComboHand)) { throw 'The route fixture requires ComboDeck and ComboHand.' }
if ($ComboDeck -and !$RealModel -and !$VerifyAgentRoute) { throw 'ComboDeck requires RealModel or VerifyAgentRoute.' }
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo = [IO.Path]::GetFullPath($YgoAiRoot)
$staging = Join-Path $pluginRoot '.selfcheck/story/player/plugins'
if (!(Test-Path -LiteralPath (Join-Path $staging 'config.json'))) { throw 'Run story-stage.ps1 first to prepare the isolated test player.' }
$configPath = Join-Path $staging 'story-ai.local.json'
$prior = if (Test-Path -LiteralPath $configPath) { [IO.File]::ReadAllText($configPath) } else { $null }
$fake = $null
$priorTestDeck = $env:STORY_AI_TEST_DECK
$priorTestHand = $env:STORY_AI_TEST_HAND
try {
    if ($ComboDeck) { $env:STORY_AI_TEST_DECK = (Resolve-Path -LiteralPath $ComboDeck).Path }
    if ($ComboHand) { $env:STORY_AI_TEST_HAND = $ComboHand }
    if ($RealModel) {
        $config = Get-Content -LiteralPath (Join-Path $pluginRoot 'story-ai.local.json') -Raw | ConvertFrom-Json
        if ($config.apiKeyFile) { $config.apiKeyFile = [IO.Path]::GetFullPath((Join-Path $pluginRoot $config.apiKeyFile)) }
    } else {
        $output = Join-Path $pluginRoot '.selfcheck/story/mock-provider.log'
        $errors = Join-Path $pluginRoot '.selfcheck/story/mock-provider-errors.log'
        $provider = if ($VerifyAgentRoute) { 'agent-route-fixture.mjs' } else { 'fake-provider.mjs' }
        $fake = Start-Process -FilePath $Node -ArgumentList ('"' + (Join-Path $repo ('skill/backend/mdpro3/' + $provider)) + '"') -WindowStyle Hidden -PassThru -RedirectStandardOutput $output -RedirectStandardError $errors
        $timer = [Diagnostics.Stopwatch]::StartNew()
        do {
            Start-Sleep -Milliseconds 100
            if ($fake.HasExited -or $timer.Elapsed.TotalSeconds -gt 10) { throw 'Mock provider did not start.' }
            $line = Get-Content -LiteralPath $output -ErrorAction SilentlyContinue | Select-Object -First 1
        } until ($line)
        $port = ($line | ConvertFrom-Json).port
        $config = Get-Content -LiteralPath (Join-Path $pluginRoot 'story-ai.example.json') -Raw | ConvertFrom-Json
        $config.baseUrl = 'http://127.0.0.1:' + $port + '/v1'
        $config.model = 'story-test'
        $config.allowAnonymousLocal = $true
        $config.apiKeyFile = ''
        $config.apiKeyEnv = 'STORY_AI_MOCK_UNUSED_KEY'
        $config.timeoutMs = 2000
    }
    $config.enabled = $true
    $config.ygoAiRoot = $repo
    $config.nodePath = (Get-Command $Node).Source
    [IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    & (Join-Path $PSScriptRoot 'story-runtime-test.ps1') -ReusePlayer -SkipCompile:$SkipCompile -ModelOnly -RealModel:$RealModel -Width $Width -Height $Height
} finally {
    $env:STORY_AI_TEST_DECK = $priorTestDeck
    $env:STORY_AI_TEST_HAND = $priorTestHand
    if ($fake -and !$fake.HasExited) { Stop-Process -Id $fake.Id }
    if ($null -eq $prior) { Remove-Item -LiteralPath $configPath -ErrorAction SilentlyContinue }
    else { [IO.File]::WriteAllText($configPath, $prior, [Text.UTF8Encoding]::new($false)) }
}
