# Spawns localsynapse-mcp.exe as a windowless console child, simulating
# Claude Desktop's stdio child process. Used to reproduce the upgrade-blocking
# file lock for both manual testing and CI smoke verification (see
# .github/workflows/upgrade-smoke.yml).

# Background spawner: launches localsynapse-mcp.exe (windowless) and holds it alive
$ErrorActionPreference = 'Continue'
$mcp = "$env:LOCALAPPDATA\Programs\LocalSynapse\localsynapse-mcp.exe"
if (-not (Test-Path $mcp)) {
    Write-Error "MCP not found at $mcp"
    exit 1
}

$si = New-Object System.Diagnostics.ProcessStartInfo
$si.FileName = $mcp
$si.UseShellExecute = $false
$si.RedirectStandardInput = $true
$si.RedirectStandardOutput = $true
$si.RedirectStandardError = $true
$si.CreateNoWindow = $true
$proc = [System.Diagnostics.Process]::Start($si)
Write-Output "MCP_PID=$($proc.Id)"
$proc.Id | Out-File "$env:TEMP\mcp.pid" -Encoding ASCII

# Hold for 180 seconds
$deadline = (Get-Date).AddSeconds(180)
while ((Get-Date) -lt $deadline -and -not $proc.HasExited) {
    Start-Sleep -Seconds 2
}
if (-not $proc.HasExited) {
    Write-Output "Timeout reached, killing MCP PID $($proc.Id)"
    $proc.Kill()
    $proc.WaitForExit(5000)
} else {
    Write-Output "MCP exited on its own with code $($proc.ExitCode)"
}
