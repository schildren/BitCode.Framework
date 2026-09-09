# Replicación SQL — Fase 5, Disaster Recovery y multi-región

**Tarea:** F5-04 (Fase 5 — Disaster Recovery y multi-región) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07
**Depende de:** F5-01 ([`docs/bia-fase5.md`](bia-fase5.md) — perfiles DR por componente), F5-02 ([`docs/mapa-ownership-regional.md`](mapa-ownership-regional.md) — un único propietario de escritura por tenant), F5-03 ([`docs/routing-regional-gateway.md`](routing-regional-gateway.md) — el tráfico llega a esa región propietaria).
**Estado:** Arquitectura de datos definida (topología recomendada para producción + modelo de consistencia + procedimiento de failover). RPO medido con una prueba real y reproducible que demuestra el mecanismo a menor escala (ver sección 4 y 5) — sin una topología de Always On Availability Groups real disponible en este entorno de un solo host de desarrollo (ver sección 4.1 para la justificación).

**Alcance:** este documento define, para `Shared.Infrastructure.Persistence` (SQL Server, el almacén transaccional del framework), (a) la topología de replicación recomendada para un despliegue productivo multi-región, (b) el modelo de consistencia resultante y su relación con los perfiles DR del BIA, y (c) el procedimiento de failover (quién decide, con qué información y en qué tiempo). No define la replicación del mapa de ownership tenant→región en sí (`ITenantRegionMapStore`, F5-02) más allá de que es un dato más que vive en la misma base de datos y por lo tanto queda cubierto por la misma topología — no es un mecanismo de replicación separado.

---

## 1. Topología recomendada para producción

### 1.1 Un único clúster multi-región: Always On Availability Groups con réplica secundaria asíncrona

Para el caso real de dos (o más) regiones que comparten el mismo clúster de Windows Server Failover Cluster (WSFC) o Pacemaker (Linux), la topología recomendada es:

- **Un Availability Group (AG) por base de datos (o grupo de bases de datos) que abarca ambas regiones.**
- **Réplica primaria** en la región propietaria de escritura activa hoy (coincide, por diseño, con la región que `IRegionalOwnershipResolver`/`ICurrentRegionProvider` resuelven como dueña — F5-02). Todas las escrituras van a la primaria.
- **Réplica(s) secundaria(s)** en la(s) otra(s) región(es), configuradas en modo de **disponibilidad asíncrona** (`AVAILABILITY_MODE = ASYNCHRONOUS_COMMIT`). La primaria confirma el commit al cliente sin esperar el ACK de la secundaria remota — la latencia de red entre regiones (típicamente decenas a cientos de milisegundos) no se suma a la latencia de cada transacción.
- **Listener de solo lectura** (`READ_ONLY_ROUTING`) opcional en la(s) secundaria(s) para servir lecturas locales de baja prioridad (reportes, consultas Gold/Standard que toleren estar unos segundos detrás) sin cruzar la red hacia la región propietaria — coherente con el principio "cómputo activo/activo, un único propietario de escritura" de la sección 2 del Plan Maestro: activo/activo es para cómputo y lectura, no para escritura del mismo dato.

### 1.2 Cuándo usar Distributed Availability Groups (DAG) en lugar de un único AG

Si las regiones NO comparten un mismo WSFC/Pacemaker (caso típico cuando cada región tiene su propio clúster de SQL Server, por ejemplo por aislamiento de red, distintos proveedores cloud, o distintas VNets sin una malla de dominio de clúster común), la topología recomendada es un **Distributed Availability Group**: un AG local en cada región (cada uno con su propio clúster y, opcionalmente, alta disponibilidad local con réplicas síncronas dentro de la región) unidos entre sí por un DAG que replica de forma asíncrona el AG primario de una región hacia el AG de la otra. Esto separa dos preocupaciones distintas:

- **HA local** (dentro de una región): réplicas síncronas para tolerar la caída de un nodo sin cambiar de región ni perder datos.
- **DR entre regiones**: el DAG replica asíncronamente hacia la otra región, con el mismo modelo de consistencia que 1.1.

**Recomendación:** empezar con 1.1 (un solo AG multi-región) mientras el número de nodos por región sea bajo (1-2); migrar a DAG cuando cada región necesite HA local propia con más de 2 réplicas — evita la complejidad operativa de dos niveles de AG sin necesidad demostrada.

### 1.3 Por qué no Always On síncrono ni replicación transaccional bidireccional entre regiones

- **Síncrono (`SYNCHRONOUS_COMMIT`) entre regiones** exige que cada commit espere el ACK de la secundaria remota — con decenas/cientos de ms de latencia de red entre regiones, esto degrada la latencia de escritura de todo el sistema a la peor latencia de red entre las dos regiones, para TODAS las transacciones, no solo las que en efecto necesitan ese nivel de garantía. El Plan Maestro es explícito en que la variante Platinum ("RPO cercano a cero") "requiere una decisión explícita sobre replicación síncrona, consenso y latencia" (`docs/bia-fase5.md` sección 1) — no se adopta por defecto.
- **Replicación transaccional bidireccional (multi-master)** introduce el problema de resolución de conflictos de escritura concurrente sobre el mismo dato desde dos regiones — exactamente lo que F5-02 ya evita por diseño al fijar un único propietario de escritura por tenant. Adoptar replicación bidireccional sería reintroducir, a nivel de infraestructura de datos, el problema que la capa de aplicación (`RegionalOwnershipBehavior`) ya decidió no tener. La topología de datos debe ser consistente con esa decisión: un solo escritor activo por AG (la primaria), nunca dos primarias escribiendo el mismo dato.

---

## 2. Modelo de consistencia resultante

| Perfil BIA (`docs/bia-fase5.md`) | RPO objetivo | Modo de disponibilidad AG | RPO real esperado |
|---|---|---|---|
| Standard | Hasta 15 minutos | Asíncrono | Sub-minuto en operación normal (el AG asíncrono real replica en segundos bajo carga normal); el margen de 15 minutos absorbe degradación de red entre regiones, no es el RPO típico. |
| Gold | Hasta 60 segundos | Asíncrono, con monitoreo activo del lag (`sys.dm_hadr_database_replica_states.log_send_queue_size` / `redo_queue_size`) y alerta si el lag supera un umbral (p. ej. 30s) | Segundos, gobernado por la latencia de red entre regiones y el volumen de log generado — **no-cero por diseño** (async). |
| Platinum | Cercano a cero | **Requiere decisión explícita adicional, no cubierta por este documento**: solo es alcanzable con `SYNCHRONOUS_COMMIT` entre regiones (con el costo de latencia de 1.3) o con un quórum de escritura de 2+ regiones (fuera del alcance de Always On AG estándar, requeriría un motor distribuido diferente). Ningún componente tiene hoy asignado Platinum sin esa decisión (`docs/bia-fase5.md` sección 1, aplicado también aquí). | N/A hasta que se tome esa decisión — sección 13 del Plan Maestro también exige aprobación humana para "SLA/RPO/RTO contractual", así que un compromiso Platinum contractual no se fija unilateralmente en este documento. |

**Conclusión de diseño:** la topología recomendada (1.1/1.2) cubre los perfiles Standard y Gold con RPO no-cero y acotado — alineado al texto literal del Plan Maestro ("async → RPO no-cero"). Platinum queda explícitamente fuera de esta topología por defecto; si en el futuro un dato real (p. ej. ledger de negocio de un consumidor) exige RPO cercano a cero, la decisión de habilitar `SYNCHRONOUS_COMMIT` (con su costo de latencia) debe tomarse explícitamente por ese caso puntual, documentando el trade-off, no aplicarse por defecto a todo el AG.

---

## 3. Procedimiento de failover

### 3.1 Failover manual (caso por defecto) — decisión humana, no automática

El Plan Maestro exige aprobación humana explícita para "failover/failback productivo" (sección 13) y F5-02 ya documentó esta misma restricción (`docs/mapa-ownership-regional.md` sección 4: "requiere aprobación humana explícita... este documento y este código solo dejan lista la capacidad de expresar y validar quién es el propietario vigente, no la de decidir un cambio de propietario en producción"). Este documento mantiene esa misma decisión para la capa de datos:

1. **Quién decide:** un operador humano con rol de guardia/on-call de infraestructura (o el responsable de DR designado), nunca un proceso automático sin supervisión, para el failover que **cambia la región propietaria de escritura** (failover "forzado"/planeado o failover ante desastre real).
2. **Con qué información:** el operador consulta, antes de decidir, (a) el estado de sincronización del AG (`sys.dm_hadr_database_replica_states`, específicamente `synchronization_health`, `log_send_queue_size`), (b) si la primaria está realmente caída o solo inalcanzable desde el monitor (para evitar un failover innecesario que cause split-brain), y (c) el RPO estimado de aceptar la secundaria como nueva primaria (cuánto log no confirmado se perdería).
3. **En qué tiempo:** el procedimiento manual (failover del AG + actualización de `ITenantRegionMapStore`/`ConfigurationTenantRegionMapStore` de F5-02/F5-03 para apuntar el tráfico a la nueva región) debe ejecutarse dentro del RTO del perfil correspondiente (Gold: 5 minutos, Standard: 60 minutos) — esto es un objetivo de runbook a validar operacionalmente (Fase 5 posterior, F5-08/F5-09 de ensayos de conmutación), no algo que este documento por sí solo garantice.
4. **Orden de operaciones (evita split-brain):** (a) confirmar que la primaria original está efectivamente inaccesible o se decide degradarla deliberadamente, (b) forzar el failover del AG hacia la secundaria (`ALTER AVAILABILITY GROUP ... FAILOVER` en modo forzado, aceptando la posible pérdida de las transacciones no replicadas — el RPO reflejado en la sección 2), (c) recién después de que la nueva primaria esté aceptando escrituras, actualizar el mapa de ownership regional (F5-02) para que `RegionalOwnershipBehavior`/`RegionalOwnershipRoutingMiddleware` (F5-03) empiecen a aceptar tráfico de escritura en la nueva región — nunca al revés, para no enrutar escrituras hacia una región cuya base de datos todavía no es primaria.

### 3.2 Failover automático — explícitamente NO adoptado por defecto

Un failover automático (el clúster decide solo, sin intervención humana, cuándo promover la secundaria) se descarta como comportamiento por defecto porque:

- Con `ASYNCHRONOUS_COMMIT` entre regiones, un failover automático ante una partición de red transitoria (no una caída real de la primaria) causaría una pérdida de datos evitable y, peor, dejaría dos primarias activas simultáneamente (split-brain) si la región original se recupera antes de que el DNS/routing (F5-03) termine de apuntar a la nueva región.
- Coincide con la restricción de sección 13 del Plan Maestro y con la misma decisión ya tomada en F5-02/F5-03 para el resto de la topología de ownership regional.

Un mecanismo semiautomático (alerta automática + decisión humana con runbook pre-escrito, en vez de decisión y ejecución 100% manuales) es una optimización operativa válida para reducir el tiempo de decisión dentro del RTO, pero la ejecución del cambio de propietario de escritura en producción sigue requiriendo la aprobación humana de 3.1.

---

## 4. Por qué esta prueba usa log shipping en vez de Always On AG real

### 4.1 Limitación real del entorno (no una elección de conveniencia)

Always On Availability Groups exige un WSFC (Windows) o Pacemaker (Linux) — un clúster de failover con quórum entre nodos — como requisito de infraestructura subyacente. Esto no es algo que Testcontainers pueda levantar de forma aislada: cada contenedor `Testcontainers.MsSql` es una instancia SQL Server standalone en su propio namespace de red/proceso, sin la capa de clustering del sistema operativo que Always On necesita para el mecanismo de quórum y detección de fallos. Levantar un WSFC/Pacemaker real entre contenedores Docker en un único host de desarrollo excede la capacidad de este entorno (requeriría, como mínimo, un dominio Active Directory o un clúster sin dominio con múltiples nodos de red enrutable entre sí con roles de clúster, fuera del alcance de una prueba automatizada de una sola ejecución de `dotnet test`).

### 4.2 Mecanismo elegido: log shipping real entre dos instancias SQL Server independientes

En su lugar, la prueba (`tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlLogShippingRpoIntegrationTests.cs`) demuestra el mismo principio de replicación asíncrona basada en el log de transacciones — el mecanismo real y productivo de **SQL Server Transaction Log Shipping** — contra **dos contenedores SQL Server completamente independientes** (dos procesos, dos discos, sin filesystem compartido):

1. **Backup completo inicial** de la base "primaria" (`BACKUP DATABASE ... WITH INIT`), con la base en `RECOVERY FULL` (requisito para poder tomar backups de log).
2. **Copia real del archivo de backup** entre los dos contenedores (`ReadFileAsync`/`CopyAsync` de Testcontainers — una copia de bytes entre dos sistemas de archivos distintos, análoga a la copia entre regiones que haría un job de log shipping real vía carpeta compartida/FTP/blob storage).
3. **Restore inicial en la "réplica"** con `WITH NORECOVERY` (la base queda a la espera de backups de log sucesivos — el estado real en que queda la base secundaria en log shipping productivo).
4. **Carga simulada**: inserts continuos y confirmados en la primaria durante la prueba (transacciones reales, cada una con marca de tiempo `UTC`).
5. **Ciclo periódico de log shipping real**: cada `N` segundos, `BACKUP LOG` en la primaria → copia real del archivo `.trn` al otro contenedor → `RESTORE LOG ... WITH STANDBY` en la réplica (el mismo modo que usa el log shipping clásico de SQL Server para dejar la secundaria consultable en modo solo-lectura entre restores sucesivos).
6. **Medición real del RPO**: se simula una "caída" de la primaria deteniendo la carga y el ciclo de log shipping en el mismo instante (sin backup de log final "de cola" — exactamente lo que ocurre en una caída real donde el tail del log no llega a copiarse) y se calcula `RPO = timestamp del último insert confirmado en la primaria − timestamp del último dato ya presente en la réplica tras el último ciclo de log shipping completado`.

Log shipping es, igual que Always On AG asíncrono, un mecanismo de **replicación basada en el log de transacciones con un modelo de consistencia asíncrono** — la diferencia con Always On AG es únicamente la granularidad temporal (Always On AG envía el log casi continuamente en segundo plano; log shipping lo hace en lotes periódicos programados) y el requisito de infraestructura (log shipping no necesita WSFC/Pacemaker, solo SQL Server Agent + una carpeta accesible desde ambos servidores). Por eso es una demostración fiel y ejecutable del mismo fenómeno que se mide en producción con Always On AG (`log_send_queue_size`/lag de redo): una ventana de datos confirmados en la primaria que todavía no llegaron a la secundaria.

### 4.3 Qué NO demuestra esta prueba (explícitamente fuera de alcance)

- **No demuestra failover automático de un AG** (no hay AG real) — el procedimiento de failover de la sección 3 sigue siendo el mismo independientemente del mecanismo de replicación subyacente (log shipping o Always On AG), pero esta prueba no ejercita el comando `ALTER AVAILABILITY GROUP ... FAILOVER`.
- **No mide el RPO de un Always On AG real** con latencia de red inter-región real (los dos contenedores corren en el mismo host de desarrollo, sin latencia de red significativa entre ellos) — el número medido en la sección 5 es válido como demostración del mecanismo y de que el RPO es "no-cero pero acotado por el intervalo configurado", no como una predicción del RPO exacto de un despliegue productivo real (que dependerá del RTO/lag real de red entre las regiones elegidas, a validar cuando exista esa topología — ver sección 6).

---

## 5. Verificación — RPO medido

**Prueba:** `dotnet test tests/Shared.Infrastructure.Persistence.Tests/Shared.Infrastructure.Persistence.Tests.csproj --filter "FullyQualifiedName~SqlLogShippingRpoIntegrationTests"`

**Resultado (ejecución real, 2026-09-07/08, dos contenedores `mcr.microsoft.com/mssql/server` vía Testcontainers, carga de 6 segundos con inserts cada 100ms, ciclo de log shipping cada 1.5 segundos):**

```
Último insert confirmado en la primaria (momento de la caída simulada): 2026-09-08T00:18:43.3733546Z
Último dato confirmado en la réplica (último restore de log aplicado):  2026-09-08T00:18:41.2270000
Ciclos de log shipping ejecutados: 2
Intervalo de log shipping configurado: 00:00:01.5000000
RPO medido (dato confirmado y perdido = disaster - último dato replicado): 00:00:02.1463546

Pruebas totales: 1
     Correcto: 1
```

Ejecutada nuevamente para confirmar reproducibilidad (no flaky): segunda corrida también `Correcto: 1` (RPO medido en un rango similar, gobernado por el mismo intervalo configurado).

La prueba afirma dos propiedades sobre el RPO medido (no solo lo imprime):

1. **RPO > 0** — la réplica, en el momento de la caída, no tiene el último dato confirmado en la primaria (prueba que el mecanismo es asíncrono, consistente con Standard/Gold, nunca "RPO cero" sin la decisión explícita de síncrona que exige Platinum).
2. **RPO acotado** — el RPO medido es menor que 3× el intervalo de log shipping configurado (prueba que el atraso es predecible y gobernado por la frecuencia del ciclo de replicación, no arbitrariamente grande).

**Verificación adicional:**
- `dotnet test tests/Shared.Infrastructure.Persistence.Tests/Shared.Infrastructure.Persistence.Tests.csproj` — 107/107 exitosas (sin regresión sobre las pruebas de integración/unitarias existentes del proyecto, incluidas las de Outbox, Inbox, multi-tenancy y transacciones que también usan SQL Server real vía Testcontainers).
- `dotnet build BitCode.Framework.slnx` — compilación correcta de toda la solución, sin errores nuevos.

---

## 6. Pendientes explícitos (fuera de alcance de F5-04, entregables de tareas posteriores o de infraestructura real)

- Configurar y validar un Always On Availability Group (o Distributed AG) real cuando exista una topología multi-región real con WSFC/Pacemaker disponible — esta tarea define la topología recomendada (sección 1) y demuestra el modelo de consistencia con un mecanismo equivalente ejecutable (log shipping, sección 4), pero no reemplaza la validación contra la topología productiva real.
- Automatizar el runbook de failover manual (sección 3.1) como procedimiento documentado paso a paso con checklist, y ensayarlo con una conmutación real — corresponde a F5-08/F5-09 (backlog de Fase 5, pruebas de conmutación y runbooks) cuando exista la topología real.
- Definir el umbral de alerta de lag de replicación (`log_send_queue_size`/`redo_queue_size` para Always On AG, o el tiempo transcurrido desde el último log shipping aplicado para log shipping) y su integración con observabilidad (Fase 4, ya cerrada) — no se fija un valor numérico contractual aquí porque el SLA/RPO/RTO contractual requiere aprobación humana explícita (sección 13 del Plan Maestro).
- Decisión explícita de replicación síncrona para el perfil Platinum (registro de auditoría, `docs/bia-fase5.md` sección 4 punto 4), incluyendo el trade-off de latencia — pendiente de negocio/infraestructura, no se resuelve por defecto en este documento.
- Confirmar con negocio/infraestructura cuántas regiones, con qué SLA de red entre ellas, antes de calibrar el intervalo de log shipping (o el umbral de alerta de Always On AG) a un valor productivo (el intervalo de 1.5s usado en la prueba es solo para que la prueba sea ejecutable en segundos, no una recomendación productiva).

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — sección 2 (decisión arquitectónica rectora de multi-región), Fase 5 (backlog F5-01 a F5-09), sección 13 (aprobaciones humanas).
- [`bia-fase5.md`](bia-fase5.md) — perfiles DR por componente (F5-01), definición de Standard/Gold/Platinum.
- [`mapa-ownership-regional.md`](mapa-ownership-regional.md) — un único propietario de escritura por tenant (F5-02), consumido por el procedimiento de failover de la sección 3.
- [`routing-regional-gateway.md`](routing-regional-gateway.md) — routing regional (F5-03), consumido por el paso final del procedimiento de failover de la sección 3.
- [`tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlLogShippingRpoIntegrationTests.cs`](../tests/Shared.Infrastructure.Persistence.Tests/Integration/SqlLogShippingRpoIntegrationTests.cs) — prueba real que mide el RPO (sección 5).
