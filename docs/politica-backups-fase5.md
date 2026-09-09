# Política de backups — Fase 5, Disaster Recovery y multi-región

**Tarea:** F5-07 (Fase 5 — Disaster Recovery y multi-región) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07
**Depende de:** F5-01 ([`docs/bia-fase5.md`](bia-fase5.md) — perfiles DR por componente), F5-04 ([`docs/replicacion-sql-fase5.md`](replicacion-sql-fase5.md) — topología de replicación SQL, con la que esta política es complementaria, no sustituta).
**Estado:** Política y jobs definidos y versionados. Restauración validada con una prueba real de ciclo completo full → differential → log → restore (ver sección 5).

**Alcance:** esta política cubre `Shared.Infrastructure.Persistence` (SQL Server, la persistencia predeterminada del framework, ver `docs/bia-fase5.md` fila 4). No cubre backups de Kafka (F5-05, `docs/replicacion-kafka-fase5.md`) ni de Redis/cache (F5-06, `docs/cache-regional-fase5.md`, que ya documenta que el cache es reconstruible y no requiere backup). El runbook operado de point-in-time restore (`RESTORE ... WITH STOPAT`) sobre esta misma cadena está resuelto en `docs/runbook-pitr-fase5.md` (F5-08). El aislamiento, cifrado en reposo y protección WORM contra borrado de los backups está resuelto en `docs/backups-inmutables-fase5.md` (F5-09) — `Full-Backup.sql`/`Differential-Backup.sql`/`Log-Backup.sql` exigen desde esa tarea un certificado de cifrado (`CertificateName`).

---

## 1. Por qué backups además de replicación (F5-04)

`docs/replicacion-sql-fase5.md` ya define una topología de replicación continua (Always On AG asíncrono / log shipping) para RPO/RTO entre regiones. Los backups periódicos son un mecanismo **complementario, no redundante**, porque cubren escenarios que la replicación no cubre:

- **Corrupción lógica o borrado accidental/malicioso** replicado instantáneamente a la secundaria (un `DELETE` erróneo se replica también) — solo un backup a un punto anterior en el tiempo permite recuperarse de esto, no una réplica en caliente.
- **Retención a largo plazo** (cumplimiento, auditoría, disputas) — la replicación solo mantiene el estado actual (o el estado con segundos/minutos de atraso), no un historial de puntos de recuperación de semanas/meses.
- **Restauración point-in-time (F5-08, ver `docs/runbook-pitr-fase5.md`)** — depende de tener una cadena `full + differential + log` intacta y con retención suficiente; sin esta política, F5-08 no tiene sobre qué operar.

Esta política no reemplaza la decisión de replicación de F5-04; ambas son capas de defensa distintas del mismo objetivo de RPO/RTO por perfil.

---

## 2. Esquema de backups por perfil DR

El esquema de frecuencia sigue los perfiles DR ya asignados en `docs/bia-fase5.md` (sección 4): `Shared.Infrastructure.Persistence` tiene perfil objetivo **Platinum**, pero un consumidor concreto construido sobre el framework puede operar con datos de perfil **Gold** o **Standard** (p. ej. `Sample.Api`, Standard). Por eso el esquema se parametriza por perfil, no se fija un único valor para "toda base de datos SQL Server del framework".

| Perfil DR (BIA) | Full backup | Differential backup | Log backup (frecuencia) | Justificación |
|---|---|---|---|---|
| **Platinum** | Semanal (domingo 02:00, ventana de bajo tráfico) | Diario (02:00, excepto domingo) | **Cada 1 minuto** | El RPO objetivo es "cercano a cero" (`docs/bia-fase5.md` sección 1). Sin una decisión explícita de replicación síncrona (pendiente, ver `docs/replicacion-sql-fase5.md` sección 2), el mecanismo de backup por sí solo no puede lograr RPO≈0 — pero un log backup cada 1 minuto acota el **peor caso de pérdida vía backups** (independiente de la réplica) a ~1 minuto, la cota más agresiva operacionalmente sostenible sin saturar de I/O al servidor primario. Se documenta explícitamente que esto es una cota de la capa de backups, no un compromiso de RPO≈0 (ese compromiso requiere la decisión de sección 13 del Plan Maestro, no tomada aquí). |
| **Gold** | Semanal (domingo 02:00) | Diario (02:00, excepto domingo) | **Cada 5 minutos** | RPO objetivo hasta 60 segundos (`docs/bia-fase5.md`). El log backup periódico no alcanza por sí solo un RPO de 60s (ese RPO lo cubre la réplica asíncrona de F5-04, con lag típico de segundos); el log backup de 5 minutos es la cota de la capa de backups como red de seguridad adicional ante corrupción lógica, alineado con el intervalo de alerta de lag propuesto en `docs/replicacion-sql-fase5.md` sección 2 (30s de umbral de alerta, con margen operativo para el propio job de backup). |
| **Standard** | Semanal (domingo 02:00) | Diario (02:00) | **Cada 15 minutos** | RPO objetivo hasta 15 minutos — el log backup de 15 minutos por sí solo ya cubre el RPO objetivo del perfil sin necesitar replicación síncrona ni de baja latencia, consistente con "backoffice" (menor criticidad). |

**Nota sobre la base en modo `RECOVERY FULL`:** los tres perfiles requieren que la base de datos esté en modelo de recuperación `FULL` (nunca `SIMPLE`) para que los backups de log sean posibles — el mismo requisito ya usado en `docs/replicacion-sql-fase5.md` y en la prueba de F5-04. Sin esto, no hay backup de log, y el RPO de la capa de backups degrada al intervalo del último differential/full (horas), independientemente del perfil.

---

## 3. Retención

La retención se alinea con la criticidad del perfil (a mayor perfil, mayor retención — coherente con que Platinum incluye, en `docs/bia-fase5.md` fila 17, el objetivo de auditoría inmutable, que exige historial más largo) y con un mínimo operativo: la retención de `differential` y `log` debe ser siempre mayor o igual al intervalo entre dos `full` consecutivos, para que nunca exista una ventana sin una cadena completa restaurable.

| Perfil DR | Retención `full` | Retención `differential` | Retención `log` |
|---|---|---|---|
| **Platinum** | 90 días | 35 días (5 semanas, cubre 5 ciclos de full) | 14 días |
| **Gold** | 60 días | 21 días (3 semanas) | 7 días |
| **Standard** | 30 días | 14 días (2 semanas) | 3 días |

La limpieza de retención se ejecuta **por tipo de backup y por extensión de archivo** (`.bak` para full/differential, `.trn` para log), nunca en bloque, para no borrar accidentalmente un `full` reciente junto con un `log` antiguo — ver `Retention-Cleanup.sql` en la sección 4.

**Nota — inmutabilidad (F5-09):** esta política define la retención lógica de la carpeta operativa (cuánto tiempo se conservan y cuándo se limpian con `Retention-Cleanup.sql`); el aislamiento, cifrado y protección WORM contra borrado (incluso accidental o malicioso) de una copia adicional en bóveda están resueltos en `docs/backups-inmutables-fase5.md` (F5-09). `Retention-Cleanup.sql` sigue operando solo sobre la carpeta operativa, nunca sobre la bóveda WORM — son carpetas distintas por diseño (ver F5-09 sección 1).

---

## 4. Automatización — scripts versionados

Los scripts viven en [`tools/SqlBackupAutomation/`](../tools/SqlBackupAutomation/), versionados en el repositorio (no en un job embebido en la aplicación):

| Archivo | Rol |
|---|---|
| `Full-Backup.sql` | `BACKUP DATABASE ... WITH INIT, COMPRESSION, CHECKSUM`. Variables sqlcmd: `DatabaseName`, `BackupPath`. |
| `Differential-Backup.sql` | `BACKUP DATABASE ... WITH DIFFERENTIAL, INIT, COMPRESSION, CHECKSUM`. Mismas variables. |
| `Log-Backup.sql` | `BACKUP LOG ... WITH INIT, COMPRESSION, CHECKSUM`. Mismas variables. |
| `Retention-Cleanup.sql` | Calcula la fecha de corte (`GETDATE() - <días>`) en una variable local y ejecuta `EXEC master.dbo.xp_delete_file 0, N'<carpeta>', N'<extensión>', @cutoffDate` — borra únicamente archivos de la extensión indicada, más antiguos que la fecha de corte. Variables sqlcmd: `BackupFolder`, `Extension`, `RetentionDays`. |
| `Invoke-SqlBackupJob.ps1` | Orquestador PowerShell (Windows y PowerShell 7+/pwsh en Linux) que invoca los cuatro scripts anteriores vía `sqlcmd`, con nombres de archivo con timestamp (`<db>_full_yyyyMMddHHmmss.bak`, etc.), según los parámetros de perfil (full/differential/log/retención) de la sección 2 y 3. Desde F5-09, exige `-CertificateName` para Full/Differential/Log (backups cifrados obligatorios) y admite `-Immutable -VaultRoot -ImmutabilityDays` para proteger cada backup generado con WORM — ver `docs/backups-inmutables-fase5.md`. |
| `Enable-/Export-/Import-BackupEncryptionCertificate.sql` | (F5-09) Aprovisionan, exportan e importan el certificado de cifrado de backups entre servidores (necesario para restaurar en un servidor distinto al que generó el backup). Ver `docs/backups-inmutables-fase5.md` sección 2. |
| `Protect-/Unlock-BackupImmutability.ps1` | (F5-09) Aplican y liberan el candado WORM (aislamiento en bóveda + ACL Deny + ReadOnly) sobre una copia de un backup ya generado. Ver `docs/backups-inmutables-fase5.md` sección 3. |

### 4.1 Por qué scripts T-SQL + PowerShell versionados, y no un Quartz job en C#

`docs/guia-quartz-ha.md` (F4-11) ya demuestra que Quartz con `AdoJobStore` clusterizado puede sostener recovery real entre procesos — es infraestructura reutilizable y válida para trabajos de aplicación. Sin embargo, para backups de la propia base de datos se prioriza **no acoplar la ejecución del backup al mismo motor cuya disponibilidad se está protegiendo**:

- El `AdoJobStore` de Quartz **persiste su propio estado en la misma base de datos SQL Server** que se necesita respaldar. Si esa base sufre un incidente (el escenario exacto que el backup debe mitigar), el propio disparo del job de backup podría verse afectado en el peor momento — justo cuando más se necesita.
- La práctica estándar de la industria (y la que exige la mayoría de auditorías de continuidad de negocio) es que la ejecución de backups de base de datos sea responsabilidad de un mecanismo **externo a la aplicación** (SQL Server Agent, un scheduler de sistema operativo — Task Scheduler/`cron` invocando `pwsh`/`sqlcmd`, o una tarea de orquestación de infraestructura), independiente del ciclo de vida del proceso de aplicación.
- Los scripts T-SQL/PowerShell versionados son ejecutables, auditables en el propio repositorio (control de cambios de la política vía Git, igual que cualquier otro artefacto), y no requieren que la aplicación esté corriendo para que el backup se dispare — a diferencia de un `IJob` de Quartz hospedado dentro del proceso de la API.

Se documenta esta decisión explícitamente (Diseño, sección 3.3 del Plan Maestro) porque es la elección arquitectónica clave de esta tarea: **backups desacoplados del runtime de la aplicación**, agendados por SQL Server Agent (`sp_add_job`/`sp_add_jobstep` apuntando a `sqlcmd -i Invoke-SqlBackupJob.ps1` o directamente a los `.sql`) o por el scheduler del sistema operativo del host de infraestructura, no por el framework en sí.

### 4.2 Ejemplo de agendamiento (referencia, no un compromiso de infraestructura productiva)

```powershell
# Full semanal (perfil Gold, ejemplo)
pwsh ./tools/SqlBackupAutomation/Invoke-SqlBackupJob.ps1 `
  -SqlInstance "sql-primary.contoso.local" -Database "AppDb" `
  -BackupRoot "\\backup-share\sql\AppDb" -BackupType Full `
  -RetentionDays 60

# Differential diario (perfil Gold, ejemplo)
pwsh ./tools/SqlBackupAutomation/Invoke-SqlBackupJob.ps1 `
  -SqlInstance "sql-primary.contoso.local" -Database "AppDb" `
  -BackupRoot "\\backup-share\sql\AppDb" -BackupType Differential `
  -RetentionDays 21

# Log cada 5 minutos (perfil Gold, ejemplo)
pwsh ./tools/SqlBackupAutomation/Invoke-SqlBackupJob.ps1 `
  -SqlInstance "sql-primary.contoso.local" -Database "AppDb" `
  -BackupRoot "\\backup-share\sql\AppDb" -BackupType Log `
  -RetentionDays 7
```

Estos comandos se agendarían como pasos de un SQL Server Agent Job (o tareas de Task Scheduler/`cron`) con la frecuencia de la sección 2 según el perfil real del consumidor — no se fija aquí un servidor, ruta de red ni credencial real (evita hardcodear infraestructura productiva inexistente en este repositorio).

---

## 5. Verificación — restauración validada (criterio de aceptación de F5-07)

**Prueba:** [`tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlBackupRestoreIntegrationTests.cs`](../tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlBackupRestoreIntegrationTests.cs)

La prueba ejecuta, contra dos contenedores Testcontainers SQL Server reales e independientes (un "origen" que produce los backups y un "destino" que los restaura — mismo patrón de copia de archivo entre sistemas de archivos distintos que `SqlLogShippingRpoIntegrationTests` de F5-04), el **texto real** de los cuatro scripts de `tools/SqlBackupAutomation/` (leídos del disco y con sus variables `$(...)` sustituidas exactamente como lo haría `sqlcmd -v`, sin reescribir la lógica SQL en el test):

1. `Full-Backup.sql` sobre un lote inicial de filas confirmadas.
2. Inserta un segundo lote de filas.
3. `Differential-Backup.sql` sobre ese estado.
4. Inserta un tercer lote de filas.
5. `Log-Backup.sql` sobre ese estado.
6. En el contenedor "destino": `RESTORE DATABASE ... WITH NORECOVERY` (full) → `RESTORE DATABASE ... WITH DIFFERENTIAL, NORECOVERY` → `RESTORE LOG ... WITH RECOVERY` (deja la base restaurada en línea).
7. Verifica que el conjunto de filas restaurado en el "destino" es **exactamente igual** (mismo conteo y mismo checksum de datos) al del "origen" en el momento del último log backup — la cadena `full + differential + log` reconstruye los datos hasta el punto esperado.

Adicionalmente, la prueba ejecuta `Retention-Cleanup.sql` contra un directorio con un backup "antiguo" (fecha de archivo forzada al pasado) y uno "reciente", y verifica que `xp_delete_file` (confirmado funcional en la imagen Linux de SQL Server usada por Testcontainers, ver nota siguiente) borra únicamente el archivo fuera de la ventana de retención, dejando intacto el que está dentro.

**Nota de verificación previa (no automatizada, manual, antes de escribir la prueba):** se confirmó con un contenedor SQL Server 2022 Linux real (`mcr.microsoft.com/mssql/server:2022-latest`) que `master.dbo.xp_delete_file` funciona igual que en Windows: borra archivos por extensión y fecha de corte en la imagen Linux usada por Testcontainers, incluyendo backups reales (`BACKUP DATABASE`) con fecha de archivo forzada al pasado vía `touch -d`. Esto valida que el mecanismo de retención de `Retention-Cleanup.sql` es real y no solo documentado.

**Resultado de ejecución real (2026-09-07/08, dos contenedores `mcr.microsoft.com/mssql/server:2022-latest` vía Testcontainers):**

```
dotnet test tests/Shared.Infrastructure.Persistence.Tests/Shared.Infrastructure.Persistence.Tests.csproj --filter "FullyQualifiedName~SqlBackupRestoreIntegrationTests"

Filas esperadas (origen, hasta el último log backup): 15, checksum D8B6FBD6
Filas restauradas (destino, tras full+differential+log): 15, checksum D8B6FBD6

Pruebas totales: 2
     Correcto: 2
```

Antes de fijar la implementación de `Retention-Cleanup.sql`, se detectó y corrigió un problema real: `EXEC master.dbo.xp_delete_file 0, N'<carpeta>', N'<extensión>', GETDATE() - 30` falla con `Incorrect syntax near ')'` porque el procedimiento extendido no acepta una expresión inline como argumento — se corrigió calculando la fecha de corte en una variable `DECLARE @cutoffDate DATETIME = GETDATE() - $(RetentionDays);` antes del `EXEC` (ver el script). También se verificó manualmente, contra un contenedor real adicional, que `xp_delete_file` sí funciona igual en la imagen Linux de SQL Server que en Windows (borra por extensión y fecha de corte, incluyendo backups reales generados con `BACKUP DATABASE`).

**Verificación adicional (regresión):**
- `dotnet test tests/Shared.Infrastructure.Persistence.Tests/Shared.Infrastructure.Persistence.Tests.csproj` → 109/109 exitosas (107 preexistentes + 2 nuevas de esta tarea, sin romper Outbox/Inbox/multi-tenancy/transacciones/log-shipping de F5-04).
- `dotnet build BitCode.Framework.slnx` → compilación correcta de toda la solución, 0 errores.

---

## 6. Pendientes explícitos (fuera de alcance de F5-07)

- **F5-08 (PITR):** ~~construir el runbook operado de point-in-time restore (`RESTORE ... WITH STOPAT`) sobre la misma cadena de backups que esta tarea automatiza, y medir el tiempo real de ejecución.~~ Resuelto — ver `docs/runbook-pitr-fase5.md`.
- **F5-09 (Backups inmutables):** ~~aplicar aislamiento, cifrado en reposo y protección WORM (immutabilidad real contra borrado, incluso privilegiado) sobre el repositorio de backups.~~ Resuelto — ver `docs/backups-inmutables-fase5.md`.
- **Agendamiento productivo real** (SQL Server Agent Job / Task Scheduler / cron apuntando a infraestructura real) — esta tarea entrega los scripts versionados y la política de frecuencia/retención, no la creación de un job en un servidor productivo concreto (no hay hoy un servidor productivo real en este repositorio al cual apuntar; ver sección 13 del Plan Maestro sobre "habilitación de tráfico productivo").
- **Backup de Kafka/cache** — fuera de alcance de esta tarea (ver sección "Alcance").
- **Recalibración de frecuencias/retención con negocio e infraestructura real** (costo de almacenamiento, ventanas de mantenimiento reales) — los valores de las secciones 2 y 3 son la propuesta técnica de partida, alineada a los perfiles del BIA, no una calibración final aprobada por negocio.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — backlog F5-07, sección 13 (aprobaciones humanas).
- [`bia-fase5.md`](bia-fase5.md) — perfiles DR por componente (F5-01).
- [`replicacion-sql-fase5.md`](replicacion-sql-fase5.md) — topología de replicación SQL (F5-04), complementaria a esta política.
- [`guia-quartz-ha.md`](guia-quartz-ha.md) — evidencia de por qué Quartz HA existe pero no se usa para este job (sección 4.1).
- [`tools/SqlBackupAutomation/`](../tools/SqlBackupAutomation/) — scripts versionados (sección 4).
- [`tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlBackupRestoreIntegrationTests.cs`](../tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlBackupRestoreIntegrationTests.cs) — prueba real que valida la restauración (sección 5).
- [`backups-inmutables-fase5.md`](backups-inmutables-fase5.md) — aislamiento, cifrado y protección WORM de los backups (F5-09).
