#Requires -Version 7.0
<#
.SYNOPSIS
    BitCode.Framework — Fase 5 (F5-07): orquestador de backups automatizados de SQL Server
    (full, differential, log) con limpieza de retencion, segun la politica documentada en
    docs/politica-backups-fase5.md.

.DESCRIPTION
    Ejecuta, via sqlcmd, el script T-SQL correspondiente al tipo de backup indicado
    (Full-Backup.sql / Differential-Backup.sql / Log-Backup.sql / Retention-Cleanup.sql, todos
    en este mismo directorio), sustituyendo las variables sqlcmd necesarias.

    Este script esta pensado para ser invocado por un scheduler EXTERNO a la aplicacion
    (SQL Server Agent Job, Task Scheduler de Windows, o `cron` en Linux invocando `pwsh`) — nunca
    por un job de Quartz dentro del proceso de la propia API, para no acoplar la ejecucion del
    backup a la disponibilidad del mismo motor que se esta protegiendo (ver
    docs/politica-backups-fase5.md seccion 4.1 para el razonamiento completo).

    Organiza los backups en subcarpetas separadas por tipo dentro de -BackupRoot
    (Full/, Differential/, Log/) para que la limpieza de retencion (que opera por extension de
    archivo dentro de una carpeta) nunca mezcle backups de distinto tipo.

.PARAMETER SqlInstance
    Instancia de SQL Server de destino (nombre de host o host\instancia), pasada a `sqlcmd -S`.

.PARAMETER Database
    Nombre de la base de datos a respaldar.

.PARAMETER BackupRoot
    Carpeta raiz donde se organizan los backups (subcarpetas Full/Differential/Log se crean
    debajo). Debe ser accesible con permisos de escritura por la cuenta de servicio de SQL Server.

.PARAMETER BackupType
    Tipo de operacion a ejecutar: Full, Differential, Log o Retention.

.PARAMETER CertificateName
    (F5-09, obligatorio para Full/Differential/Log) Nombre del certificado de servidor creado por
    Enable-BackupEncryption.sql, usado para cifrar el backup en reposo (ver
    docs/backups-inmutables-fase5.md seccion 2). No aplica a -BackupType Retention.

.PARAMETER RetentionDays
    Dias de retencion a aplicar cuando -BackupType es Retention (ver
    docs/politica-backups-fase5.md seccion 3 para los valores recomendados por perfil DR).

.PARAMETER SqlCmdExtraArgs
    Argumentos adicionales para `sqlcmd` (p. ej. autenticacion: "-U","sa","-P","...", o "-E" para
    autenticacion integrada). Por defecto usa autenticacion integrada (-E) mas "-C" (confiar en el
    certificado del servidor, requerido por versiones recientes de sqlcmd con cifrado forzado).

.PARAMETER Immutable
    (F5-09, opcional) Si se indica junto con -VaultRoot y -BackupType Full/Differential/Log, tras
    generar el backup en -BackupRoot se invoca Protect-BackupImmutability.ps1 para copiarlo a una
    boveda aislada (-VaultRoot) y bloquearlo (ACL Deny + ReadOnly) durante -ImmutabilityDays. Ver
    docs/backups-inmutables-fase5.md. No afecta -BackupType Retention (la limpieza de retencion
    sigue operando solo sobre la copia operativa en -BackupRoot, nunca sobre la boveda).

.PARAMETER VaultRoot
    (F5-09) Carpeta raiz de la boveda WORM, distinta de -BackupRoot. Requerido cuando -Immutable.

.PARAMETER ImmutabilityDays
    (F5-09) Dias de bloqueo WORM de la copia en la boveda. Requerido cuando -Immutable.

.EXAMPLE
    # Full semanal (perfil Gold), cifrado (F5-09) con el certificado provisionado por
    # Enable-BackupEncryption.sql, y ademas copiado a boveda WORM (F5-09/F5-07)
    ./Invoke-SqlBackupJob.ps1 -SqlInstance "sql-primary" -Database "AppDb" `
        -BackupRoot "D:\Backups\AppDb" -BackupType Full -CertificateName "AppDbBackupCert" `
        -Immutable -VaultRoot "E:\BackupVault\AppDb" -ImmutabilityDays 90

.EXAMPLE
    # Limpieza de retencion de backups de log (perfil Gold: 7 dias, ver politica seccion 3)
    ./Invoke-SqlBackupJob.ps1 -SqlInstance "sql-primary" -Database "AppDb" `
        -BackupRoot "D:\Backups\AppDb" -BackupType Retention -RetentionDays 7
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SqlInstance,

    [Parameter(Mandatory = $true)]
    [string] $Database,

    [Parameter(Mandatory = $true)]
    [string] $BackupRoot,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Full', 'Differential', 'Log', 'Retention')]
    [string] $BackupType,

    [Parameter(Mandatory = $false)]
    [string] $CertificateName,

    [Parameter(Mandatory = $false)]
    [int] $RetentionDays,

    [Parameter(Mandatory = $false)]
    [string[]] $SqlCmdExtraArgs = @('-E', '-C'),

    [Parameter(Mandatory = $false)]
    [switch] $Immutable,

    [Parameter(Mandatory = $false)]
    [string] $VaultRoot,

    [Parameter(Mandatory = $false)]
    [int] $ImmutabilityDays
)

$ErrorActionPreference = 'Stop'
$scriptRoot = $PSScriptRoot
$timestamp = Get-Date -Format 'yyyyMMddHHmmss'

function Protect-GeneratedBackup {
    param([string] $BackupPath)

    if (-not $Immutable) {
        return
    }
    if ([string]::IsNullOrWhiteSpace($VaultRoot) -or $ImmutabilityDays -le 0) {
        throw '-Immutable requiere -VaultRoot y -ImmutabilityDays (> 0). Ver docs/backups-inmutables-fase5.md.'
    }

    & (Join-Path $scriptRoot 'Protect-BackupImmutability.ps1') `
        -SourcePath $BackupPath -VaultRoot $VaultRoot -RetentionDays $ImmutabilityDays
}

function Invoke-SqlCmdScript {
    param(
        [string] $ScriptPath,
        [hashtable] $Variables
    )

    $sqlCmdVars = @()
    foreach ($key in $Variables.Keys) {
        $sqlCmdVars += '-v'
        $sqlCmdVars += "$key=$($Variables[$key])"
    }

    $arguments = @('-S', $SqlInstance, '-b') + $SqlCmdExtraArgs + $sqlCmdVars + @('-i', $ScriptPath)

    Write-Verbose "sqlcmd $($arguments -join ' ')"
    & sqlcmd @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd fallo (exit code $LASTEXITCODE) ejecutando $ScriptPath"
    }
}

if ($BackupType -in @('Full', 'Differential', 'Log') -and [string]::IsNullOrWhiteSpace($CertificateName)) {
    throw "-CertificateName es obligatorio para -BackupType $BackupType (F5-09: los backups deben cifrarse en reposo, ver docs/backups-inmutables-fase5.md seccion 2 y Enable-BackupEncryption.sql)."
}

switch ($BackupType) {
    'Full' {
        $folder = Join-Path $BackupRoot 'Full'
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        $backupPath = Join-Path $folder "$($Database)_full_$timestamp.bak"
        Invoke-SqlCmdScript -ScriptPath (Join-Path $scriptRoot 'Full-Backup.sql') -Variables @{
            DatabaseName    = $Database
            BackupPath      = $backupPath
            CertificateName = $CertificateName
        }
        Write-Output "Full backup completado: $backupPath"
        Protect-GeneratedBackup -BackupPath $backupPath
    }
    'Differential' {
        $folder = Join-Path $BackupRoot 'Differential'
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        $backupPath = Join-Path $folder "$($Database)_diff_$timestamp.bak"
        Invoke-SqlCmdScript -ScriptPath (Join-Path $scriptRoot 'Differential-Backup.sql') -Variables @{
            DatabaseName    = $Database
            BackupPath      = $backupPath
            CertificateName = $CertificateName
        }
        Write-Output "Differential backup completado: $backupPath"
        Protect-GeneratedBackup -BackupPath $backupPath
    }
    'Log' {
        $folder = Join-Path $BackupRoot 'Log'
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        $backupPath = Join-Path $folder "$($Database)_log_$timestamp.trn"
        Invoke-SqlCmdScript -ScriptPath (Join-Path $scriptRoot 'Log-Backup.sql') -Variables @{
            DatabaseName    = $Database
            BackupPath      = $backupPath
            CertificateName = $CertificateName
        }
        Write-Output "Log backup completado: $backupPath"
        Protect-GeneratedBackup -BackupPath $backupPath
    }
    'Retention' {
        if (-not $PSBoundParameters.ContainsKey('RetentionDays')) {
            throw '-RetentionDays es obligatorio cuando -BackupType es Retention.'
        }

        foreach ($pair in @(
            @{ SubFolder = 'Full'; Extension = 'bak' },
            @{ SubFolder = 'Differential'; Extension = 'bak' },
            @{ SubFolder = 'Log'; Extension = 'trn' }
        )) {
            $folder = Join-Path $BackupRoot $pair.SubFolder
            if (-not (Test-Path $folder)) {
                continue
            }

            Invoke-SqlCmdScript -ScriptPath (Join-Path $scriptRoot 'Retention-Cleanup.sql') -Variables @{
                BackupFolder  = $folder
                Extension     = $pair.Extension
                RetentionDays = $RetentionDays
            }
            Write-Output "Retencion aplicada en $folder (extension .$($pair.Extension), $RetentionDays dias)."
        }
    }
}
