#Requires -Version 5.1
<#
.SYNOPSIS
    BitCode.Framework — Fase 5 (F5-09): aplica proteccion WORM (Write Once Read Many) real, a
    nivel de sistema de archivos, sobre una COPIA aislada de un backup ya generado por F5-07.

.DESCRIPTION
    Esta demostracion NO reemplaza un WORM real de object storage (Azure Immutable Blob Storage /
    S3 Object Lock modo Compliance) — es una prueba de concepto ejecutable localmente, documentada
    como tal en docs/backups-inmutables-fase5.md seccion 4. El mecanismo real aplicado es:

      1. Aislamiento: el script COPIA el backup desde la carpeta operativa (-BackupRoot de
         Invoke-SqlBackupJob.ps1, sujeta a Retention-Cleanup.sql) hacia una carpeta "boveda"
         separada (-VaultRoot), simulando un destino con identidad/red distinta de la que gestiona
         la base de datos productiva (en produccion: cuenta de almacenamiento/bucket separado con
         permisos minimos, nunca el mismo credential que ejecuta BACKUP DATABASE).
      2. Atributo ReadOnly sobre la copia en la boveda (primera capa, reversible trivialmente por
         si sola — ver nota de diseno).
      3. ACL con regla DENY EXPLICITA (no solo ausencia de ALLOW) para la identidad indicada
         (por defecto, la identidad que ejecuta este proceso) sobre los derechos Delete,
         DeleteSubdirectoriesAndFiles, WriteData y AppendData. Un DENY explicito en Windows tiene
         prioridad sobre cualquier ALLOW heredado o explicito para esa misma identidad — por eso
         es la capa real de proteccion, no el atributo ReadOnly (que se puede quitar con
         `Set-ItemProperty -Name IsReadOnly -Value $false` sin tocar ACLs).
      4. Metadata de retencion (`<archivo>.worm.json`) con la fecha de expiracion del bloqueo,
         para que Unlock-BackupImmutability.ps1 pueda verificar que no se libera un backup antes
         de tiempo.

    Deliberadamente NO se deniega ChangePermissions/TakeOwnership: eso permitiria que
    Unlock-BackupImmutability.ps1 revierta el candado despues de que expire la retencion, mediante
    una accion administrativa explicita y auditable (ver ese script), en vez de dejar el archivo
    bloqueado para siempre. Esto es equivalente al modo "Governance" de S3 Object Lock (reversible
    por una accion privilegiada separada), no al modo "Compliance" (irreversible ni para el root).
    Se documenta esta eleccion explicitamente en docs/backups-inmutables-fase5.md seccion 3.3.

    Requiere Windows PowerShell 5.1+ o PowerShell 7+ (las clases
    System.Security.AccessControl/FileSystemRights son de Windows unicamente; a diferencia de
    Invoke-SqlBackupJob.ps1 (que si exige pwsh 7 por invocar sqlcmd de forma multiplataforma),
    este script y su companero Unlock-BackupImmutability.ps1 son deliberadamente Windows-only,
    consistente con que la ACL de sistema de archivos que aplican solo existe en NTFS/Windows.

.PARAMETER SourcePath
    Ruta del backup ya generado por Invoke-SqlBackupJob.ps1 en la carpeta operativa.

.PARAMETER VaultRoot
    Carpeta raiz de la "boveda" aislada donde se copia y bloquea el backup. Debe ser una ruta
    distinta de -BackupRoot (en produccion, un destino con identidad/permisos distintos).

.PARAMETER RetentionDays
    Dias durante los cuales el backup queda bloqueado contra borrado/sobrescritura.

.PARAMETER Identity
    Identidad de Windows a la que se le deniega Delete/WriteData. Por defecto, la identidad que
    ejecuta este proceso (el escenario minimo exigido: ni el propio proceso que genero el backup
    puede borrarlo).

.OUTPUTS
    Ruta de la copia bloqueada en la boveda.

.EXAMPLE
    ./Protect-BackupImmutability.ps1 -SourcePath 'D:\Backups\AppDb\Full\AppDb_full_20260907.bak' `
        -VaultRoot 'E:\BackupVault\AppDb' -RetentionDays 90
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SourcePath,

    [Parameter(Mandatory = $true)]
    [string] $VaultRoot,

    [Parameter(Mandatory = $true)]
    [int] $RetentionDays,

    [Parameter(Mandatory = $false)]
    [string] $Identity = ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
    throw "SourcePath no existe o no es un archivo: $SourcePath"
}

New-Item -ItemType Directory -Path $VaultRoot -Force | Out-Null

$fileName = Split-Path -Leaf $SourcePath
$vaultPath = Join-Path $VaultRoot $fileName

if (Test-Path -LiteralPath $vaultPath) {
    throw "Ya existe una copia en la boveda para $fileName ($vaultPath) — un WORM real nunca sobrescribe una copia existente; usar un nombre de archivo distinto."
}

Copy-Item -LiteralPath $SourcePath -Destination $vaultPath -ErrorAction Stop

# --- Capa 1: atributo ReadOnly (primera barrera, insuficiente por si sola) ---
$item = Get-Item -LiteralPath $vaultPath
$item.IsReadOnly = $true

# --- Capa 2: ACL con DENY explicito de Delete/WriteData/AppendData para la identidad indicada ---
$acl = Get-Acl -LiteralPath $vaultPath
$denyRights = [System.Security.AccessControl.FileSystemRights](
    [System.Security.AccessControl.FileSystemRights]::Delete -bor
    [System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
    [System.Security.AccessControl.FileSystemRights]::WriteData -bor
    [System.Security.AccessControl.FileSystemRights]::AppendData
)
$denyRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    $Identity, $denyRights, [System.Security.AccessControl.AccessControlType]::Deny
)
$acl.AddAccessRule($denyRule)
Set-Acl -LiteralPath $vaultPath -AclObject $acl

# --- Capa 3: metadata de retencion, para que Unlock-BackupImmutability.ps1 no libere antes de tiempo ---
$lockedAtUtc = [DateTime]::UtcNow
$retentionUntilUtc = $lockedAtUtc.AddDays($RetentionDays)
$metadata = [ordered]@{
    sourcePath        = (Resolve-Path -LiteralPath $SourcePath).Path
    vaultPath         = $vaultPath
    lockedIdentity    = $Identity
    lockedAtUtc       = $lockedAtUtc.ToString('o')
    retentionDays     = $RetentionDays
    retentionUntilUtc = $retentionUntilUtc.ToString('o')
    mechanism         = 'Windows-FS-ACL-Deny+ReadOnly (demo local, ver docs/backups-inmutables-fase5.md — produccion: Azure Immutable Blob Storage / S3 Object Lock Compliance)'
}
$metadataPath = "$vaultPath.worm.json"
$metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8

Write-Output "Backup bloqueado (WORM local) en: $vaultPath"
Write-Output "Retencion hasta (UTC): $($retentionUntilUtc.ToString('o'))"
Write-Output "Metadata: $metadataPath"

return $vaultPath
