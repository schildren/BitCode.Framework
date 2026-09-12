# Guía de Migraciones y Rollout — BitCode.Framework

**Tarea:** Fase 8 — Developer Experience y productización, tarea F8-07 (Migraciones: tooling de generación, validación y rollout) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md). Extendida en Fase 9 (F9-08, "Data migration": migrar y reconciliar datos — ver sección 8).  
**Fecha:** Septiembre de 2026.  
**Estado:** Aplicada con tooling y pruebas automatizadas contra SQL Server real (Testcontainers).

---

## 1. Propósito

El Plan Maestro (sección 11 y sección 13) establece que la persistencia en BitCode debe evolucionar de manera segura y sin interrupción del servicio (Zero-Downtime), aplicando el patrón **Expand-and-Contract** y prohibiendo operaciones destructivas sin autorización explícita:

> *"Las migraciones de base de datos deberán seguir expand-and-contract cuando exista despliegue sin downtime.*  
> *Un nuevo campo deberá ser opcional o tener default durante la transición.*  
> *La eliminación física ocurrirá después de confirmar que no existen consumidores."*

Esta guía documenta las convenciones, las reglas arquitectónicas y el uso de la herramienta CLI oficial **`BitCode.Migrations`** (`tools/BitCode.Migrations`) para validar, aplicar y revertir migraciones de Entity Framework Core de forma repetible y controlada.

---

## 2. El Patrón Expand-and-Contract en Persistencia

En entornos productivos de alta disponibilidad (Kubernetes con réplicas múltiples y `RollingUpdate`), las versiones nueva y vieja de una aplicación coexisten temporalmente recibiendo tráfico. Por lo tanto, el esquema de base de datos debe ser siempre compatible con **ambas** versiones simultáneamente.

```
+--------------------------------------------------------------------------------+
| PASO 1: EXPAND (Aditivo)                                                       |
| - Se agregan nuevas columnas (Nullable o con Default) o nuevas tablas.         |
| - Se aplica la migración a la base de datos (Job pre-deploy).                 |
| - La versión V1 (actual) de la app sigue funcionando sin enterarse del cambio. |
+--------------------------------------------------------------------------------+
                                       |
                                       v
+--------------------------------------------------------------------------------+
| PASO 2: TRANSITION (Rolling Update del Código)                                 |
| - Se despliegan gradualmente los pods de la versión V2.                        |
| - V2 comienza a escribir en las nuevas columnas/tablas.                        |
| - Si se necesita un rollback del software, V1 sigue siendo compatible.         |
+--------------------------------------------------------------------------------+
                                       |
                                       v
+--------------------------------------------------------------------------------+
| PASO 3: CONTRACT (Limpieza posterior — Requiere Aprobación)                    |
| - Una vez que V1 ya no existe en ningún pod y no hay consumidores viejos.      |
| - Se eliminan columnas/tablas obsoletas con autorización explícita.            |
+--------------------------------------------------------------------------------+
```

### Reglas Duras de Compatibilidad (Antipatrones Prohibidos en Despliegue Normal)

El validador de BitCode detecta y **bloquea** (Error) las siguientes operaciones en cualquier migración:

| Operación EF Core | Severidad | Razón del bloqueo | Remediación Expand-and-Contract |
|---|---|---|---|
| `DropTable` | **Error** | Destruye datos y rompe pods en versión anterior. | Desacoplar consumidores en release previa; eliminar solo en fase Contract con `--allow-destructive`. |
| `DropColumn` | **Error** | Las réplicas viejas que hagan `SELECT *` o lean la entidad fallarán. | Marcar propiedad como `[Obsolete]`, dejar de usarla en código, y retirar la columna en una versión posterior. |
| `RenameTable` | **Error** | La versión anterior buscará el nombre viejo y recibirá error SQL inmediato. | Crear nueva tabla (Expand), sincronizar datos, migrar lecturas/escrituras, luego retirar tabla vieja. |
| `RenameColumn` | **Error** | La versión anterior fallará al mapear la columna renombrada. | Agregar nueva columna (Expand), sincronizar datos, cambiar código en V2, luego retirar vieja en V3. |
| `AddColumn` (`NOT NULL` sin default) | **Error** | Las inserciones ejecutadas por réplicas viejas fallarán por violación de restricción `NOT NULL`. | Definir la columna como opcional (`nullable: true`) o suministrar un `DefaultValue` / `DefaultValueSql`. |
| `AddColumn` (`NOT NULL` con default) | **Warning** | Seguro para filas existentes, pero la versión vieja no enviará valor explícito. | Verificar que el valor default sea semánticamente inocuo durante el despliegue gradual. |
| `AlterColumn` | **Warning** | Riesgo de truncado de datos o incompatibilidad de tipos. | Verificar conversiones y compatibilidad binaria hacia atrás. |

---

## 3. Tooling CLI: `BitCode.Migrations`

La herramienta se encuentra en `tools/BitCode.Migrations` y se ejecuta mediante `dotnet run` o invocando el binario compilado.

### 3.1 Comandos Disponibles

#### A. `validate`
Analiza las clases de migración compiladas en un ensamblado sin conectarse a la base de datos, verificando que ninguna operación viole las reglas de Expand-and-Contract:

```powershell
dotnet run --project tools/BitCode.Migrations -- validate `
  --assembly "bin/Debug/net10.0/MiApp.Api.dll"
```

Si se requiere ejecutar una migración de fase *Contract* que contenga operaciones destructivas previamente aprobadas:
```powershell
dotnet run --project tools/BitCode.Migrations -- validate `
  --assembly "bin/Debug/net10.0/MiApp.Api.dll" `
  --allow-destructive
```

#### B. `status`
Inspecciona el estado de la base de datos comparando las migraciones registradas en `__EFMigrationsHistory` contra las migraciones definidas en el código:

```powershell
dotnet run --project tools/BitCode.Migrations -- status `
  --connection-string "Server=localhost;Database=MiDb;User Id=sa;Password=...;TrustServerCertificate=True" `
  --assembly "bin/Debug/net10.0/MiApp.Api.dll" `
  --context "MiAppDbContext"
```

#### C. `migrate` (Forward Rollout)
Ejecuta la validación de seguridad primero. Si no hay violaciones bloqueantes, aplica todas las migraciones pendientes (o hasta `--target` si se especifica):

```powershell
dotnet run --project tools/BitCode.Migrations -- migrate `
  --connection-string "Server=localhost;Database=MiDb;User Id=sa;Password=...;TrustServerCertificate=True" `
  --assembly "bin/Debug/net10.0/MiApp.Api.dll" `
  --context "MiAppDbContext"
```

#### D. `rollback` (Reversión Ensayada)
Revierte el esquema de base de datos hasta una migración específica anterior (o `0` para desaplicar todas):

```powershell
dotnet run --project tools/BitCode.Migrations -- rollback `
  --connection-string "Server=localhost;Database=MiDb;User Id=sa;Password=...;TrustServerCertificate=True" `
  --assembly "bin/Debug/net10.0/MiApp.Api.dll" `
  --context "MiAppDbContext" `
  --target "20260910000001_InitialMigration"
```

#### E. `script` (Generación de SQL Idempotente)
Genera el script SQL idempotente con guardas condicionales (`IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory]...)`), ideal para entregar a DBAs o incorporar en pipelines de aprobación externa:

```powershell
dotnet run --project tools/BitCode.Migrations -- script `
  --connection-string "Server=localhost;Database=MiDb;User Id=sa;Password=...;TrustServerCertificate=True" `
  --assembly "bin/Debug/net10.0/MiApp.Api.dll" `
  --context "MiAppDbContext" `
  --output "deploy/migrations/migrate-v2.sql"
```

---

## 4. Generación de Nuevas Migraciones

Para generar una nueva migración en un proyecto consumidor:

1. Modifique las entidades y la configuración del `DbContext`.
2. Ejecute la herramienta oficial de EF Core apuntando al proyecto API:
   ```powershell
   dotnet ef migrations add AgregarColumnaNotas `
     --project src/MiApp.Infrastructure `
     --startup-project src/MiApp.Api `
     --output-dir Migrations
   ```
3. Ejecute la validación con `BitCode.Migrations`:
   ```powershell
   dotnet run --project tools/BitCode.Migrations -- validate --assembly "src/MiApp.Api/bin/Debug/net10.0/MiApp.Api.dll"
   ```

---

## 5. Rollout en CI/CD y Kubernetes

En entornos productivos (Kubernetes), las migraciones **nunca** deben ejecutarse desde el `Program.cs` de cada pod de API (para evitar condiciones de carrera y bloqueos distribuidos entre múltiples réplicas concurrentes).

El flujo de despliegue recomendado es:

1. **Pipeline CI**:
   - `dotnet build`
   - `BitCode.Migrations validate`: falla el build si se introduce una operación destructiva no autorizada.
   - `dotnet test`: ejecuta pruebas unitarias y de integración.
2. **Pipeline CD (Despliegue)**:
   - Se ejecuta un **Kubernetes Job** independiente con `BitCode.Migrations migrate`.
   - El Job aplica las migraciones y finaliza con éxito.
   - Si el Job falla, el despliegue se detiene de inmediato sin tocar los Pods de la aplicación.
   - Una vez completado el Job, Kubernetes inicia el `RollingUpdate` del Deployment de los Pods (aplicación V2).

---

## 6. Runbook de Rollback ante Incidentes

Si la nueva versión de la aplicación (V2) presenta un fallo crítico durante el despliegue:

1. **Reversión de Cómputo (Pods)**:
   - Revierta el despliegue de Kubernetes a la versión anterior:
     ```bash
     kubectl rollout undo deployment/miapp-api
     ```
   - Gracias al patrón Expand-and-Contract, las réplicas V1 que vuelven a arrancar son 100 % compatibles con la base de datos (las nuevas columnas son opcionales o tienen defaults).
2. **Reversión de Base de Datos (Opcional / Controlada)**:
   - Si se decide que la base de datos también debe volver a su estado previo:
     ```powershell
     dotnet run --project tools/BitCode.Migrations -- rollback `
       --connection-string $CONNECTION_STRING `
       --assembly "MiApp.Api.dll" `
       --target "UltimaMigracionV1Valida"
     ```
   - Verifique mediante `status` que el nivel de migración actual coincide con el objetivo.

---

## 8. Reconciliación de Datos (Fase 9, F9-08 — "Data migration")

**Tarea:** Fase 9 — Capacidad de extracción de microservicios, tarea F9-08 (Data migration: migrar y reconciliar datos) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md). Entregable: "Herramienta". Criterio de aceptación: "Conteos y hashes conciliados".

### 8.1 Contexto y alcance real de esta tarea

El Plan Maestro fija Workflow como módulo piloto de extracción de microservicios (ver [ADR-0020](adr/0020-fase9-seleccion-piloto-extraccion-microservicio.md)). F9-03 ("Data ownership") ya confirmó con evidencia real (`WorkflowDataOwnershipIntegrationTests`) que `WorkflowDbContext` **nunca compartió base de datos con ningún otro módulo** — este framework separó el store de cada módulo de plataforma desde la Fase 6. Por lo tanto, F9-08 en este framework **no consiste en separar filas mezcladas en una base compartida** (ese trabajo no existe aquí), sino en construir la herramienta que sí sería necesaria en una extracción real: mover el store físico de un módulo (p. ej. Workflow) de un servidor SQL Server a otro sin downtime, o consolidar/particionar datos, y **demostrar con evidencia real** que el destino contiene exactamente lo mismo que el origen.

### 8.2 Comando `reconcile`

Se extendió el CLI existente `BitCode.Migrations` (en vez de crear una herramienta nueva) porque ya resuelve, de forma genérica por reflexión, cualquier `DbContext` de EF Core a partir de un ensamblado — el mismo mecanismo que usan `validate`/`status`/`migrate`/`rollback`/`script`. El comando `reconcile` compara, tabla por tabla, un origen y un destino que comparten el mismo modelo:

```powershell
dotnet run --project tools/BitCode.Migrations -- reconcile `
  --assembly "bin/Debug/net10.0/MiApp.Api.dll" `
  --context "WorkflowDbContext" `
  --source-connection-string "Server=origen;Database=Workflow;User Id=sa;Password=...;TrustServerCertificate=True" `
  --target-connection-string "Server=destino;Database=Workflow;User Id=sa;Password=...;TrustServerCertificate=True" `
  --tables "WorkflowDefiniciones,WorkflowVersiones,WorkflowStates,WorkflowTransitions,WorkflowInstances,WorkflowTasks,WorkflowHistoriales"
```

Devuelve código de salida `0` si todas las tablas quedan conciliadas y `1` si se detecta cualquier discrepancia (conteo o contenido). `--format json` emite el reporte estructurado para pipelines/auditoría. Omitir `--tables` reconcilia todas las tablas descubribles del modelo (incluidas `IdempotencyKey`/`OutboxMessage`/`InboxMessage`, configuradas automáticamente por `MultiTenantDbContext`); en un escenario real de migración de store conviene filtrarlas explícitamente si el Outbox/Inbox se maneja con su propia estrategia de relay en vez de copiarse fila por fila.

### 8.3 Qué garantiza y qué NO garantiza

Por cada tabla, el reporte incluye:

- **Conteo de filas** en origen y destino (`SELECT COUNT_BIG(*)`).
- **Hash de contenido agregado** (`SHA-256`), calculado como el hash acumulado *streaming* (sin materializar la tabla completa en memoria) de los hashes SHA-256 de cada fila individual, en el orden determinístico de la clave primaria.
- Si hay discrepancia, el **detalle de la primera fila divergente** (índice, valores de clave primaria, hash de fila en cada lado) — sin necesidad de un segundo paso de búsqueda, porque el hash por fila ya se calculó en el camino.

**Granularidad y su implicación:** el hash reportado por tabla es un resumen de tabla completa, no un hash por columna. Esto significa que el reporte confirma o refuta la igualdad de **toda la fila** de una vez; para saber *qué columna específica* cambió dentro de la fila divergente reportada, hay que inspeccionar esa fila puntual en ambos lados con una consulta manual — la herramienta ya redujo el problema de "¿alguna de N millones de filas difiere?" a "esta fila con esta clave difiere", pero no llega a nivel de columna.

**Limitaciones honestas:**

- **Sin aislamiento transaccional entre origen y destino.** Si hay escrituras concurrentes durante la reconciliación, puede reportarse una discrepancia falsa. Ejecutar en una ventana sin escritura (tras cortar tráfico hacia el store viejo) o con aislamiento snapshot.
- **Solo reconcilia entidades con clave primaria** (sin PK no hay orden determinístico posible) y **excluye tipos derivados de jerarquías TPH** para no duplicar el conteo de la tabla raíz. Ninguna de las siete entidades de negocio de `WorkflowDbContext` cae en ninguno de los dos casos.
- **Diseño específico de módulo, generalizable como trabajo futuro:** `DataReconciler` no está acoplado a `WorkflowDbContext` — descubre las tablas a partir del `IModel` de cualquier `DbContext`, así que el mismo comando sirve, sin cambios de código, para cualquier otro módulo del framework. Lo que queda **fuera de alcance** de esta tarea es reconciliar contra un esquema de destino ya transformado (columnas renombradas, particionado, tipos distintos) — hoy se asume que origen y destino comparten exactamente el mismo modelo.
- **El comando hereda la limitación de carga por reflexión de `validate`/`status`/`migrate` (F8-07):** apuntar `--assembly` a un ensamblado de host ASP.NET Core (que depende del *shared framework* `Microsoft.AspNetCore.App`) puede fallar al cargarse desde el proceso de consola del CLI, porque este último no hospeda ese *shared framework*. Funciona sin problemas apuntando a un ensamblado de biblioteca plano que contenga el `DbContext` (como se hace en la prueba de integración automatizada de esta tarea, que apunta directamente a `BitCode.Platform.Workflow.dll`).

### 8.4 Evidencia y pruebas automatizadas

`tests/BitCode.Migrations.Tests/Reconciliation/DataReconciliationIntegrationTests.cs` ejecuta contra **SQL Server real** (Testcontainers, dos bases de datos aisladas dentro del mismo contenedor simulando origen y destino):

- **Escenario conciliado:** puebla un origen de `WorkflowDbContext` con el grafo completo de las siete entidades de negocio (mismo patrón de datos que `WorkflowDataOwnershipIntegrationTests`, F9-03), lo copia al destino tabla por tabla con `SqlBulkCopy` (respetando el orden de dependencia de claves foráneas) y confirma que `DataReconciler` reporta `IsFullyReconciled = true`, con conteos y hashes iguales en las siete tablas.
- **Escenario con discrepancia:** repite la copia y luego altera deliberadamente el contenido de una fila en el destino (`UPDATE` sobre `WorkflowHistoriales.Detalle`) sin cambiar el conteo de filas. Confirma que la herramienta detecta la discrepancia exactamente en `WorkflowHistoriales` (`CountsMatch = true`, `HashesMatch = false`, `FirstMismatch` no nulo) sin producir falsos positivos en las otras seis tablas no alteradas — evidencia de que la comparación es de **contenido real**, no solo de cantidad.

---

## 9. Evidencia y Pruebas Automatizadas — Ciclo de Vida de Esquema (F8-07)

La suite automatizada en `tests/BitCode.Migrations.Tests` valida el comportamiento de punta a punta del ciclo de vida de migraciones de esquema (`validate`/`status`/`migrate`/`rollback`/`script`). La evidencia del comando `reconcile` (F9-08) se documenta en la sección 8.4:

- **`MigrationValidatorTests`**: 8 pruebas unitarias que verifican la detección estricta de `DropTable`, `DropColumn`, `RenameTable`, `RenameColumn` y `AddColumn` `NOT NULL` sin default, así como el modo permisivo `--allow-destructive`.
- **`MigrationRolloutIntegrationTests`**: Pruebas de integración ejecutadas contra **SQL Server real** (Testcontainers):
  - Forward inicial desde base limpia a V1.
  - Inserción y persistencia de datos de negocio.
  - Forward evolutivo (Expand) a V2 con datos preexistentes: comprobación de preservación de datos y compatibilidad de lecturas.
  - Rollback ensayado de V2 hacia V1: verificación de que el esquema revierte limpiamente, las columnas nuevas se retiran y las filas originales continúan intactas y legibles.
  - Generación de scripts SQL idempotentes listos para ejecución offline.
