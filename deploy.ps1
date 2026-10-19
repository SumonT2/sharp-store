<#
.SYNOPSIS
    Builds TechBhai and installs or updates it as a Windows service (default: C:\Store, port 5014).

.DESCRIPTION
    Run from the root of the repository in PowerShell opened "as Administrator":

        .\deploy.ps1

    First run: asks for the database connection, the admin login and the hotline, saves them in
    C:\Store\appsettings.Production.json and creates the Windows service. Later runs only update the app.

    Each run: runs the tests, publishes a Release build, backs up the database (when pg_dump is available),
    stops the service, keeps the current version in C:\Store\app.previous, copies the new version to
    C:\Store\app, starts the service and checks that the site answers. The database is updated by the app
    itself when it starts.

    Layout:
        C:\Store\app                          the published site (replaced on every deploy)
        C:\Store\app.previous                 the version before, for -Rollback
        C:\Store\appsettings.Production.json  passwords and shop settings (never overwritten)
        C:\Store\data\keys                    login cookie keys
        C:\Store\backups                      database backups (the last 10 are kept)

    Needs: the .NET 10 SDK and PostgreSQL. If PowerShell refuses to run scripts:
        Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

.EXAMPLE
    .\deploy.ps1
.EXAMPLE
    .\deploy.ps1 -OpenFirewall          # also allow other computers to reach port 5014
.EXAMPLE
    .\deploy.ps1 -SkipTests -SkipBackup
.EXAMPLE
    .\deploy.ps1 -Rollback              # go back to the previous version
#>
[CmdletBinding()]
param(
    [string]$Target = 'C:\Store',
    [int]$Port = 5014,
    [string]$ServiceName = 'TechBhai',
    # Fill an empty database with the demo catalogue (first run only).
    [switch]$DemoData,
    # Add a Windows Firewall rule for the port, so other computers can open the site.
    [switch]$OpenFirewall,
    [switch]$SkipTests,
    [switch]$SkipBackup,
    # Restore the version in app.previous and restart.
    [switch]$Rollback
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$appDir      = Join-Path $Target 'app'
$previousDir = Join-Path $Target 'app.previous'
$stagingDir  = Join-Path $Target 'staging'
$dataDir     = Join-Path $Target 'data'
$keysDir     = Join-Path $dataDir 'keys'
$backupDir   = Join-Path $Target 'backups'
$settings    = Join-Path $Target 'appsettings.Production.json'
$exe         = Join-Path $appDir 'Shop.Web.exe'
$siteUrl     = "http://localhost:$Port/"

function Step([string]$text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Note([string]$text) { Write-Host "    $text" -ForegroundColor DarkGray }
function Warn([string]$text) { Write-Host "    ! $text" -ForegroundColor Yellow }

# Runs a native program and stops the script when it fails.
function Invoke-Native([string]$file, [string[]]$arguments) {
    & $file @arguments
    if ($LASTEXITCODE -ne 0) { throw "$file failed (exit code $LASTEXITCODE)." }
}

# robocopy: exit codes below 8 mean success.
function Copy-Mirror([string]$from, [string]$to) {
    & robocopy $from $to /MIR /NFL /NDL /NJH /NJS /NP /R:3 /W:2 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying $from to $to failed (robocopy exit code $LASTEXITCODE)." }
    $global:LASTEXITCODE = 0
}

function Read-Plain([string]$prompt, [string]$default) {
    $value = Read-Host "$prompt$(if ($default) { " [$default]" })"
    if ([string]::IsNullOrWhiteSpace($value)) { return $default }
    return $value.Trim()
}

function Read-Secret([string]$prompt, [switch]$NoSemicolon) {
    while ($true) {
        $secure = Read-Host $prompt -AsSecureString
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
        if ($plain.Length -lt 8) { Warn 'Use at least 8 characters.'; continue }
        if ($NoSemicolon -and $plain.Contains(';')) { Warn 'The database password cannot contain ";".'; continue }
        return $plain
    }
}

function Stop-Store {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        Note "Stopping service $ServiceName..."
        Stop-Service -Name $ServiceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
}

function Start-Store {
    Start-Service -Name $ServiceName
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 2
        try {
            $response = Invoke-WebRequest -Uri $siteUrl -UseBasicParsing -TimeoutSec 10
            if ($response.StatusCode -eq 200) { return $true }
        } catch { }
        if ((Get-Service -Name $ServiceName).Status -eq 'Stopped') { break }
    }
    return $false
}

function Show-Failure {
    Warn "The site did not answer on $siteUrl."
    Warn 'Recent errors from the Windows event log:'
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Level = 2; StartTime = (Get-Date).AddMinutes(-5) } -MaxEvents 5 -ErrorAction SilentlyContinue |
        ForEach-Object { Write-Host ("    " + ($_.Message -split "`n" | Select-Object -First 6) -join "`n    ") -ForegroundColor DarkYellow }
    Warn "To see the error directly, run:  `$env:SHOP_SETTINGS='$settings'; & '$exe' --urls http://localhost:$Port"
    Warn 'To go back to the previous version:  .\deploy.ps1 -Rollback'
}

# ------------------------------------------------------------------------------------------------
Step 'Checking prerequisites'
if (-not (Test-Path (Join-Path $PSScriptRoot 'Shop.slnx'))) { throw 'deploy.ps1 must stay in the root of the repository.' }
Set-Location $PSScriptRoot

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Please run PowerShell as Administrator (right-click > Run as administrator). It is needed to manage the Windows service.'
}

# ------------------------------------------------------------------------------------------------
if ($Rollback) {
    if (-not (Test-Path (Join-Path $previousDir 'Shop.Web.exe'))) { throw "There is no previous version in $previousDir." }
    Step "Rolling back to the previous version"
    Stop-Store
    Copy-Mirror $previousDir $appDir
    if (Start-Store) { Write-Host "`nRolled back. The site is running at $siteUrl" -ForegroundColor Green }
    else { Show-Failure; exit 1 }
    Note "The database is not rolled back. Backups are in $backupDir"
    exit 0
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET SDK is not installed: https://dotnet.microsoft.com/download/dotnet/10.0' }
if (-not (& dotnet --list-runtimes | Select-String 'Microsoft.AspNetCore.App 10\.')) { throw 'The ASP.NET Core 10 runtime is missing. Install the .NET 10 SDK or Hosting Bundle.' }
Note "dotnet $(& dotnet --version)"

foreach ($dir in @($Target, $dataDir, $keysDir, $backupDir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

# ------------------------------------------------------------------------------------------------
if (-not (Test-Path $settings)) {
    Step "First run: creating $settings"
    Note 'Database (PostgreSQL). Create the user and database first if you have not, e.g. in psql:'
    Note "  CREATE USER shop WITH PASSWORD 'your-password';  CREATE DATABASE shop OWNER shop;"
    $dbHost = Read-Plain '  Database server' 'localhost'
    $dbPort = Read-Plain '  Database port' '5432'
    $dbName = Read-Plain '  Database name' 'shop'
    $dbUser = Read-Plain '  Database user' 'shop'
    $dbPass = Read-Secret '  Database password' -NoSemicolon
    Note 'Admin login for /admin (created on the first start; you can change the password in the admin later).'
    $adminEmail = Read-Plain '  Admin email' ''
    while ($adminEmail -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') { $adminEmail = Read-Plain '  Admin email (e.g. you@example.com)' '' }
    $adminPass = Read-Secret '  Admin password'
    Note 'Shop details (press Enter to skip; you can edit the file later).'
    $hotline = Read-Plain '  Hotline number' ''
    $address = Read-Plain '  Shop address' ''

    $config = [ordered]@{
        ConnectionStrings = [ordered]@{ Shop = "Host=$dbHost;Port=$dbPort;Database=$dbName;Username=$dbUser;Password=$dbPass" }
        Admin             = [ordered]@{ Email = $adminEmail; Password = $adminPass }
        Seed              = [ordered]@{ DemoData = [bool]$DemoData }
        Shop              = [ordered]@{ Hotline = $hotline; Address = $address }
    }
    $config | ConvertTo-Json -Depth 5 | Set-Content -Path $settings -Encoding UTF8
    Note "Saved. Edit it any time (then restart the service): notepad `"$settings`""
} else {
    Note "Using existing settings: $settings"
}

# ------------------------------------------------------------------------------------------------
if (-not $SkipTests) {
    Step 'Running tests'
    Invoke-Native dotnet @('test', 'Shop.slnx', '-c', 'Release', '--nologo', '-v', 'q')
}

Step 'Publishing a Release build'
if (Test-Path $stagingDir) { Remove-Item -Recurse -Force $stagingDir }
Invoke-Native dotnet @('publish', 'src\Shop.Web\Shop.Web.csproj', '-c', 'Release', '-o', $stagingDir, '--nologo', '-v', 'q')
if (-not (Test-Path (Join-Path $stagingDir 'Shop.Web.exe'))) { throw 'The build did not produce Shop.Web.exe.' }

# ------------------------------------------------------------------------------------------------
if (-not $SkipBackup -and (Test-Path $exe)) {
    Step 'Backing up the database'
    $pgDump = Get-Command pg_dump -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
    if (-not $pgDump) {
        $pgDump = Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\pg_dump.exe' -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -ExpandProperty FullName -First 1
    }
    if ($pgDump) {
        $connection = (Get-Content $settings -Raw | ConvertFrom-Json).ConnectionStrings.Shop
        $parts = @{}
        foreach ($pair in $connection -split ';') {
            $kv = $pair -split '=', 2
            if ($kv.Count -eq 2) { $parts[$kv[0].Trim().ToLowerInvariant()] = $kv[1].Trim() }
        }
        $file = Join-Path $backupDir ("shop-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.dump')
        $env:PGPASSWORD = $parts['password']
        try {
            & $pgDump -h $parts['host'] -p $(if ($parts['port']) { $parts['port'] } else { '5432' }) -U $parts['username'] -d $parts['database'] -F c -f $file
            if ($LASTEXITCODE -ne 0) { throw "pg_dump failed (exit code $LASTEXITCODE). Fix it or run with -SkipBackup." }
        } finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
        Note "Saved $file"
        Get-ChildItem $backupDir -Filter 'shop-*.dump' | Sort-Object Name -Descending | Select-Object -Skip 10 | Remove-Item -Force
    } else {
        Warn 'pg_dump not found (it comes with PostgreSQL), so no backup was made.'
    }
}

# ------------------------------------------------------------------------------------------------
Step 'Installing the new version'
Stop-Store
if (Test-Path $exe) {
    Note "Keeping the current version in $previousDir"
    Copy-Mirror $appDir $previousDir
}
Copy-Mirror $stagingDir $appDir
Remove-Item -Recurse -Force $stagingDir

# ------------------------------------------------------------------------------------------------
Step "Configuring the Windows service '$ServiceName'"
if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
    New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName 'TechBhai store' `
        -Description "TechBhai online store on port $Port ($Target)" -StartupType Automatic | Out-Null
    Note 'Created.'
}
# Run under its own low-privilege account, restart automatically after a crash.
Invoke-Native sc.exe @('config', $ServiceName, 'obj=', "NT SERVICE\$ServiceName", 'start=', 'auto') | Out-Null
Invoke-Native sc.exe @('failure', $ServiceName, 'reset=', '86400', 'actions=', 'restart/5000/restart/10000/restart/60000') | Out-Null
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name Environment -Type MultiString -Value @(
    'ASPNETCORE_ENVIRONMENT=Production',
    "ASPNETCORE_URLS=http://0.0.0.0:$Port",
    "SHOP_SETTINGS=$settings",
    "DataProtection__KeysPath=$keysDir"
)

# Permissions: the service may read the app and its settings and write only to the data folder;
# the settings file (passwords) is readable only by administrators, SYSTEM and the service.
$account = "NT SERVICE\$ServiceName"
Invoke-Native icacls.exe @($Target, '/grant', "${account}:(OI)(CI)RX", '/T', '/C', '/Q') | Out-Null
Invoke-Native icacls.exe @($dataDir, '/grant', "${account}:(OI)(CI)M", '/T', '/C', '/Q') | Out-Null
Invoke-Native icacls.exe @($settings, '/inheritance:r', '/grant:r', '*S-1-5-32-544:F', '*S-1-5-18:F', "${account}:R", '/Q') | Out-Null

if ($OpenFirewall -and -not (Get-NetFirewallRule -DisplayName "TechBhai port $Port" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "TechBhai port $Port" -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
    Note "Firewall: port $Port is open for other computers."
}

# ------------------------------------------------------------------------------------------------
Step 'Starting'
if (Start-Store) {
    Write-Host "`nDone. The store is running at $siteUrl  (admin: ${siteUrl}admin)" -ForegroundColor Green
    if (-not $OpenFirewall) { Note "Other computers: run again with -OpenFirewall, or put IIS/nginx with HTTPS in front." }
} else {
    Show-Failure
    exit 1
}
