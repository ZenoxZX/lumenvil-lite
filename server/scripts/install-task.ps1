<#
.SYNOPSIS
    Registers Lumenvil Lite as a scheduled task that runs on user logon.

.DESCRIPTION
    Builds a self-contained single-file executable from server/ and registers
    a scheduled task that launches it whenever the current user logs on. The
    task runs hidden, restarts itself on failure, and survives logoff/logon
    cycles without requiring an interactive console window.

    Re-running the script overwrites the existing task and refreshes the
    binary in $InstallPath.

.PARAMETER InstallPath
    Where to place the published binary. Defaults to C:\Tools\LumenvilLite.

.PARAMETER TaskName
    Name of the scheduled task. Defaults to "Lumenvil Lite".

.PARAMETER SkipBuild
    Reuse whatever is already in $InstallPath instead of running dotnet
    publish first. Useful when iterating on the task config without
    rebuilding.

.PARAMETER Uninstall
    Remove the scheduled task and delete $InstallPath.

.EXAMPLE
    PS> .\install-task.ps1
    Builds, copies to C:\Tools\LumenvilLite, registers the task, and starts it.

.EXAMPLE
    PS> .\install-task.ps1 -Uninstall
    Stops and removes the task, deletes the install directory.

.NOTES
    Run from an elevated PowerShell (Administrator) so Register-ScheduledTask
    can create the firewall rule and write under C:\Tools.
#>
[CmdletBinding()]
param(
    [string]$InstallPath = 'C:\Tools\LumenvilLite',
    [string]$TaskName    = 'Lumenvil Lite',
    [switch]$SkipBuild,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

function Assert-Admin {
    $current = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($current)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This script must be run from an elevated PowerShell (Run as Administrator).'
    }
}

function Remove-LumenvilTask {
    param([string]$Name)
    $existing = Get-ScheduledTask -TaskName $Name -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "Removing existing scheduled task '$Name'..."
        Unregister-ScheduledTask -TaskName $Name -Confirm:$false
    }
}

function Remove-FirewallRule {
    $rule = Get-NetFirewallRule -DisplayName 'Lumenvil Lite' -ErrorAction SilentlyContinue
    if ($rule) {
        Write-Host "Removing firewall rule 'Lumenvil Lite'..."
        Remove-NetFirewallRule -DisplayName 'Lumenvil Lite' -ErrorAction SilentlyContinue
    }
}

function Ensure-FirewallRule {
    $rule = Get-NetFirewallRule -DisplayName 'Lumenvil Lite' -ErrorAction SilentlyContinue
    if (-not $rule) {
        Write-Host 'Adding firewall rule for TCP 5151...'
        New-NetFirewallRule -DisplayName 'Lumenvil Lite' `
            -Direction Inbound -Protocol TCP -LocalPort 5151 -Action Allow | Out-Null
    }
}

Assert-Admin

# Repo root is two levels up from this script (server/scripts -> server -> repo).
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ServerDir  = Split-Path -Parent $ScriptRoot
$RepoRoot   = Split-Path -Parent $ServerDir

if ($Uninstall) {
    Remove-LumenvilTask -Name $TaskName
    Remove-FirewallRule
    if (Test-Path $InstallPath) {
        Write-Host "Removing $InstallPath..."
        Remove-Item -Recurse -Force $InstallPath
    }
    Write-Host 'Uninstall complete.'
    return
}

# Step 1 — publish a single-file self-contained binary.
$publishDir = Join-Path $ServerDir 'bin\publish'
if (-not $SkipBuild) {
    Write-Host 'Publishing self-contained single-file binary...'
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
    & dotnet publish (Join-Path $ServerDir 'LumenvilLite.csproj') `
        -c Release -r win-x64 -o $publishDir | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
}

$exePath = Join-Path $publishDir 'LumenvilLite.exe'
if (-not (Test-Path $exePath)) {
    throw "Expected published exe not found at $exePath. Run without -SkipBuild."
}

# Step 2 — copy into the install directory.
Write-Host "Copying binary to $InstallPath..."
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
}
Copy-Item -Path (Join-Path $publishDir '*') -Destination $InstallPath -Recurse -Force

$installedExe = Join-Path $InstallPath 'LumenvilLite.exe'

# Step 3 — firewall.
Ensure-FirewallRule

# Step 4 — register the scheduled task.
Remove-LumenvilTask -Name $TaskName

$action = New-ScheduledTaskAction -Execute $installedExe -WorkingDirectory $InstallPath
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit (New-TimeSpan -Hours 0)
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited

Write-Host "Registering scheduled task '$TaskName' (logon trigger for $env:USERNAME)..."
Register-ScheduledTask -TaskName $TaskName `
    -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
    -Description 'Lumenvil Lite — LAN build-status server. See https://github.com/ZenoxZX/lumenvil-lite' | Out-Null

# Step 5 — start it once now so the user does not have to log out/in.
Write-Host 'Starting the task once to verify...'
Start-ScheduledTask -TaskName $TaskName
Start-Sleep -Seconds 2

$state = (Get-ScheduledTask -TaskName $TaskName).State
Write-Host ""
Write-Host "Task state: $state"
Write-Host "Listening on http://0.0.0.0:5151"
Write-Host "Logon trigger: $env:USERNAME"
Write-Host ""
Write-Host "Quick check:  curl http://localhost:5151/health"
Write-Host "To remove:    .\install-task.ps1 -Uninstall"
