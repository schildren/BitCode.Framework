# Guía de Migraciones y Rollout — BitCode.Framework

**Tarea:** Fase 8 — Developer Experience y productización, tarea F8-07 (Migraciones: tooling de generación, validación y rollout) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).  
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

## 7. Evidencia y Pruebas Automatizadas

La suite automatizada en `tests/BitCode.Migrations.Tests` valida el comportamiento de punta a punta:

- **`MigrationValidatorTests`**: 8 pruebas unitarias que verifican la detección estricta de `DropTable`, `DropColumn`, `RenameTable`, `RenameColumn` y `AddColumn` `NOT NULL` sin default, así como el modo permisivo `--allow-destructive`.
- **`MigrationRolloutIntegrationTests`**: Pruebas de integración ejecutadas contra **SQL Server real** (Testcontainers):
  - Forward inicial desde base limpia a V1.
  - Inserción y persistencia de datos de negocio.
  - Forward evolutivo (Expand) a V2 con datos preexistentes: comprobación de preservación de datos y compatibilidad de lecturas.
  - Rollback ensayado de V2 hacia V1: verificación de que el esquema revierte limpiamente, las columnas nuevas se retiran y las filas originales continúan intactas y legibles.
  - Generación de scripts SQL idempotentes listos para ejecución offline.
