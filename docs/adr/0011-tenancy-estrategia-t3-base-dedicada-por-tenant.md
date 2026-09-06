# 0011. Tenancy: contratos y operación de la estrategia T3 (base de datos dedicada por tenant)

**Estado:** Proposed
**Fecha:** 2026-09-06
**Responsable:** Pendiente de asignación

## Contexto

ADR 0003 aceptó T1 (base de datos compartida + filtro global, F1-12) como la estrategia multi-tenant
vigente del framework, dejando abiertas T2 (sharding por grupos de tenants, ADR 0010/F1-13) y T3
(base de datos dedicada por tenant) como alternativas para tenants con requisitos de aislamiento o
volumen que T1 no cubra. F1-14 (Épica F1-C del Plan Maestro) pide diseñar la estrategia T3, con
"Contratos y operación" como entregable y "Provisioning documentado" como criterio de aceptación — a
diferencia de F1-13 (que pedía explícitamente un prototipo ejecutable), el foco de F1-14 recae en
documentar cómo se **opera** T3 (provisioning, migraciones, baja, backup/restore), no en construir una
plataforma de gestión de tenants dedicados.

Un cambio del modelo multi-tenant y aprovisionar una base de datos nueva son ambos puntos de la
sección 13 del Plan Maestro que requieren aprobación humana explícita antes de ejecutarse contra
infraestructura real. Este ADR no cambia el modelo vigente (T1 sigue siendo, sin ningún cambio de
comportamiento, la única estrategia con implementación productiva real) ni aprovisiona ninguna base de
datos física: registra el diseño de un contrato adicional mínimo, un prototipo acotado del mecanismo
de migración masiva (demostrado con SQLite en memoria, no con SQL Server real), y la documentación
operativa completa de provisioning/baja/backup que el criterio de aceptación exige. Por eso queda en
`Proposed`, igual que ADR 0010.

## Decisión

### T3 es un caso particular de T2, no un modelo nuevo

**Decisión de diseño central:** T3 no introduce un `IShardResolver`/`IShardConnectionStringProvider`
nuevos. Se modela como el caso particular de T2 (ADR 0010) donde un `ShardId` es exclusivo de un único
tenant (cardinalidad 1) en vez de agrupar varios tenants. Un tenant en T3 tiene una fila en
`ITenantShardMapStore` (F1-13) cuyo `ShardId` no es compartido por ningún otro tenant, e
`IShardConnectionStringProvider` resuelve ese `ShardId` a la cadena de conexión de la base física
dedicada exactamente igual que resolvería la de un shard de grupo. Se evaluó definir un contrato
paralelo (`IDedicatedTenantConnectionStringProvider`, etc.) y se descartó: hubiera duplicado el mismo
contrato con una semántica idéntica (`tenantId`/`ShardId` → cadena de conexión), forzando a cada
consumidor a saber de antemano si un tenant está en T2 o T3 para elegir qué interfaz llamar. Reusar
`IShardResolver`/`IShardConnectionStringProvider` significa que el mismo código de la aplicación
(por ejemplo, una futura fábrica de `DbContextOptions` dinámica) sirve para T1, T2 y T3 sin
ramificarse por estrategia.

### Único contrato nuevo: `IDedicatedTenantDatabaseCatalog`

T2 (F1-13) nunca necesitó enumerar "todos los tenants con asignación explícita" porque su prototipo no
incluía ninguna operación en lote. T3 sí lo necesita: aplicar una migración de EF Core a todas las
bases dedicadas requiere poder listarlas. Se agrega:

- **`IDedicatedTenantDatabaseCatalog`** (`src/Shared.Domain/MultiTenancy/IDedicatedTenantDatabaseCatalog.cs`):
  `GetDedicatedTenantIdsAsync` devuelve el conjunto de tenants provisionados en T3. Combinado con
  `IShardResolver`/`IShardConnectionStringProvider` ya existentes, permite resolver la cadena de
  conexión de cada tenant dedicado sin necesitar un contrato de enumeración separado por estrategia.

`AddSharedPersistence` registra por defecto `InMemoryDedicatedTenantDatabaseCatalog` vacío
(`TryAddSingleton`): ningún tenant es T3 hasta que un proyecto lo registre explícitamente — mismo
patrón de "cero cambio de comportamiento" que los defaults de T2 (ADR 0010).

### Prototipo de migración masiva (código)

`DedicatedTenantDatabaseMigrator` (`Shared.Infrastructure.Persistence.MultiTenancy.Sharding`)
demuestra el mecanismo operativo: itera `IDedicatedTenantDatabaseCatalog`, resuelve el `ShardId` y la
cadena de conexión de cada tenant con los contratos ya existentes de T2, y aplica un delegado
`Func<string, CancellationToken, Task>` que el proyecto consumidor construye (normalmente:
`new MiAppDbContext(...).Database.MigrateAsync(...)`). Deliberadamente agnóstico del tipo de
`DbContext` concreto — Shared.Infrastructure.Persistence no puede depender de un `DbContext` de
negocio de un proyecto consumidor. Un fallo al migrar un tenant no detiene a los demás: cada resultado
queda en `DedicatedTenantMigrationReport` (éxito/fallo por tenant), para que el operador reintente solo
los fallidos.

`DedicatedTenantDatabaseMigratorTests` (`tests/Shared.Infrastructure.Persistence.Tests/Sharding/`)
prueba el mecanismo con SQLite en memoria (3 "bases dedicadas" simuladas, sin SQL Server real ni
Testcontainers): catálogo vacío da un reporte vacío exitoso; 3 tenants dedicados se migran cada uno
contra su propia base (verificado consultando cada base por separado tras la migración); y un tenant
con una cadena de conexión inválida falla sin impedir que los otros dos se migren correctamente.

### Provisioning de un tenant nuevo en T3 (documentación operativa pura — sin código)

Proceso operativo para dar de alta un tenant en T3 (ninguno de estos pasos tiene una implementación
productiva en este repositorio; es el procedimiento que un futuro runbook/script de operación debe
seguir):

1. **Crear la base de datos física dedicada.** Fuera del alcance de este ADR aprovisionar
   infraestructura real: en SQL Server, esto es una base nueva (o una instancia/servidor nuevo si el
   requisito de aislamiento del tenant es a nivel de servidor, no solo de base de datos) — decisión que
   cae bajo la sección 13 del Plan Maestro (nueva base de datos) y requiere aprobación humana explícita
   antes de ejecutarse contra un entorno real.
2. **Aplicar todas las migraciones de EF Core vigentes** contra la base recién creada, dejando su
   esquema al mismo nivel que el resto de las bases (T1 compartida y cualquier otro tenant T3 ya
   existente). Esto es exactamente lo que demuestra `DedicatedTenantDatabaseMigrator`: el operador
   registra el tenant nuevo en el catálogo antes de correr el job de migración masiva, o corre la
   migración puntual de ese único tenant si el runbook lo prefiere (`context.Database.MigrateAsync()`
   contra la única cadena de conexión nueva).
3. **Registrar la asignación tenant → shard** en `ITenantShardMapStore` (una fila con un `ShardId`
   exclusivo de ese tenant, no compartido) y **registrar el tenant en `IDedicatedTenantDatabaseCatalog`**
   (para que quede incluido en futuras migraciones masivas). Una implementación productiva de ambos
   contratos debe ser la misma tabla de control en SQL Server (ver "Consecuencias" de ADR 0010): un
   tenant T3 es, en esa tabla, una fila cuyo `ShardId` no aparece en ninguna otra fila.
4. **Registrar y proteger la cadena de conexión física** de la base nueva. Esta es la brecha más
   importante que este ADR señala explícitamente: el repositorio **no tiene todavía** un mecanismo de
   gestión de secretos/KMS (Fase 2 del Plan Maestro, y la elección de proveedor de secretos/KMS está en
   la lista de decisiones que requieren aprobación humana de la sección 13). Hasta que exista, una
   implementación productiva de `IShardConnectionStringProvider` para T3 no debe almacenar la cadena de
   conexión en texto plano en la misma tabla de control que el mapeo tenant→shard (aunque sea
   tentador, por conveniencia, guardar ambas cosas juntas): debe resolverla en tiempo de ejecución
   desde donde sea que la Fase 2 decida gestionar secretos, usando el `ShardId` únicamente como clave
   de búsqueda. Este ADR no resuelve esa brecha (haría falta una decisión de Fase 2 primero), solo deja
   constancia de que existe y de por qué no se cierra acá.
5. **Verificar el aislamiento** antes de dar el tenant por operativo: la misma clase de prueba que
   `MultiTenantDbContextIntegrationTests` (ADR 0003, T1) — acceso a un registro de otro tenant
   indistinguible de "no existe" — no aplica igual en T3 (una base físicamente separada no puede, por
   construcción, devolver datos de otro tenant en la misma consulta), pero sí conviene verificar que la
   aplicación resuelve la cadena de conexión correcta para ese tenant (un `IShardResolver` mal
   configurado podría, en teoría, enrutar accidentalmente a un tenant hacia la base de otro).

### Baja de un tenant de T3

Dar de baja o migrar un tenant fuera de T3 (por ejemplo, de vuelta a T1 si dejó de justificar una base
dedicada) es un proceso inverso y explícitamente fuera del alcance de este ADR implementarlo en
código: requiere (a) migrar los datos existentes del tenant desde su base dedicada hacia el destino
(T1 compartida u otro shard T2), (b) actualizar su fila en `ITenantShardMapStore` únicamente después de
confirmar que la migración de datos fue exitosa y completa (nunca antes — cambiar el mapeo antes de
migrar los datos dejaría al tenant "resolviendo" a un destino sin sus datos), (c) quitar su entrada de
`IDedicatedTenantDatabaseCatalog`, y (d) decidir cuándo es seguro decomisionar (no solo dejar de usar)
la base física original — normalmente después de un período de retención por si hace falta revertir la
migración. Cualquier migración de datos entre bases físicas reales, al ser una migración destructiva de
datos potencial, cae también bajo la sección 13 del Plan Maestro y requiere aprobación humana explícita.

### Backup/restore por tenant — ventaja operativa de T3 sobre T1/T2

A diferencia de T1 (donde un restore de la base compartida afecta a todos los tenants a la vez, o
requiere una extracción selectiva de filas por `TenantId` para restaurar solo uno) y de T2 (donde un
shard de grupo tiene el mismo problema para los tenants que comparten ese shard), T3 permite un backup y
un restore point-in-time por tenant individual usando exactamente las herramientas estándar de SQL
Server (backup de una única base de datos), sin necesitar ninguna lógica de extracción selectiva por
`TenantId`. Esta es la ventaja operativa concreta que justifica pagar el costo de aprovisionamiento y
mantenimiento por tenant de T3: tenants con requisitos regulatorios de recuperación puntual
independiente del resto de la plataforma son el caso de uso principal para elegir T3 sobre T1/T2.

## Alternativas consideradas

- **Definir un contrato de conexión/resolución específico de T3, separado de `IShardResolver`/
  `IShardConnectionStringProvider`:** descartado — ver la decisión de diseño central arriba. Hubiera
  duplicado semántica idéntica y obligado a ramificar el código consumidor por estrategia.
- **Guardar la cadena de conexión de cada tenant T3 en la misma tabla de control del mapeo tenant→shard
  (sin esperar a un mecanismo de secretos de Fase 2):** descartado como recomendación productiva —
  aunque sería el camino de menor esfuerzo para una implementación productiva inmediata, guarda un
  secreto (cadena de conexión con credenciales) junto a metadatos no sensibles, ampliando
  innecesariamente la superficie de exposición si esa tabla se filtra o se audita con menos rigor que
  un store de secretos dedicado. Se documenta como brecha abierta en vez de resolverse con una solución
  provisoria que después haya que migrar.
- **Implementar el migrador masivo directamente contra SQL Server con Testcontainers en vez de SQLite
  en memoria:** se descartó para el prototipo de F1-14 por ser desproporcionado al alcance pedido
  ("contratos y operación", no una implementación productiva); SQLite en memoria demuestra el mecanismo
  genérico (iterar + aplicar por conexión + reportar éxito/fallo por tenant) sin depender de Docker. Una
  implementación productiva real (contra bases SQL Server físicas) sí debería probarse con
  Testcontainers antes de aceptarse, siguiendo el precedente de ADR 0003/0010.

## Consecuencias

- T1 (ADR 0003) y T2 (ADR 0010) siguen sin cambios de comportamiento: los nuevos registros por defecto
  (`InMemoryDedicatedTenantDatabaseCatalog` vacío) son "cero cambio" para cualquier proyecto que no
  adopte T3 explícitamente.
- Ningún componente productivo del framework (`MultiTenantDbContext`, `AddSharedPersistence`) elige
  todavía dinámicamente una conexión física distinta por tenant basándose en
  `IShardResolver`/`IShardConnectionStringProvider` — esto sigue siendo, igual que en ADR 0010, una
  tarea de implementación de infraestructura posterior para cuando exista un tenant real que lo
  justifique.
- La brecha de gestión de secretos para cadenas de conexión de bases dedicadas queda explícitamente
  documentada como abierta y dependiente de una decisión de Fase 2 (elección de proveedor de
  secretos/KMS, sección 13 del Plan Maestro) — no se resuelve en este ADR ni con una solución
  provisoria.
- `InMemoryDedicatedTenantDatabaseCatalog` no es apta para producción multi-instancia, mismo motivo que
  `InMemoryTenantShardMapStore` (ADR 0010): una implementación productiva real debe respaldarse en la
  misma tabla de control en SQL Server que el mapeo tenant→shard.
- Este ADR no habilita, por sí mismo, ningún tenant real en T3: hacerlo requiere aprovisionar la
  infraestructura física, un `ITenantShardMapStore`/`IDedicatedTenantDatabaseCatalog` productivos, un
  mecanismo de gestión de secretos para la cadena de conexión, y (para dar de baja un tenant) una
  estrategia de migración de datos — todo eso cae bajo la sección 13 del Plan Maestro y requiere
  aprobación humana explícita antes de ejecutarse, igual que T1 (ADR 0003) y T2 (ADR 0010).

## Riesgos y mitigación

- **Riesgo:** que un futuro consumidor asuma que `DedicatedTenantDatabaseMigrator` ya migra bases SQL
  Server reales en producción. **Mitigación:** la documentación XML del tipo y este ADR dejan explícito
  que es un prototipo agnóstico del `DbContext` concreto, probado con SQLite en memoria, no con
  infraestructura real; el ADR queda en `Proposed`.
- **Riesgo:** una implementación productiva de `IShardConnectionStringProvider` para T3 guarde la
  cadena de conexión (con credenciales) en texto plano por no tener todavía un mecanismo de secretos.
  **Mitigación:** este ADR documenta la brecha explícitamente en la sección de provisioning (paso 4) en
  vez de dejarla implícita, para que no se resuelva "de paso" con una solución insegura al implementar
  T3 productivamente.
- **Riesgo:** una migración de datos al dar de baja un tenant de T3 dejando el `ITenantShardMapStore`
  actualizado antes de que la migración de datos haya terminado, causando que el tenant "pierda" sus
  datos desde la perspectiva de la aplicación. **Mitigación:** el orden documentado en "Baja de un
  tenant de T3" es explícito: actualizar el mapeo solo después de confirmar la migración completa, y
  cualquier migración de datos real requiere aprobación humana (sección 13 del Plan Maestro).
- Vinculado al registro de riesgos de F0-12 (pendiente de creación).
