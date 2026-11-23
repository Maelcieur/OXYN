# Installe (ou met à jour) la tâche planifiée qui fournit la température CPU à OXYN.
# Lancé par l'installateur avec élévation (une demande UAC) : une tâche « avec les privilèges les plus
# élevés » ne peut être créée que par un administrateur. Le script pont est copié dans ProgramData,
# lisible par tous les comptes, où OXYN (non élevé) lit le résultat.
param(
    # Compte qui ouvre la session (DOMAINE\nom), transmis par l'installateur non élevé : une fois élevé,
    # le script pourrait tourner sous un autre compte administrateur.
    [Parameter(Mandatory)][string]$User,
    [Parameter(Mandatory)][string]$Source
)

$ErrorActionPreference = 'Stop'
$taskName = 'OXYN-CpuTempBridge'
$dir = 'C:\ProgramData\Oxyn'
$script = Join-Path $dir 'CpuTempBridge.ps1'

New-Item -ItemType Directory -Force -Path $dir | Out-Null
# Le script y est exécuté avec les privilèges élevés : dossier réservé aux administrateurs (un compte standard qui
# l'aurait créé avant l'installation ne doit plus pouvoir remplacer le script). Les utilisateurs gardent la lecture ;
# le compte de la tâche peut écrire (s'il n'est pas administrateur, la tâche tourne sans élévation : rien à gagner).
& icacls.exe $dir /setowner '*S-1-5-32-544' /T /C /Q | Out-Null
& icacls.exe $dir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' "${User}:(OI)(CI)M" /Q | Out-Null
& icacls.exe "$dir\*" /reset /T /C /Q 2>$null | Out-Null

# Ancienne tâche d'avant le renommage (SysDeck) : arrêtée et supprimée avec son dossier, remplacée par celle-ci.
$legacyTask = 'SysDeck-CpuTempBridge'
$legacyDir = 'C:\ProgramData\SysDeck'
if (Get-ScheduledTask -TaskName $legacyTask -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $legacyTask -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $legacyTask -Confirm:$false -ErrorAction SilentlyContinue
}
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -like "*$legacyDir\CpuTempBridge.ps1*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Remove-Item -Path $legacyDir -Recurse -Force -ErrorAction SilentlyContinue

# Arrête l'ancienne boucle avant de remplacer la tâche : sinon elle reste en vie à côté de la nouvelle
# et les deux se disputent les fichiers de température.
Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
    # Filtre précis : le processus qui exécute ce script reçoit aussi « CpuTempBridge.ps1 » en paramètre.
    Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -like "*-File `"$script`"*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

Copy-Item -Path $Source -Destination $script -Force

$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`""
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $User
$principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel Highest
# Le script tourne en boucle : pas de limite de durée, une seule instance, y compris sur batterie.
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -Hidden

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal `
    -Settings $settings -Force | Out-Null

# Démarrage immédiat : la température est disponible sans attendre la prochaine ouverture de session.
Start-ScheduledTask -TaskName $taskName
