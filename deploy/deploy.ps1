# One-click deploy of UP2AI to IIS. Run on the IIS server from a clone of this repo
# (double-click deploy\deploy.bat). Safe to run again for every update.

# ---------------------------------------------------------------- settings
$SiteName    = 'UP2AI'                 # IIS site name
$AppPoolName = 'UP2AI'                 # IIS app pool name
$SitePath    = 'C:\inetpub\up2ai'      # where the published site lives
$Port        = 80                      # HTTP port for the binding (used only when the site is first created)
$HostName    = ''                      # e.g. 'up2ai.ir'; empty = any host name
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

    # ------------------------------------------------------- production config
    # Secrets live only on the server, in appsettings.Production.json; created once on first deploy.
    New-Item -ItemType Directory -Force $SitePath | Out-Null
    $prodConfig = Join-Path $SitePath 'appsettings.Production.json'
    if (-not (Test-Path $prodConfig)) {
        Step 'First deploy: creating appsettings.Production.json'
        $dbHost = Read-Host 'PostgreSQL host [localhost]'; if (-not $dbHost) { $dbHost = 'localhost' }
        $dbPort = Read-Host 'PostgreSQL port [5432]';      if (-not $dbPort) { $dbPort = '5432' }
        $dbName = Read-Host 'Database name [up2ai]';       if (-not $dbName) { $dbName = 'up2ai' }
        $dbUser = Read-Host 'Database user'
        $dbPass = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR((Read-Host 'Database password' -AsSecureString)))
        $adPass = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR((Read-Host 'Admin panel password (user: admin)' -AsSecureString)))
        if ($adPass.Length -lt 8) { Fail 'admin password must be at least 8 characters.' }

        # Same format as AdminAuth.HashPassword: pbkdf2:<iterations>:<salt hex>:<key hex>, SHA-256, 32-byte key.
        $salt = New-Object byte[] 16
        [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($salt)
        $kdf  = New-Object Security.Cryptography.Rfc2898DeriveBytes($adPass, $salt, 210000, [Security.Cryptography.HashAlgorithmName]::SHA256)
        $hex  = { param($b) -join ($b | ForEach-Object { $_.ToString('x2') }) }
        $hash = "pbkdf2:210000:$(& $hex $salt):$(& $hex $kdf.GetBytes(32))"

        $cfg = [ordered]@{
            ConnectionStrings = @{ DefaultConnection = "Host=$dbHost;Port=$dbPort;Database=$dbName;Username=$dbUser;Password=$dbPass" }
            Admin             = @{ InitialPasswordHash = $hash }
        }
        $cfg | ConvertTo-Json -Depth 5 | Set-Content -Path $prodConfig -Encoding UTF8
    }

    # ------------------------------------------------------- IIS site & pool
    Step 'Configuring IIS'
    & $AppCmd list apppool /name:$AppPoolName | Out-Null
    if ($LASTEXITCODE -ne 0) { AppCmd add apppool /name:$AppPoolName /managedRuntimeVersion: /managedPipelineMode:Integrated }
    AppCmd set apppool /apppool.name:$AppPoolName /managedRuntimeVersion: /startMode:AlwaysRunning /processModel.idleTimeout:00:00:00

    & $AppCmd list site /name:$SiteName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        AppCmd add site /name:$SiteName /physicalPath:$SitePath /bindings:"http/*:${Port}:$HostName"
    }
    AppCmd set app "$SiteName/" /applicationPool:$AppPoolName

    # ----------------------------------------------------------------- deploy
    Step 'Stopping site'
    $offline = Join-Path $SitePath 'app_offline.htm'
    Set-Content $offline '<!doctype html><meta charset="utf-8"><title>Updating</title><p style="font-family:sans-serif;text-align:center;margin-top:20vh">Updating, back in a moment.</p>'
    & $AppCmd stop apppool /apppool.name:$AppPoolName | Out-Null   # fails harmlessly if already stopped
    for ($i = 0; $i -lt 30 -and (& $AppCmd list apppool /name:$AppPoolName /text:state) -ne 'Stopped'; $i++) { Start-Sleep 1 }

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
    icacls $prodConfig /inheritance:r /grant:r "IIS AppPool\${AppPoolName}:R" "Administrators:F" "SYSTEM:F" /Q | Out-Null

    Step 'Starting site'
    Remove-Item $offline -Force
    AppCmd start apppool /apppool.name:$AppPoolName
    & $AppCmd start site /site.name:$SiteName | Out-Null   # errors harmlessly if already started
    if ((& $AppCmd list site /name:$SiteName /text:state) -ne 'Started') {
        Fail "IIS site '$SiteName' did not start. Usually another site (e.g. Default Web Site) already uses port ${Port}: set `$HostName or `$Port at the top of deploy.ps1."
    }

    # ------------------------------------------------------------------ check
    Step 'Checking the site responds'
    $url = "http://localhost:$Port/"
    $headers = @{}; if ($HostName) { $headers['Host'] = $HostName }
    $ok = $false
    for ($i = 0; $i -lt 10 -and -not $ok; $i++) {
        try { $r = Invoke-WebRequest $url -Headers $headers -UseBasicParsing -TimeoutSec 30; $ok = $r.StatusCode -eq 200 }
        catch { Start-Sleep 3 }
    }
    if (-not $ok) { Fail "site did not answer 200 at $url. Check Windows Event Viewer > Application for the startup error (often the database connection)." }

    Write-Host "`nDEPLOY SUCCEEDED: $SiteName is running at $url" -ForegroundColor Green
}
catch {
    if ($offline -and (Test-Path $offline)) { Remove-Item $offline -Force -ErrorAction SilentlyContinue }
    Fail $_.Exception.Message
}
