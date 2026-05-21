#!/usr/bin/env pwsh
# gate-check.ps1 — Must run before reporting any task as complete
# Usage: ./gate-check.ps1

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "`n========== Gate 1: Build ==========" -ForegroundColor Cyan
dotnet build "$root/LocalSynapse.v2.sln" -v q 2>&1 | Out-Null

if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED: Gate 1 - Build errors detected" -ForegroundColor Red
    dotnet build "$root/LocalSynapse.v2.sln" -v q 2>&1 | Select-String ": error "
    exit 1
}
Write-Host "PASSED: Gate 1 - Build succeeded (0 errors)" -ForegroundColor Green

Write-Host "`n========== Gate 2: Tests ==========" -ForegroundColor Cyan
$testOutput = dotnet test "$root/LocalSynapse.v2.sln" --no-build -v q 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED: Gate 2 - Test failures detected" -ForegroundColor Red
    $testOutput | Select-String "Failed|Error" | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 2
}

$passedLine = $testOutput | Select-String "Passed!" | Select-Object -Last 1
Write-Host "PASSED: Gate 2 - $passedLine" -ForegroundColor Green

Write-Host "`n========== Gate 3: Impact Scope (auto) ==========" -ForegroundColor Cyan

$workingTreeFiles = git diff --name-only HEAD

$hasOriginMain = $false
try {
    git fetch origin main --quiet 2>$null
    if ($LASTEXITCODE -eq 0) { $hasOriginMain = $true }
} catch { }

$commitFiles = @()
if ($hasOriginMain) {
    $commitFiles = git diff --name-only origin/main..HEAD 2>$null
} else {
    Write-Host "  (origin/main not fetchable; working tree only)" -ForegroundColor DarkGray
}

$allFiles = @($workingTreeFiles) + @($commitFiles) | Where-Object { $_ } | Sort-Object -Unique

$agentMap = [ordered]@{
    'src/LocalSynapse.Core/'                = 'Core'
    'src/LocalSynapse.Pipeline/'            = 'Pipeline'
    'src/LocalSynapse.Search/'              = 'Search'
    'src/LocalSynapse.Email/'               = 'Email'
    'src/LocalSynapse.Mcp.Stdio/'           = 'Mcp.Stdio'
    'src/LocalSynapse.Mcp/'                 = 'Mcp'
    'src/LocalSynapse.UI/'                  = 'UI'
    'tests/LocalSynapse.Core.Tests/'        = 'Core (test)'
    'tests/LocalSynapse.Pipeline.Tests/'    = 'Pipeline (test)'
    'tests/LocalSynapse.Search.Tests/'      = 'Search (test)'
    'tests/LocalSynapse.UI.Tests/'          = 'UI (test)'
    'tests/LocalSynapse.UI.Headless.Tests/' = 'UI (headless test)'
    'tests/LocalSynapse.Mcp.Tests/'         = 'Mcp (test)'
    'tests/LocalSynapse.Email.Tests/'       = 'Email (test)'
    'tests/LocalSynapse.Integration.Tests/' = 'Integration'
}

$agentsTouched = [ordered]@{}
foreach ($f in $allFiles) {
    $matched = $false
    foreach ($prefix in $agentMap.Keys) {
        if ($f -like "$prefix*") {
            $agent = $agentMap[$prefix]
            if (-not $agentsTouched.Contains($agent)) { $agentsTouched[$agent] = @() }
            $agentsTouched[$agent] += $f
            $matched = $true
            break
        }
    }
    if (-not $matched -and -not $f.StartsWith('docs/plans/')) {
        if (-not $agentsTouched.Contains('(other)')) { $agentsTouched['(other)'] = @() }
        $agentsTouched['(other)'] += $f
    }
}

if ($agentsTouched.Count -eq 0) {
    Write-Host "No tracked changes." -ForegroundColor Yellow
} else {
    foreach ($agent in $agentsTouched.Keys) {
        Write-Host "  [$agent] $($agentsTouched[$agent].Count) file(s)" -ForegroundColor Yellow
        foreach ($file in $agentsTouched[$agent]) {
            Write-Host "    $file" -ForegroundColor DarkGray
        }
    }
    $codeAgents = @($agentsTouched.Keys | Where-Object { $_ -notmatch '\(test\)|\(headless test\)|Integration|\(other\)' })
    if ($codeAgents.Count -gt 1) {
        Write-Host "  WARN: Cross-Agent code change: $($codeAgents -join ', '). Review carefully." -ForegroundColor Yellow
    }
}

Write-Host "`n========== Gate 4: Golden + Contract + Headless ==========" -ForegroundColor Cyan

$gate4Categories = @('GoldenMaster', 'Contract', 'Headless')
$gate4Failed = $false
foreach ($cat in $gate4Categories) {
    Write-Host "  Running Category=$cat ..." -ForegroundColor DarkCyan
    $catOutput = dotnet test "$root/LocalSynapse.v2.sln" --no-build -v q --filter "Category=$cat" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  FAILED: Gate 4 [$cat]" -ForegroundColor Red
        $catOutput | Select-String "Failed|Error" | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        $gate4Failed = $true
    } else {
        $catPassed = $catOutput | Select-String "Passed!" | Select-Object -Last 1
        Write-Host "  PASSED: Gate 4 [$cat] - $catPassed" -ForegroundColor Green
    }
}

if ($gate4Failed) {
    Write-Host "`nReproduce with: dotnet test --filter `"FullyQualifiedName~<TestName>`" --logger `"console;verbosity=detailed`"" -ForegroundColor Yellow
    exit 4
}

Write-Host "`n========== All Gates Passed ==========" -ForegroundColor Green
exit 0
