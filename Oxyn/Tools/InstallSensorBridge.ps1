# Installe (ou met à jour) le pont des capteurs d'OXYN. Lancé par l'installateur avec élévation (une seule demande UAC) :
#   1. installe le pilote PawnIO (installateur officiel, silencieux), nécessaire pour lire processeur, carte mère et mémoire ;
#   2. supprime l'ancien pont de température (tâche OXYN-CpuTempBridge / SysDeck-CpuTempBridge, script PowerShell) ;
#   3. copie le pont (Oxyn.SensorBridge.exe) dans C:\ProgramData\Oxyn\SensorBridge, dossier réservé aux administrateurs ;
#   4. crée la tâche planifiée « OXYN-SensorBridge » (à l'ouverture de session, privilèges les plus élevés) et la démarre.
param(
    # Compte qui ouvre la session (DOMAINE\nom), transmis par l'installateur non élevé : une fois élevé,
    # le script pourrait tourner sous un autre compte administrateur.
    [Parameter(Mandatory)][string]$User,
    # Dossier contenant Oxyn.SensorBridge.exe et ses bibliothèques.
    [Parameter(Mandatory)][string]$Source,
    # Installateur officiel de PawnIO (PawnIO_setup.exe).
    [Parameter(Mandatory)][string]$PawnIO
)

$ErrorActionPreference = 'Stop'
$taskName = 'OXYN-SensorBridge'
$dir = 'C:\ProgramData\Oxyn'
$bridgeDir = Join-Path $dir 'SensorBridge'
$exe = Join-Path $bridgeDir 'Oxyn.SensorBridge.exe'
$log = Join-Path $dir 'install-sensors.log'

function Write-InstallLog([string]$message) {
    try { Add-Content -Path $log -Value ('{0:yyyy-MM-dd HH:mm:ss} {1}' -f (Get-Date), $message) -Encoding UTF8 } catch { }
}

New-Item -ItemType Directory -Force -Path $dir | Out-Null

# 1. PawnIO : installé (ou mis à jour) en premier. L'installateur officiel l'inscrit dans Windows, condition pour que
#    LibreHardwareMonitor l'utilise (le pilote seul, sans inscription, donne des températures à 0).
$p = Start-Process -FilePath $PawnIO -ArgumentList '-install', '-silent' -Wait -PassThru
# Registre 64 bits même si ce script tourne dans le PowerShell 32 bits (lancé par l'installateur).
$hklm = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', 'Registry64')
$registered = $null -ne $hklm.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO')
# Code 183 : PawnIO déjà installé dans cette version.
Write-InstallLog "PawnIO : installateur terminé (code $($p.ExitCode)), inscrit : $registered."

# Le dossier contient un programme exécuté avec les privilèges élevés : réservé aux administrateurs (un compte standard qui
# l'aurait créé avant l'installation ne doit plus pouvoir le remplacer). Les utilisateurs gardent la lecture ; le compte de
# la tâche peut écrire (s'il n'est pas administrateur, la tâche tourne sans élévation : rien à gagner).
& icacls.exe $dir /setowner '*S-1-5-32-544' /T /C /Q | Out-Null
& icacls.exe $dir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' "${User}:(OI)(CI)M" /Q | Out-Null
& icacls.exe "$dir\*" /reset /T /C /Q 2>$null | Out-Null
# Le pont lui-même : seuls les administrateurs et SYSTEM peuvent le modifier (pas même le compte de la session).
New-Item -ItemType Directory -Force -Path $bridgeDir | Out-Null
& icacls.exe $bridgeDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' /Q | Out-Null

# 2. Ancien pont de température (PowerShell + outil AMD Ryzen Master), y compris celui d'avant le renommage (SysDeck).
foreach ($old in 'OXYN-CpuTempBridge', 'SysDeck-CpuTempBridge') {
    if (Get-ScheduledTask -TaskName $old -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $old -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $old -Confirm:$false -ErrorAction SilentlyContinue
        Write-InstallLog "Ancienne tâche supprimée : $old."
    }
}
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -like '*CpuTempBridge.ps1*' -and $_.CommandLine -notlike '*InstallSensorBridge.ps1*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Remove-Item -Path (Join-Path $dir 'CpuTempBridge.ps1'), (Join-Path $dir 'bridge-test.txt'), (Join-Path $dir 'cputemp-bridge.log'), (Join-Path $dir 'cputemp-bridge.log.old') -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $dir -Filter 'cputemp*.tmp' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Remove-Item -Path 'C:\ProgramData\SysDeck' -Recurse -Force -ErrorAction SilentlyContinue

# 3. Pont précédent arrêté avant de remplacer ses fichiers (sinon ils sont verrouillés).
Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
Get-Process -Name 'Oxyn.SensorBridge' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
& robocopy.exe $Source $bridgeDir /MIR /NJH /NJS /NP /NFL /NDL /XF *.pdb | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copie du pont des capteurs impossible (robocopy $LASTEXITCODE)." }

# 4. Tâche planifiée : à l'ouverture de session, élevée, sans limite de durée, une seule instance, y compris sur batterie.
$action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $bridgeDir
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $User
$principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -Hidden
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal `
    -Settings $settings -Force | Out-Null

# Démarrage immédiat : les capteurs sont disponibles sans attendre la prochaine ouverture de session.
Start-ScheduledTask -TaskName $taskName
Write-InstallLog "Pont des capteurs installé ($exe) et démarré."
