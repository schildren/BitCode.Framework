# Runbook — Point-in-Time Restore (PITR), Fase 5 — Disaster Recovery y multi-región

**Tarea:** F5-08 (Fase 5 — Disaster Recovery y multi-región) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07/08
**Depende de:** F5-07 ([`docs/politica-backups-fase5.md`](politica-backups-fase5.md) — esquema de backups `full + differential + log`, scripts en [`tools/SqlBackupAutomation/`](../tools/SqlBackupAutomation/), sin la cual no hay sobre qué operar un PITR).
**Estado:** Runbook operativo definido y probado con un PITR real contra dos instancias de SQL Server (Testcontainers), con varias transacciones de negocio distribuidas en el tiempo, restaurando exactamente hasta un punto ubicado entre dos de ellas y midiendo el tiempo real de la secuencia (ver sección 5).

**Alcance:** este runbook cubre exclusivamente el procedimiento de restauración a un instante exacto (`RESTORE ... WITH STOPAT`) para `Shared.Infrastructure.Persistence` (SQL Server). No cubre la generación/retención de los backups en sí (F5-07), ni inmutabilidad/WORM del repositorio de backups (F5-09, tarea posterior — hoy nada impide que un operador o un atacante borre un `.trn` necesario para un PITR antes de tiempo), ni failover/failback automatizado (F5-10/F5-11).

---

## 1. Cuándo usar este runbook (y cuándo no)

Un PITR es el procedimiento correcto cuando el objetivo es **volver la base a un instante anterior exacto**, no simplemente "restaurar el último backup disponible":

- Corrupción lógica o borrado accidental/malicioso de datos, detectado después de que ya ocurrió (un `DELETE`/`UPDATE` erróneo, o una migración de esquema que dañó datos) — se conoce (o se acota) la hora aproximada del incidente y se quiere recuperar el estado **inmediatamente anterior**, sin perder las transacciones legítimas posteriores que no tienen relación con el incidente y que sí interesa conservar en un entorno paralelo para reconciliación.
- Investigación forense/auditoría: reconstruir el estado exacto de la base en un instante pasado específico (p. ej. "¿cómo estaba el saldo del cliente X a las 14:32:07 del día del incidente?").
- Verificación periódica de que la cadena de backups es realmente restaurable a un punto arbitrario (ejercicio de DR programado), no solo al final de la cadena.

**No** es el procedimiento correcto para:
- Un failover a la réplica secundaria por caída del primario (eso es replicación continua, `docs/replicacion-sql-fase5.md`, F5-04/F5-10) — un PITR reconstruye una base **nueva**, restaurada desde backups, con el tiempo de restore de la sección 5 (varios segundos a minutos según el tamaño real de la base), no es un mecanismo de alta disponibilidad de bajo RTO.
- Recuperar **todo** hasta el backup de log más reciente sin ningún punto intermedio — para ese caso alcanza la cadena `full + differential + log` completa de F5-07 (`RESTORE LOG ... WITH RECOVERY` sin `STOPAT`), no hace falta este runbook.

---

## 2. Prerrequisitos

1. La base de datos afectada está en modelo de recuperación `FULL` (requisito ya documentado en `docs/politica-backups-fase5.md` sección 2) y tiene una cadena continua de backups: **un `full`**, **cero o más `differential`** posteriores a ese `full`, y **una secuencia ininterrumpida de `log`** desde el `full`/`differential` más reciente hasta, al menos, un instante posterior al punto que se quiere restaurar.
2. Se conoce (aunque sea aproximadamente) el **instante objetivo** (`STOPAT`) al que se quiere restaurar, en la zona horaria/reloj del **servidor de origen** de los backups — nunca en la zona horaria del operador ni de la máquina desde la que se ejecuta el restore. Ver sección 4 sobre cómo acotar este instante si no se conoce con precisión.
3. Existe un servidor/instancia SQL Server **destino** distinto del origen (o, si es el mismo servidor, una base con **nombre distinto** — nunca sobrescribir en caliente la base de producción con un restore exploratorio) con espacio en disco suficiente y acceso a los archivos de backup (`.bak`/`.trn`), copiados desde el repositorio de backups.
4. El operador tiene permiso `RESTORE DATABASE`/`ALTER ANY DATABASE` sobre la instancia destino.

---

## 3. Procedimiento paso a paso

### 3.1 Elegir qué backups aplicar

No se restauran "todos los backups disponibles": se restaura el `full` más reciente **anterior o igual** al `STOPAT` objetivo, el `differential` más reciente **anterior o igual** al `STOPAT` (si existe alguno posterior al `full` elegido), y **solo** los backups de `log` necesarios para llegar exactamente al `STOPAT` — nunca los posteriores.

1. Listar los backups disponibles del repositorio (`Retention-Cleanup.sql`/política de F5-07 documentan dónde y con qué nombre se generan — típicamente `<db>_full_yyyyMMddHHmmss.bak`, `<db>_differential_yyyyMMddHHmmss.bak`, `<db>_log_yyyyMMddHHmmss.trn`, ver `Invoke-SqlBackupJob.ps1`).
2. Elegir el `full` cuyo timestamp de nombre de archivo sea el más reciente **anterior** al `STOPAT` objetivo.
3. Elegir, si existe, el `differential` más reciente tomado **después de ese `full`** y **anterior o igual** al `STOPAT` objetivo (si el `STOPAT` cae antes del primer `differential` posterior al `full`, se omite el paso de `differential` y se aplican los `log` directamente sobre el `full`).
4. Ordenar cronológicamente **todos** los backups de `log` tomados después del `full`/`differential` elegido, y aplicarlos **en orden estricto**, uno por uno, hasta encontrar el primero cuyo rango de tiempo (desde el fin del backup de log anterior hasta el momento en que se tomó este backup) **contiene** al `STOPAT` objetivo — ese es el **último** backup de log que se aplica. Ninguno de los backups de log posteriores a ese se copia ni se restaura (ver la prueba de la sección 5, que deliberadamente no copia el tercer backup de log al destino).
5. Si no es evidente en qué backup de log cae el `STOPAT` (por ejemplo, no se conoce con precisión la hora del incidente), usar `RESTORE HEADERONLY`/`RESTORE FILELISTONLY FROM DISK = N'<ruta>.trn'` sobre cada candidato: `BackupStartDate`/`BackupFinishDate` de `RESTORE HEADERONLY` delimitan el rango de tiempo cubierto por ese archivo de log. El backup correcto es aquel donde `BackupStartDate <= STOPAT < BackupFinishDate` (con margen: si el `STOPAT` cae justo en el borde, aplicar también el backup de log inmediatamente siguiente, con `STOPAT`, y verificar cuál de los dos efectivamente detiene el log en el punto esperado — un `STOPAT` fuera del rango cubierto por el archivo de log indicado produce el error de SQL Server `The log in this backup set begins at LSN ... which is too recent to apply to the database` o, si el `STOPAT` es posterior a todo el archivo, el archivo se aplica completo sin detenerse ahí).

### 3.2 Copiar los archivos elegidos al servidor/instancia destino

Copiar (SMB, `scp`, o el mecanismo real del repositorio de backups de la organización) únicamente: el `full` elegido, el `differential` elegido (si aplica) y la secuencia ordenada de `log` hasta el que contiene el `STOPAT` (inclusive). No copiar backups de log posteriores — reduce superficie de error operativo y dificulta aplicar por accidente más allá del punto deseado.

### 3.3 Ejecutar la secuencia de restore

Contra la instancia **destino**, en este orden exacto, sin ejecutar comandos intermedios sobre la base en restauración:

```sql
-- 1) Full backup, SIEMPRE con NORECOVERY (deja la base en estado "restoring", lista para
--    aceptar más backups en la cadena) — nunca WITH RECOVERY en este paso, o la cadena se cierra
--    y ningún differential/log posterior podrá aplicarse.
RESTORE DATABASE [NombreBaseDatos]
FROM DISK = N'<ruta>\full.bak'
WITH NORECOVERY, REPLACE;  -- REPLACE solo si la base destino no existe o se acepta sobrescribirla

-- 2) Differential backup, si se eligió uno en el paso 3.1. También WITH NORECOVERY.
RESTORE DATABASE [NombreBaseDatos]
FROM DISK = N'<ruta>\differential.bak'
WITH NORECOVERY;

-- 3) Backups de log ANTERIORES al que contiene el STOPAT, aplicados COMPLETOS
--    (WITH NORECOVERY, sin STOPAT) — quedan enteramente antes del punto objetivo por construcción
--    del paso 3.1.
RESTORE LOG [NombreBaseDatos]
FROM DISK = N'<ruta>\log1.trn'
WITH NORECOVERY;

-- (repetir para cada log intermedio, en orden cronológico)

-- 4) El backup de log que CONTIENE el STOPAT objetivo: este es el ÚNICO paso que lleva la
--    cláusula STOPAT, y es también el que cierra la cadena con RECOVERY (deja la base en línea,
--    lista para usarse).
RESTORE LOG [NombreBaseDatos]
FROM DISK = N'<ruta>\log-final.trn'
WITH STOPAT = N'2026-09-08 01:38:37.420', RECOVERY;
```

Formato recomendado para el literal de `STOPAT`: `'YYYY-MM-DD HH:MI:SS.mmm'` (formato ODBC canónico, sin ambigüedad de orden día/mes) — evitar formatos regionales (`DD/MM/YYYY` o `MM/DD/YYYY`) que dependen del idioma/región configurados en la sesión (`SET DATEFORMAT`) de la instancia destino.

Al completar el paso 4, la base queda **en línea** (no en estado "restoring") con los datos exactamente como estaban en el `STOPAT` elegido. No se puede aplicar ningún backup de log adicional después de este paso sin empezar la cadena de nuevo desde el `full` (`WITH RECOVERY` cierra la cadena de forma irreversible).

### 3.4 Verificar el resultado

Antes de considerar el PITR terminado:
1. Verificar que la base está en línea: `SELECT state_desc FROM sys.databases WHERE name = 'NombreBaseDatos'` debe devolver `ONLINE`.
2. Verificar el dato de negocio concreto que motivó el PITR (por ejemplo, confirmar que la fila/transacción problemática **no** está presente, y que las anteriores sí) — nunca dar el PITR por bueno solo porque el comando no reportó error; el `STOPAT` puede haber quedado en un punto distinto del esperado por un error de zona horaria o de elección de backup (ver sección 3.1, paso 5).
3. Documentar (ticket de incidente/auditoría): `STOPAT` usado, archivos de backup aplicados (con sus timestamps), operador, hora de inicio/fin de la secuencia de restore y el motivo del PITR.

---

## 4. Qué hacer si el `STOPAT` deseado cae dentro de una transacción en curso

`RESTORE ... WITH STOPAT` opera sobre el **log de transacciones**, que registra operaciones a nivel de página/registro, no "transacciones completas" como unidad atómica de corte. Si el instante elegido cae **en medio** de una transacción multi-sentencia que todavía no había hecho `COMMIT` en ese instante exacto:

- SQL Server **nunca** deja una transacción a medio aplicar de forma visible: el motor de recuperación (la misma fase de "redo/undo" que corre en cualquier arranque de SQL Server tras una caída) revierte automáticamente cualquier transacción que no estuviera confirmada (`COMMIT`) en el instante exacto del `STOPAT`. El resultado es **transaccionalmente consistente** — se obtiene el mismo tipo de garantía ACID que si el servidor real se hubiera apagado abruptamente en ese instante.
- Esto significa que, si el `STOPAT` elegido cae a mitad de una transacción de negocio (por ejemplo, un command `ITransactionalCommand` del framework que actualiza dos tablas relacionadas, ver `docs/convenciones.md` regla dura 3), **ninguna de las dos escrituras de esa transacción aparecerá** en la base restaurada — el corte respeta la atomicidad de la transacción, no dejará una escritura sin la otra.
- **Consecuencia práctica para elegir el `STOPAT`:** si el objetivo es "excluir exactamente la transacción problemática X y conservar todo lo anterior", el `STOPAT` correcto es cualquier instante **entre el `COMMIT` de la última transacción buena conocida y el inicio (o `COMMIT`) de la transacción X** — no hace falta acertar el instante exacto del `COMMIT` de X con precisión de milisegundos, porque cualquier punto anterior al `COMMIT` de X ya la excluye por completo (X nunca se aplica parcialmente). Este es exactamente el criterio usado en la prueba de la sección 5: el `STOPAT` es el punto medio entre el commit de la transacción a conservar y el commit de la transacción a excluir, con margen amplio en ambas direcciones — no se necesita el timestamp exacto de ningún `COMMIT`, alcanza con acotar el rango.
- Si, en cambio, el objetivo es "recuperar el máximo posible de una transacción larga que se sabe que iba a tardar minutos" (poco común, pero posible con una migración/batch de negocio de larga duración), **no existe forma de recuperar un resultado parcial de esa transacción vía PITR** — el `STOPAT` solo puede elegir "toda la transacción" (situándolo en o después de su `COMMIT`) o "nada de ella" (situándolo antes). Si se necesita ese nivel de granularidad, la única alternativa es reconstruir manualmente el efecto parcial deseado a partir de los datos de negocio ya restaurados (fuera del alcance de un PITR).
- Advertencia operativa: si tras el `RESTORE LOG ... WITH STOPAT` la base no queda con el estado esperado (por ejemplo, faltan datos que se creía debían estar confirmados antes del `STOPAT`), la causa más común es haber estimado mal el instante de `COMMIT` real de la transacción de referencia — repetir el procedimiento completo desde el `full` con un `STOPAT` ligeramente posterior (nunca se puede "avanzar" una base ya recuperada con `RECOVERY`; hay que reiniciar la cadena).

---

## 5. Verificación — PITR real medido (criterio de aceptación de F5-08)

**Prueba:** [`tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlPointInTimeRestoreIntegrationTests.cs`](../tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlPointInTimeRestoreIntegrationTests.cs)

Igual que `SqlBackupRestoreIntegrationTests` (F5-07), la prueba usa dos contenedores Testcontainers SQL Server reales e independientes ("origen" que produce los backups, "destino" que los restaura) y reutiliza el **texto real** de `Full-Backup.sql`, `Differential-Backup.sql` y `Log-Backup.sql` de `tools/SqlBackupAutomation/` (leídos del disco, variables `$(...)` sustituidas como lo haría `sqlcmd -v`) — no reimplementa la lógica de backup.

Escenario ejecutado, con varias transacciones de negocio distribuidas en el tiempo (separadas por esperas reales de 2 segundos, no simuladas):

1. Lote `antes-full` → `Full-Backup.sql`.
2. Lote `antes-diff` → `Differential-Backup.sql`.
3. Transacción de negocio conocida **"pedido-1"** (debe sobrevivir al PITR) → se captura la hora del **servidor de origen** (`SELECT SYSDATETIME()`) inmediatamente después de confirmarla → espera 2 s → `Log-Backup.sql` (`log1.trn`).
4. Transacción de negocio conocida **"pedido-2"** (debe quedar excluida) → se captura de nuevo la hora del servidor → espera 2 s → `Log-Backup.sql` (`log2.trn`).
5. Transacción adicional **"pedido-3"** → `Log-Backup.sql` (`log3.trn`) — este backup se genera pero **nunca se copia** al destino, evidencia de que el runbook (sección 3.1) elige hasta qué log aplicar, no "todos los disponibles".
6. `STOPAT` calculado como el punto medio exacto entre el commit de "pedido-1" y el de "pedido-2".
7. Secuencia real de restore, cronometrada de punta a punta con `System.Diagnostics.Stopwatch`: `RESTORE DATABASE ... WITH NORECOVERY` (full) → `RESTORE DATABASE ... WITH NORECOVERY` (differential) → `RESTORE LOG ... WITH NORECOVERY` (log1, íntegro) → `RESTORE LOG ... WITH STOPAT = '<punto medio>', RECOVERY` (log2, es donde ocurre el corte real).
8. Verificación: las etiquetas `antes-full`, `antes-diff` y `pedido-1` están presentes en el destino; `pedido-2` y `pedido-3` **no** aparecen.

**Resultado de ejecución real (2026-09-07/08, dos contenedores `mcr.microsoft.com/mssql/server:2022-latest` vía Testcontainers):**

```
dotnet test tests/Shared.Infrastructure.Persistence.Tests/Shared.Infrastructure.Persistence.Tests.csproj --filter "FullyQualifiedName~SqlPointInTimeRestoreIntegrationTests"

STOPAT elegido (entre pedido-1 y pedido-2): 2026-09-08 01:38:37.420
Etiquetas presentes tras el PITR: antes-full, antes-diff, pedido-1
Tiempo real de la secuencia de restore (full+differential+log1+log2 WITH STOPAT): 3466 ms

Pruebas totales: 1
     Correcto: 1
```

**Interpretación del tiempo medido:** 3466 ms corresponde a una base de datos de demostración (unas pocas filas, backups de pocos KB) — el tiempo real de un PITR productivo escala principalmente con el tamaño del `full`/`differential` restaurado (I/O de disco) y con la cantidad/tamaño de los backups de log a reproducir, no con esta medición aislada. El valor documentado aquí es evidencia de que la secuencia se **ejecutó realmente** (no fue simulada ni estimada) y del **orden de magnitud** para una base pequeña — no es un SLA de RTO para una base productiva de tamaño real, que debe medirse específicamente contra el volumen de datos de cada consumidor del framework.

**Verificación adicional (regresión):**
- `dotnet test tests/Shared.Infrastructure.Persistence.Tests/Shared.Infrastructure.Persistence.Tests.csproj` → 110/110 exitosas (109 preexistentes de F5-07 y tareas anteriores + 1 nueva de esta tarea).
- `dotnet build BitCode.Framework.slnx` → compilación correcta de toda la solución, 0 errores.

---

## 6. Pendientes explícitos (fuera de alcance de F5-08)

- **Inmutabilidad de los backups usados para el PITR (F5-09):** este runbook asume que los archivos `.bak`/`.trn` copiados al destino no fueron alterados/borrados entre su creación y su uso — hoy no hay ninguna protección WORM/checksum de cadena de custodia más allá de `WITH CHECKSUM` en la propia operación de `BACKUP` (F5-07). Brecha conocida, no encubierta.
- **Automatización del runbook como script ejecutable:** este documento es deliberadamente un procedimiento operado por un humano (con los `RESTORE` reales, no una herramienta que decida por sí sola qué backups aplicar) — no se entrega un script `Invoke-Pitr.ps1` que automatice la selección de backups de la sección 3.1, porque esa selección requiere criterio humano sobre el incidente concreto (qué transacción excluir, qué margen de seguridad tomar). Automatizarlo completamente sin supervisión sería peligroso ante un `STOPAT` mal calculado.
- **Medición de RTO productivo:** el tiempo medido en la sección 5 es de una base de demostración; no reemplaza un ejercicio de DR con el volumen de datos real de un consumidor concreto del framework.
- **Failover/failback automatizado (F5-10/F5-11):** este runbook es para recuperación puntual/forense, no para conmutación de tráfico productivo entre regiones.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — backlog F5-08.
- [`politica-backups-fase5.md`](politica-backups-fase5.md) — esquema de backups `full + differential + log` sobre el que opera este runbook (F5-07).
- [`replicacion-sql-fase5.md`](replicacion-sql-fase5.md) — replicación continua (F5-04), mecanismo complementario, no sustituto, de este runbook.
- [`tools/SqlBackupAutomation/`](../tools/SqlBackupAutomation/) — scripts reales de backup reutilizados por la prueba (sección 5).
- [`tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlPointInTimeRestoreIntegrationTests.cs`](../tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlPointInTimeRestoreIntegrationTests.cs) — prueba real que valida y mide el PITR (sección 5).
