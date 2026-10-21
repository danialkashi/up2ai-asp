# One-click deploy of UP2AI to IIS. Run on the IIS server from a clone of this repo
# (double-click deploy\deploy.bat). Safe to run again for every update.

# ---------------------------------------------------------------- settings
$SiteName    = 'up2ai.ir'              # IIS site to deploy to
# If that site already exists, its own folder, app pool, bindings and certificates are used
# as they are. The values below only matter when the script has to create the site.
$AppPoolName = 'up2ai.ir'
$SitePath    = 'C:\inetpub\up2ai.ir'
$Port        = 80
$HostName    = 'up2ai.ir'
# --------------------------------------------------------------------------

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project  = Join-Path $RepoRoot 'src\web.csproj'
$Publish  = Join-Path $env:TEMP 'up2ai-publish'
$AppCmd   = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Fail($msg) { Write-Host "`nDEPLOY FAILED: $msg" -ForegroundColor Red; exit 1 }
function AppCmd { & $AppCmd @args; if ($LASTEXITCODE -ne 0) { Fail "appcmd $($args -join ' ') (exit $LASTEXITCODE)" } }

$DotnetDir = Join-Path $env:ProgramFiles 'dotnet'
function HasHostingBundle {
    # ANCM V2 (the IIS module) plus the ASP.NET Core 10 runtime.
    (Test-Path (Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll')) -and
    @(Get-ChildItem (Join-Path $DotnetDir 'shared\Microsoft.AspNetCore.App') -Filter '10.*' -ErrorAction SilentlyContinue).Count -gt 0
}
function HasSdk {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    @(dotnet --list-sdks | Where-Object { $_ -like '10.*' }).Count -gt 0
}
function Install-FromMicrosoft($name, $url) {
    Step "Installing $name"
    $exe = Join-Path $env:TEMP ([IO.Path]::GetFileName($url))
    try { Invoke-WebRequest $url -OutFile $exe -UseBasicParsing }
    catch { Fail "could not download $name from $url ($($_.Exception.Message)). Check the server's internet access, or install it by hand." }
    $p = Start-Process $exe -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru
    Remove-Item $exe -Force -ErrorAction SilentlyContinue
    if ($p.ExitCode -notin 0, 1641, 3010) { Fail "$name installer failed (exit $($p.ExitCode))." }
    # pick up dotnet in this session without reopening the window
    if ($env:Path -notlike "*$DotnetDir*") { $env:Path = "$DotnetDir;$env:Path" }
}

try {
    # ---------------------------------------------------------- prerequisites
    Step 'Checking prerequisites'
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) { Fail 'run as Administrator (deploy.bat does this for you).' }
    # Anything missing is downloaded from Microsoft and installed silently, then re-checked.
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is very slow with the progress bar on

    if (-not (Test-Path $AppCmd)) {
        Step 'Installing IIS (Windows feature)'
        if (Get-Command Install-WindowsFeature -ErrorAction SilentlyContinue) {
            Install-WindowsFeature Web-Server, Web-Mgmt-Console | Out-Host            # Windows Server
        } else {
            Enable-WindowsOptionalFeature -Online -NoRestart -All `
                -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-ManagementConsole | Out-Host   # Windows 10/11
        }
        if (-not (Test-Path $AppCmd)) { Fail 'IIS could not be installed automatically. Install IIS from Server Manager, then run again.' }
    }

    if (-not (HasHostingBundle)) {
        Install-FromMicrosoft '.NET 10 Hosting Bundle' 'https://aka.ms/dotnet/10.0/dotnet-hosting-win.exe'
        Step 'Restarting IIS so it loads the ASP.NET Core module'
        net stop was /y | Out-Null
        net start w3svc | Out-Null
        if (-not (HasHostingBundle)) { Fail '.NET 10 Hosting Bundle is still missing after install. Restart the server and run again.' }
    }

    if (-not (HasSdk)) {
        Install-FromMicrosoft '.NET 10 SDK' 'https://aka.ms/dotnet/10.0/dotnet-sdk-win-x64.exe'
        if (-not (HasSdk)) { Fail '.NET 10 SDK is still missing after install. Restart the server and run again.' }
    }

    # ------------------------------------------------------------------ build
    # (deploy.bat already pulled the latest code)
    Step 'Publishing (Release)'
    if (Test-Path $Publish) { Remove-Item $Publish -Recurse -Force }
    dotnet publish $Project -c Release -o $Publish --nologo
    if ($LASTEXITCODE -ne 0) { Fail 'dotnet publish failed.' }

    # ------------------------------------------------------- IIS site & pool
    Step 'Configuring IIS'
    & $AppCmd list site /name:$SiteName | Out-Null
    if ($LASTEXITCODE -eq 0) {
        # Existing site: deploy into its folder and reuse its pool; bindings and certificates stay untouched.
        $SitePath    = [Environment]::ExpandEnvironmentVariables((& $AppCmd list vdir "$SiteName/" /text:physicalPath))
        $AppPoolName = & $AppCmd list app "$SiteName/" /text:applicationPool
        Write-Host "Using existing site '$SiteName': $SitePath (app pool '$AppPoolName')"
        AppCmd set apppool /apppool.name:$AppPoolName /managedRuntimeVersion:
    } else {
        Write-Host "Site '$SiteName' not found; creating it at $SitePath"
        New-Item -ItemType Directory -Force $SitePath | Out-Null
        & $AppCmd list apppool /name:$AppPoolName | Out-Null
        if ($LASTEXITCODE -ne 0) { AppCmd add apppool /name:$AppPoolName /managedRuntimeVersion: /managedPipelineMode:Integrated }
        AppCmd set apppool /apppool.name:$AppPoolName /managedRuntimeVersion: /startMode:AlwaysRunning /processModel.idleTimeout:00:00:00
        AppCmd add site /name:$SiteName /physicalPath:$SitePath /bindings:"http/*:${Port}:$HostName"
        AppCmd set app "$SiteName/" /applicationPool:$AppPoolName
    }

    # ------------------------------------------------------- production config
    # Never asks anything: server-only settings live in appsettings.Production.json in the
    # site folder (kept across deploys). Without it, appsettings.json / environment variables apply.
    $prodConfig = Join-Path $SitePath 'appsettings.Production.json'
    if (-not (Test-Path $prodConfig)) {
        Write-Host "Note: $prodConfig not found; using the settings from appsettings.json and environment variables." -ForegroundColor Yellow
    }

    # ----------------------------------------------------------------- deploy
    Step 'Stopping site'
    $offline = Join-Path $SitePath 'app_offline.htm'
    Set-Content $offline '<!doctype html><meta charset="utf-8"><title>Updating</title><p style="font-family:sans-serif;text-align:center;margin-top:20vh">Updating, back in a moment.</p>'
    & $AppCmd stop apppool /apppool.name:$AppPoolName | Out-Null   # fails harmlessly if already stopped
    for ($i = 0; $i -lt 30 -and (& $AppCmd list apppool /name:$AppPoolName /text:state) -ne 'Stopped'; $i++) { Start-Sleep 1 }

    Step "Backing up current site to $SitePath-backup"
    # Only the latest backup is kept; copy it back over $SitePath to roll back.
    robocopy $SitePath "$SitePath-backup" /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 /XF app_offline.htm | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "backup failed (robocopy exit $LASTEXITCODE)." }

    Step 'Copying files'
    # /MIR mirrors the build, but excluded paths are never deleted: uploaded images, logs, server secrets.
    robocopy $Publish $SitePath /MIR /NFL /NDL /NJH /NP /R:3 /W:2 `
        /XD (Join-Path $SitePath 'wwwroot\uploads') (Join-Path $SitePath 'logs') `
        /XF appsettings.Production.json app_offline.htm | Out-Host
    if ($LASTEXITCODE -ge 8) { Fail "robocopy failed (exit $LASTEXITCODE)." }

    $uploads = Join-Path $SitePath 'wwwroot\uploads'
    New-Item -ItemType Directory -Force $uploads | Out-Null
    icacls $SitePath /grant "IIS AppPool\${AppPoolName}:(OI)(CI)RX" /Q | Out-Null
    icacls $uploads  /grant "IIS AppPool\${AppPoolName}:(OI)(CI)M"  /Q | Out-Null
    if (Test-Path $prodConfig) {
        icacls $prodConfig /inheritance:r /grant:r "IIS AppPool\${AppPoolName}:R" "Administrators:F" "SYSTEM:F" /Q | Out-Null
    }

    Step 'Starting site'
    Remove-Item $offline -Force
    AppCmd start apppool /apppool.name:$AppPoolName
    & $AppCmd start site /site.name:$SiteName | Out-Null   # errors harmlessly if already started
    if ((& $AppCmd list site /name:$SiteName /text:state) -ne 'Started') {
        Fail "IIS site '$SiteName' did not start. Usually another site already uses the same port and host name."
    }

    # ------------------------------------------------------------------ check
    Step 'Checking the site responds'
    # Ask the site through its own binding: plain http on localhost when it has one, else its https address.
    $bindings = (& $AppCmd list site /name:$SiteName /text:bindings) -split ','
    $binding  = @($bindings | Where-Object { $_ -like 'http/*' }) + @($bindings | Where-Object { $_ -like 'https/*' }) | Select-Object -First 1
    $proto, $rest = $binding -split '/', 2
    $null, $bPort, $bHost = $rest -split ':'
    $url = if ($proto -eq 'http') { "http://localhost:$bPort/" } else { "https://$(if ($bHost) { $bHost } else { 'localhost' }):$bPort/" }
    [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
    $ok = $false; $code = 0
    for ($i = 0; $i -lt 10 -and -not $ok; $i++) {
        $req = [Net.HttpWebRequest]::Create($url)
        $req.AllowAutoRedirect = $false; $req.Timeout = 30000
        if ($proto -eq 'http' -and $bHost) { $req.Host = $bHost }
        try { $resp = $req.GetResponse(); $code = [int]$resp.StatusCode; $resp.Close() }
        catch [Net.WebException] { if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode } }
        $ok = $code -ge 200 -and $code -lt 400   # a redirect (e.g. to https) also means the app is up
        if (-not $ok) { Start-Sleep 3 }
    }
    if (-not $ok) { Fail "site did not respond at $url (last status $code). Check Windows Event Viewer > Application for the startup error (often the database connection)." }

    Write-Host "`nDEPLOY SUCCEEDED: $SiteName is running at $url" -ForegroundColor Green
}
catch {
    if ($offline -and (Test-Path $offline)) { Remove-Item $offline -Force -ErrorAction SilentlyContinue }
    Fail $_.Exception.Message
}
