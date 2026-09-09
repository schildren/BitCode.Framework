#Requires -Version 5.1
<#
.SYNOPSIS
    BitCode.Framework — Fase 5 (F5-09): libera el candado WORM local aplicado por
    Protect-BackupImmutability.ps1, unicamente despues de que expire la retencion (o con -Force
    explicito y justificacion registrada).

.DESCRIPTION
    Accion administrativa deliberadamente separada, explicita y auditable — no forma parte del
    flujo normal de Retention-Cleanup.sql/Invoke-SqlBackupJob.ps1. Requiere que quien la ejecute
    tenga permiso para modificar la ACL del archivo (WRITE_DAC), que Protect-BackupImmutability.ps1
    NO denego a proposito (ver ese script, seccion de diseno) para que el desbloqueo legitimo sea
    posible sin necesitar `takeown`, pero siga siendo un paso humano separado, nunca automatico.

    Sin -Force, rehusa liberar el candado si `retentionUntilUtc` (metadata .worm.json) todavia no
    paso — un WORM real (Object Lock Compliance/Governance) tampoco permite acortar la retencion
    sin un permiso explicito adicional (Governance) o nunca (Compliance); este script modela el
    caso Governance.

.PARAMETER VaultPath
    Ruta del archivo bloqueado en la boveda (el mismo que devolvio Protect-BackupImmutability.ps1).

.PARAMETER Identity
    Identidad a la que se le habia denegado Delete/WriteData. Por defecto, la identidad actual.

.PARAMETER Force
    Libera el candado aunque la retencion no haya expirado. Debe usarse solo en un procedimiento
    de excepcion documentado (ticket de auditoria) — nunca como parte de un job automatizado.

.PARAMETER Reason
    Obligatorio cuando se usa -Force: motivo auditable de la liberacion anticipada.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $VaultPath,

    [Parameter(Mandatory = $false)]
    [string] $Identity = ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name),

    [Parameter(Mandatory = $false)]
    [switch] $Force,

    [Parameter(Mandatory = $false)]
    [string] $Reason
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $VaultPath -PathType Leaf)) {
    throw "VaultPath no existe: $VaultPath"
}

$metadataPath = "$VaultPath.worm.json"
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
    throw "No se encontro metadata WORM ($metadataPath) — no se puede verificar la retencion. Abortando por seguridad."
}

$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$retentionUntilUtc = [DateTime]::Parse($metadata.retentionUntilUtc, $null, [System.Globalization.DateTimeStyles]::RoundtripKind)

if ([DateTime]::UtcNow -lt $retentionUntilUtc) {
    if (-not $Force) {
        throw "Retencion vigente hasta $($retentionUntilUtc.ToString('o')) (UTC) — no se libera el candado sin -Force y -Reason explicitos (procedimiento de excepcion auditado)."
    }
    if ([string]::IsNullOrWhiteSpace($Reason)) {
        throw "-Force requiere -Reason (motivo auditable de la liberacion anticipada de retencion vigente)."
    }
    Write-Warning "Liberando candado ANTES de que expire la retencion ($($retentionUntilUtc.ToString('o')) UTC). Motivo registrado: $Reason"
}

# Quita la regla DENY especifica agregada por Protect-BackupImmutability.ps1 (no toca el resto de la ACL).
$acl = Get-Acl -LiteralPath $VaultPath
$denyRights = [System.Security.AccessControl.FileSystemRights](
    [System.Security.AccessControl.FileSystemRights]::Delete -bor
    [System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
    [System.Security.AccessControl.FileSystemRights]::WriteData -bor
    [System.Security.AccessControl.FileSystemRights]::AppendData
)
$denyRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    $Identity, $denyRights, [System.Security.AccessControl.AccessControlType]::Deny
)
$acl.RemoveAccessRule($denyRule) | Out-Null
Set-Acl -LiteralPath $VaultPath -AclObject $acl

$item = Get-Item -LiteralPath $VaultPath
$item.IsReadOnly = $false

$unlockRecord = [ordered]@{
    unlockedAtUtc      = [DateTime]::UtcNow.ToString('o')
    unlockedBy         = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    forced             = [bool]$Force
    reason             = $Reason
    originalRetention  = $metadata
}
$unlockRecord | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$VaultPath.unlocked.json" -Encoding utf8

Write-Output "Candado WORM liberado para: $VaultPath"
Write-Output "Registro de desbloqueo: $VaultPath.unlocked.json"
