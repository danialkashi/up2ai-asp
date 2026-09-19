# Start the app, wait for it, run the e2e script, then clean up.
param()

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $projectDir\..\

Write-Host "Starting app (dotnet run)..."
$start = Start-Process -FilePath dotnet -ArgumentList 'run' -WorkingDirectory (Get-Location) -PassThru -NoNewWindow

try {
    Start-Sleep -Seconds 2
    Write-Host "Running E2E..."
    python .\scripts\e2e.py
    $exit = $LASTEXITCODE
    if ($exit -eq 0) { Write-Host "E2E OK" } else { Write-Host "E2E failed: exit $exit" }
}
finally {
    Write-Host "Stopping app..."
    Stop-Process -Id $start.Id -Force -ErrorAction SilentlyContinue
    Pop-Location
}
