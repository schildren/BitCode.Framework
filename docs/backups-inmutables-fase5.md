# Backups inmutables — Fase 5, Disaster Recovery y multi-región

**Tarea:** F5-09 (Fase 5 — Disaster Recovery y multi-región) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-08/09.
**Depende de:** F5-07 ([`docs/politica-backups-fase5.md`](politica-backups-fase5.md) — jobs de backup full/differential/log versionados) y F5-08 ([`docs/runbook-pitr-fase5.md`](runbook-pitr-fase5.md) — restauración point-in-time sobre la misma cadena).
**Trabajo del backlog:** "Aplicar aislamiento, cifrado y WORM". **Criterio de aceptación:** "Resistencia a borrado probada".

**Alcance:** esta tarea completa las tres brechas dejadas explícitas por F5-07 (`docs/politica-backups-fase5.md` sección 6): aislamiento del repositorio de backups respecto de la identidad que opera la base productiva, cifrado en reposo, y protección WORM (Write Once Read Many) real contra borrado — incluso privilegiado.

---

## 1. Aislamiento

### 1.1 Amenaza que se mitiga

Si un atacante o un error de operación compromete la cuenta de servicio de SQL Server o la carpeta operativa de backups (`-BackupRoot` de `Invoke-SqlBackupJob.ps1`, la misma que gestiona `Retention-Cleanup.sql`), un backup que viva únicamente en esa misma carpeta, con los mismos permisos, es tan vulnerable a borrado como los propios datos de producción — el backup dejaría de ser una defensa independiente contra ransomware, borrado malicioso o error humano con esa cuenta.

### 1.2 Mecanismo implementado

[`tools/SqlBackupAutomation/Protect-BackupImmutability.ps1`](../tools/SqlBackupAutomation/Protect-BackupImmutability.ps1) (entregado junto con F5-07, documentado formalmente aquí) copia cada backup recién generado desde la carpeta operativa (`-BackupRoot`) hacia una **carpeta "bóveda" separada** (`-VaultRoot`), antes de aplicarle el candado WORM (sección 3). La separación de carpetas modela, en este entorno local, la separación real que debe existir en producción:

- **Identidad distinta:** la bóveda debe vivir bajo una cuenta/credencial que la cuenta de servicio de SQL Server (o la identidad de la aplicación) **no pueda administrar** — en Windows/NTFS, una identidad distinta con ACL propia (lo que demuestra `Protect-BackupImmutability.ps1`, sección 3); en la nube, una cuenta de almacenamiento o proyecto/cuenta de nube completamente separados de la suscripción que aloja la base de datos productiva, con credenciales de acceso propias que ni el equipo de operación de base de datos gestiona por defecto.
- **Red/canal distinto (recomendado, no implementado en este repositorio):** en producción, la bóveda debería además ser inalcanzable por la misma ruta de red que un atacante que ya comprometió el servidor de base de datos usaría — p. ej. un endpoint privado o un bucket con políticas de red restrictivas, nunca el mismo recurso compartido (`\\backup-share\...`) que ya es de lectura/escritura para la cuenta de SQL Server.

### 1.3 Qué NO se declara resuelto aquí (honestidad de alcance)

Este repositorio no dispone de una cuenta de almacenamiento cloud real, un segundo dominio de Windows con una identidad separada aprovisionada, ni una red segmentada — desplegar eso requeriría una decisión de infraestructura productiva (sección 13 del Plan Maestro: "nueva base de datos o broker" no aplica aquí, pero sí implica aprovisionar cuentas/roles en un proveedor cloud, que si es nuevo requeriría aprobación humana). Lo que se entrega es:

1. El mecanismo de software real (`Protect-BackupImmutability.ps1`/`Unlock-BackupImmutability.ps1`) que aplica la separación de carpeta + identidad + candado, ejecutable y probado en esta máquina.
2. Este runbook documentando exactamente qué cambia al llevarlo a un proveedor cloud real (sección 4).

---

## 2. Cifrado en reposo

### 2.1 Mecanismo: cifrado nativo de SQL Server Backup Encryption

`Full-Backup.sql`, `Differential-Backup.sql` y `Log-Backup.sql` (`tools/SqlBackupAutomation/`) ahora incluyen, en el propio `BACKUP DATABASE`/`BACKUP LOG`, la cláusula:

```sql
ENCRYPTION (ALGORITHM = AES_256, SERVER CERTIFICATE = [$(CertificateName)])
```

`$(CertificateName)` es ahora una variable **obligatoria** (antes solo existían `DatabaseName`/`BackupPath`) — `Invoke-SqlBackupJob.ps1` rechaza con una excepción explícita cualquier invocación de `-BackupType Full/Differential/Log` sin `-CertificateName`, para que un backup sin cifrar nunca pueda generarse por accidente a través del orquestador versionado.

Se eligió **AES_256** (no TDES, la otra opción soportada) por ser el algoritmo recomendado por Microsoft para nuevas implementaciones (TDES está deprecado desde SQL Server 2016). Se eligió cifrado nativo de `BACKUP ... WITH ENCRYPTION` (no TDE de base de datos completa, ni cifrado de archivo a nivel de sistema operativo tipo BitLocker/EFS) porque:

- No requiere cambiar el modelo de almacenamiento de la base de datos productiva (TDE cifra los archivos `.mdf`/`.ldf` en todo momento, una decisión de mayor alcance que excede F5-09).
- El cifrado queda embebido en el propio archivo de backup — es portable: un `.bak` cifrado sigue estando protegido incluso si se copia a un medio sin cifrado de disco (a diferencia de BitLocker/EFS, que protege el volumen, no el archivo en tránsito).
- Es verificable de forma nativa por SQL Server (`msdb.dbo.backupset.encryptor_type`, ver sección 2.3) sin depender de herramientas externas de cifrado de archivos.

### 2.2 Gestión de la clave — provisión y DR entre servidores

Tres scripts nuevos, todos en `tools/SqlBackupAutomation/`:

| Script | Rol |
|---|---|
| `Enable-BackupEncryption.sql` | Ejecutar UNA VEZ por servidor: crea la Database Master Key de `master` (si no existe) y el certificado de servidor dedicado a backups (idempotente). |
| `Export-BackupEncryptionCertificate.sql` | `BACKUP CERTIFICATE ... WITH PRIVATE KEY (...)` — exporta el certificado y su clave privada (cifrada con contraseña) a dos archivos (`.cer`/`.pvk`), para transferir a otro servidor. |
| `Import-BackupEncryptionCertificate.sql` | En el servidor DESTINO: crea su propia Master Key (si no existe) y usa `CREATE CERTIFICATE ... FROM FILE ... WITH PRIVATE KEY (...)` para importar el certificado exportado. |

**Por qué esto es indispensable, no opcional:** un backup cifrado con `SERVER CERTIFICATE` solo puede restaurarse en un servidor que tenga ese certificado (con su clave privada) instalado — SQL Server rechaza el `RESTORE` en cualquier otro servidor. Esto es la propiedad de seguridad real del cifrado (sin ella, "cifrado" sería cosmético), pero implica que el runbook de DR entre regiones (`docs/runbook-pitr-fase5.md`) y la topología de replicación (`docs/replicacion-sql-fase5.md`) deben incorporar la exportación/importación del certificado como un paso de aprovisionamiento previo al primer restore en cada servidor secundario — se documenta aquí explícitamente para que no quede como una sorpresa operativa durante un incidente real.

**Gestión de contraseñas:** `MasterKeyPassword` y `PrivateKeyPassword` deben provenir de un secret store real (ver [`docs/guia-secret-provider.md`](guia-secret-provider.md)) en cualquier uso productivo — ningún script de `tools/SqlBackupAutomation/` fija una contraseña por defecto; siempre se pasan por variable `sqlcmd -v`, igual que el resto de variables sensibles de la política de F5-07.

### 2.3 Verificación real — evidencia de ejecución

[`tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlBackupRestoreIntegrationTests.cs`](../tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlBackupRestoreIntegrationTests.cs) (extendido en esta tarea, sobre la base de F5-07) ejecuta, contra dos contenedores Testcontainers SQL Server 2022 reales, el flujo completo:

1. `Enable-BackupEncryption.sql` en el origen.
2. `Full-Backup.sql` → `Differential-Backup.sql` → `Log-Backup.sql`, los tres con `CertificateName` real.
3. **Evidencia de cifrado real** (no una suposición sobre el texto del script enviado): consulta `msdb.dbo.backupset.encryptor_type` en el origen y confirma que los tres backups quedan marcados por el propio motor de SQL Server como `'CERTIFICATE'` — es SQL Server, no el test, quien certifica que el backup está cifrado.
4. `Export-BackupEncryptionCertificate.sql` en el origen → copia real de los archivos `.cer`/`.pvk` entre los sistemas de archivos de los dos contenedores → `Import-BackupEncryptionCertificate.sql` en el destino.
5. La cadena de `RESTORE` (full → differential → log) se ejecuta en el destino y reconstruye exactamente los mismos datos que en el origen — el mismo criterio de aceptación de F5-07, ahora sobre backups cifrados.

Además, [`SqlBackupRestoreIntegrationTests.RestoreEncryptedBackup_WithoutCertificate_FailsWithCertificateError`](../tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlBackupRestoreIntegrationTests.cs) aísla la propiedad de seguridad opuesta: genera un backup cifrado y lo copia a un servidor destino que **deliberadamente no** recibe el certificado — el `RESTORE` real falla con un error real de SQL Server (`Cannot find server certificate with thumbprint '...'`), confirmando que el cifrado ofrece protección real, no solo un flag cosmético en el header del backup.

**Resultado de ejecución real (2026-09-09, dos/tres contenedores `mcr.microsoft.com/mssql/server:2022-latest` vía Testcontainers):**

```
dotnet test tests/Shared.Infrastructure.Persistence.Tests/Shared.Infrastructure.Persistence.Tests.csproj --filter "FullyQualifiedName~SqlBackupRestoreIntegrationTests"

Correctas: FullDifferentialLogCycle_RestoresExactDataUpToLastLogBackup
  Filas esperadas (origen, hasta el último log backup): 15, checksum D8B6FBD6
  Filas restauradas (destino, tras full+differential+log cifrados): 15, checksum D8B6FBD6
Correctas: RetentionCleanup_DeletesOnlyBackupsOlderThanRetentionWindow
Correctas: RestoreEncryptedBackup_WithoutCertificate_FailsWithCertificateError
  Excepción real: Microsoft.Data.SqlClient.SqlException: Cannot find server certificate with thumbprint '0xC3A35E8FEAEA0C01639CD82240AB7FF2821C72C3'. RESTORE DATABASE is terminating abnormally.

Pruebas totales: 3
     Correcto: 3
```

Se verificó además que `SqlPointInTimeRestoreIntegrationTests` (F5-08) — que también invoca `Full-Backup.sql`/`Differential-Backup.sql`/`Log-Backup.sql` — necesitó actualizarse para aprovisionar y transferir el mismo certificado (ver commit de esta tarea): el cifrado ahora es obligatorio para cualquier consumidor de esos scripts, incluidos los ya existentes. Tras el ajuste, la suite completa de `Shared.Infrastructure.Persistence.Tests` (113 pruebas, incluidas F5-01 a F5-09) pasa sin regresiones.

---

## 3. WORM (Write Once Read Many) — resistencia a borrado probada

### 3.1 Mecanismo implementado

[`tools/SqlBackupAutomation/Protect-BackupImmutability.ps1`](../tools/SqlBackupAutomation/Protect-BackupImmutability.ps1) aplica, sobre la copia de un backup en la bóveda (sección 1), tres capas:

1. **Atributo `ReadOnly`** del sistema de archivos — primera barrera, insuficiente por sí sola (reversible con `Set-ItemProperty -Name IsReadOnly -Value $false`, sin tocar ACLs).
2. **ACL con regla `Deny` explícita** (no solo ausencia de `Allow`) sobre `Delete`, `DeleteSubdirectoriesAndFiles`, `WriteData` y `AppendData`, para la identidad indicada (por defecto, la identidad que ejecuta el propio proceso de backup). En Windows/NTFS, un `Deny` explícito tiene prioridad sobre cualquier `Allow` heredado o explícito para esa misma identidad — esta es la capa real de protección, verificada en la sección 3.2.
3. **Metadata de retención** (`<archivo>.worm.json`) con la fecha de expiración del bloqueo, para que `Unlock-BackupImmutability.ps1` (acción administrativa separada, auditable, nunca automática) no pueda liberar el candado antes de tiempo sin `-Force` y una `-Reason` explícita registrada.

Se denegó `Delete`/`WriteData`/`AppendData`, pero **no** `ChangePermissions`/`TakeOwnership` — esta es una elección de diseño deliberada, equivalente al modo **"Governance"** de S3 Object Lock (reversible mediante una acción privilegiada separada y auditada) y no al modo **"Compliance"** (irreversible ni para el administrador raíz). Se documenta esta elección explícitamente: un WORM en modo "Compliance" real tampoco es alcanzable en un sistema de archivos NTFS local (cualquier administrador con `TakeOwnership` podría, en última instancia, tomar posesión del archivo y modificar su ACL) — ese nivel de garantía requiere infraestructura especializada de almacenamiento inmutable (sección 4).

### 3.2 Prueba de resistencia a borrado — criterio de aceptación de F5-09

[`tests/Shared.Infrastructure.Persistence.Tests/Integration/BackupImmutabilityFileSystemTests.cs`](../tests/Shared.Infrastructure.Persistence.Tests/Integration/BackupImmutabilityFileSystemTests.cs) ejercita el **mismo mecanismo real del sistema operativo** que `Protect-BackupImmutability.ps1` (una `FileSystemAccessRule` de tipo `Deny` sobre `Delete`/`WriteData`/`AppendData`, más el atributo `ReadOnly`) directamente contra un archivo real en el disco de esta máquina, y realiza un **intento real de `File.Delete(filePath)`** desde el mismo proceso que aplicó el candado (el escenario mínimo exigido: ni el propio proceso que generó/protegió el backup puede borrarlo) — no una simulación ni una aserción sobre el flag `ReadOnly` únicamente.

**Resultado de ejecución real (2026-09-09, Windows 11, `dotnet test` en esta máquina):**

```
dotnet test tests/Shared.Infrastructure.Persistence.Tests/Shared.Infrastructure.Persistence.Tests.csproj --filter "FullyQualifiedName~BackupImmutabilityFileSystemTests"

Correctas: ArchivoBloqueadoConAclDeny_FileDelete_LanzaUnauthorizedAccessException [21 ms]
  -> File.Delete(filePath) lanzó System.UnauthorizedAccessException real.
  -> El archivo bloqueado siguió existiendo tras el intento de borrado fallido.

Pruebas totales: 1
     Correcto: 1
```

Esto cumple literalmente el criterio de aceptación de F5-09 ("Resistencia a borrado probada"): el borrado se intentó de verdad, con las mismas credenciales/permisos del proceso que normalmente opera, y el sistema operativo lo rechazó.

**Limitación de plataforma, documentada sin ocultarla:** las ACL de `FileSystemSecurity`/`AccessControlType.Deny` son específicas de NTFS/Windows — no aplican en Linux/macOS. El pipeline de CI de este repositorio corre en `ubuntu-latest`, por lo que la prueba se auto-omite (`return` temprano) en ese entorno sin fallar el build; la evidencia real de ejecución exitosa en Windows queda documentada aquí (arriba) como el artefacto de verificación, tal como se hizo con la verificación manual de `xp_delete_file` sobre SQL Server Linux en F5-07 (`docs/politica-backups-fase5.md` sección 5, nota de verificación previa).

### 3.3 Retención — cómo interactúa con `Retention-Cleanup.sql`

`Retention-Cleanup.sql` (F5-07) borra por antigüedad **solo en la carpeta operativa** (`-BackupRoot`), nunca en la bóveda (`-VaultRoot`) — son carpetas distintas por diseño (sección 1.2), así que la limpieza de retención normal no puede alcanzar, ni por error, un backup ya protegido con WORM. La liberación de un backup de la bóveda es exclusivamente responsabilidad de `Unlock-BackupImmutability.ps1`, ejecutado manualmente por un operador cuando la retención WORM (`retentionUntilUtc` en el `.worm.json`) ya expiró, o con `-Force -Reason` explícito en un procedimiento de excepción auditado.

---

## 4. Runbook para infraestructura cloud real (pendiente de decisión, no fingido como resuelto)

Este repositorio no incluye una cuenta cloud real, así que **no se genera evidencia simulada** de un WORM de nivel "Compliance" en un object storage con Object Lock. Se documenta en cambio, como runbook ejecutable el día en que exista esa infraestructura (y previa aprobación humana si implica una nueva cuenta/proveedor de nube, sección 13 del Plan Maestro), el mapeo exacto de este mecanismo local a las dos opciones de mercado más usadas:

| Concepto de este documento | Azure Blob Storage | AWS S3 |
|---|---|---|
| Aislamiento (sección 1) | Cuenta de almacenamiento separada de la suscripción/RG productivo, con Managed Identity dedicada de solo escritura para el proceso de backup (nunca la misma identidad que administra SQL Server). | Cuenta de AWS separada (o al menos bucket + IAM role dedicados) con política de bucket que deniega explícitamente `s3:DeleteObject`/`s3:PutObject` a cualquier principal salvo el rol de backup. |
| Cifrado (sección 2) | El cifrado de `BACKUP ... WITH ENCRYPTION` de esta tarea sigue aplicando (cifrado a nivel de aplicación, portable); adicionalmente, Azure Storage cifra en reposo por defecto (SSE con claves administradas por Microsoft o por el cliente vía Key Vault). | Igual: el cifrado de SQL Server se mantiene; S3 añade SSE-S3/SSE-KMS como capa adicional de la plataforma. |
| WORM modo "Governance" (sección 3, equivalente) | **Immutable Blob Storage** con política de retención basada en tiempo, modo "sin bloqueo legal" (permite extender pero no acortar sin permiso elevado). | **S3 Object Lock**, modo **Governance** (`s3:BypassGovernanceRetention` requerido para override, auditable en CloudTrail). |
| WORM modo "Compliance" (no alcanzable localmente, sección 3.1) | Immutable Blob Storage con política de retención **bloqueada** ("locked") — irreversible ni para el propietario de la suscripción hasta que expire. | S3 Object Lock, modo **Compliance** — irreversible ni para el usuario raíz de la cuenta hasta que expire. |
| Prueba de resistencia a borrado (sección 3.2, equivalente) | Intentar `az storage blob delete` con la identidad de backup contra un blob bajo retención inmutable — debe fallar con `BlobImmutableDueToPolicy`. | Intentar `aws s3api delete-object` con el rol de backup contra un objeto bajo Object Lock — debe fallar con `AccessDenied`/`InvalidObjectState`. |

Ninguna fila de esta tabla se declara "hecha" en este repositorio — es la guía operativa para cuando exista la cuenta cloud real, análoga a como F4 documentó el paso a un clúster Kubernetes real sin fingir evidencia de un clúster que no existía en ese entorno.

---

## 5. Resumen frente al criterio de aceptación de F5-09

| Exigencia del backlog | Estado | Evidencia |
|---|---|---|
| Aislamiento | Mecanismo de software real (bóveda con identidad/ACL separada) entregado y ejecutable; separación de cuenta/red cloud real documentada como runbook (sección 1.3, 4) — no fingida. | `Protect-BackupImmutability.ps1`, `Unlock-BackupImmutability.ps1`. |
| Cifrado | Backups cifrados con `ENCRYPTION (ALGORITHM = AES_256, SERVER CERTIFICATE = ...)`, obligatorio en el orquestador. Restauración exitosa con certificado transferido, y fallo real sin él. | `Full-Backup.sql`/`Differential-Backup.sql`/`Log-Backup.sql`, `Enable-/Export-/Import-BackupEncryptionCertificate.sql`, `SqlBackupRestoreIntegrationTests` (3/3 correctas). |
| WORM — Resistencia a borrado probada | Intento real de `File.Delete` contra un archivo protegido con ACL `Deny`, ejecutado en esta máquina, rechazado por el sistema operativo con `UnauthorizedAccessException`. | `BackupImmutabilityFileSystemTests` (1/1 correcta, ejecutada en Windows). |

**Regresión de la suite completa:** `dotnet test tests/Shared.Infrastructure.Persistence.Tests/Shared.Infrastructure.Persistence.Tests.csproj` → 113/113 correctas (incluye F5-01 a F5-09, sin romper Outbox/Inbox/multi-tenancy/transacciones/log-shipping/backups/PITR previos). `dotnet build BitCode.Framework.slnx` → compilación correcta, 0 errores.

## 6. Pendientes explícitos (fuera de alcance de F5-09)

- **Infraestructura cloud real con Object Lock/Immutable Storage** (sección 4) — requiere aprobar una cuenta/proveedor cloud real (sección 13 del Plan Maestro) que no existe hoy en este repositorio.
- **Segmentación de red real** entre la carpeta operativa y la bóveda (hoy son dos carpetas en el mismo host/sistema de archivos en el entorno de prueba; en producción deben vivir en redes/cuentas distintas, ver sección 1.3).
- **Rotación del certificado de cifrado de backups** — este documento no define una política de rotación periódica del certificado de `Enable-BackupEncryption.sql`; los backups cifrados con un certificado rotado seguirán requiriendo el certificado original (no el nuevo) para restaurarse, así que una política de rotación debe conservar todos los certificados históricos usados, no solo el vigente. Queda como trabajo futuro explícito.
- **Agendamiento productivo real** de `Protect-BackupImmutability.ps1`/`-Immutable` como parte del job periódico — igual que F5-07, esta tarea entrega el mecanismo y la integración opcional en `Invoke-SqlBackupJob.ps1` (parámetros `-Immutable -VaultRoot -ImmutabilityDays`), no la creación de un job en un servidor productivo concreto.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — backlog F5-09, sección 13 (aprobaciones humanas).
- [`politica-backups-fase5.md`](politica-backups-fase5.md) — política de frecuencia/retención de backups (F5-07), complementaria a este documento.
- [`runbook-pitr-fase5.md`](runbook-pitr-fase5.md) — restauración point-in-time (F5-08), que ahora también depende de la exportación/importación del certificado de esta tarea.
- [`guia-secret-provider.md`](guia-secret-provider.md) — gestión de contraseñas/secretos referenciada en la sección 2.2.
- [`tools/SqlBackupAutomation/`](../tools/SqlBackupAutomation/) — scripts versionados (todas las secciones de este documento).
- [`tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlBackupRestoreIntegrationTests.cs`](../tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlBackupRestoreIntegrationTests.cs) — evidencia de cifrado y DR entre servidores (sección 2.3).
- [`tests/Shared.Infrastructure.Persistence.Tests/Integration/BackupImmutabilityFileSystemTests.cs`](../tests/Shared.Infrastructure.Persistence.Tests/Integration/BackupImmutabilityFileSystemTests.cs) — evidencia de resistencia a borrado (sección 3.2).
