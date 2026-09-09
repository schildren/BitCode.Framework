# Catálogo de eventos de integración — BitCode.Framework

**Tarea:** F3-12 (Fase 3 — Plataforma de eventos) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07. **Actualizado:** 2026-09-09 (Fase 6, módulo 4 — Feature Management, agrega
`FeatureManagement.FeatureFlagActivado`, `FeatureManagement.FeatureFlagDesactivado` y
`FeatureManagement.RolloutIniciado`; ver la sección "Eventos productivos registrados" más abajo).
Actualizado previamente el mismo día (Fase 6, módulo 3 — Catalogs and Parameters, agrega
`Catalogos.CatalogoVersionPublicada` y `Catalogos.ParametroVigenciaCreada`). Actualizado previamente el
2026-09-08 (Fase 6, módulo 2 — Organization, primer registro productivo real).
**Estado:** Aplicado como PROCESO y PLANTILLA, con sus primeras ocho filas productivas reales
(`Organizacion.EmpresaCreada`, `Organizacion.EmpresaDesactivada`, `Organizacion.SucursalCreada`,
`Catalogos.CatalogoVersionPublicada`, `Catalogos.ParametroVigenciaCreada`,
`FeatureManagement.FeatureFlagActivado`, `FeatureManagement.FeatureFlagDesactivado`,
`FeatureManagement.RolloutIniciado`) -- ver la sección "Por qué el catálogo está vacío hoy" para el
contexto histórico de por qué el catálogo empezó vacío en F3-12.

Este documento es el registro único de todo `IIntegrationEvent` (`Shared.Application.Eventing`, F3-01)
que un bounded context publica o consume en producción: quién es su dueño, qué versión de esquema
tiene, si transporta PII, quién lo consume y con qué clave de partición viaja. No duplica la
documentación de mecanismo ya escrita en `docs/guia-eventing-contratos.md`,
`docs/politica-versionado.md` (sección 5), `docs/guia-observabilidad-eventos.md`,
`docs/politica-seguridad-kafka.md` ni `docs/runbook-dlq.md` — este documento es el inventario de
INSTANCIAS concretas de eventos de negocio, no el mecanismo genérico que ya describen esos documentos.

## Por qué el catálogo está vacío hoy

A la fecha de esta tarea (F3-12), el repositorio no tiene ningún módulo de negocio real: toda la Fase 3
(F3-01 a F3-11) construyó el MECANISMO de eventing (contratos, adapter Kafka, Outbox/Inbox,
particionamiento, compatibilidad de esquema, reintentos, DLQ, poison messages, observabilidad,
seguridad de transporte) sin que exista todavía ningún bounded context de negocio que declare un
`IIntegrationEvent` productivo. Los únicos tipos que implementan `IIntegrationEvent` hoy en el
repositorio son de EJEMPLO/test:

- `tests/Shared.Application.Tests/Eventing/TestIntegrationEvents.cs` (`TestOrderCreatedIntegrationEvent`,
  `TestOrderCreatedV2IntegrationEvent`, y variantes usadas por `EventSchemaCompatibilityChecker`, F3-06).
- `tests/Shared.Infrastructure.Messaging.Kafka.Tests/TestPartitionedIntegrationEvent.cs` y otros tipos
  de test del adapter Kafka (F3-02/F3-05).
- `samples/Sample.Eventing/Pedidos/PedidoConfirmadoIntegrationEvent.cs`
  (`Pedidos.PedidoConfirmado`) — la prueba de referencia de F3-13 (Fase 3, cierre): un ejemplo
  EJECUTABLE de dos módulos (Pedidos, Módulo A / Facturación, Módulo B) comunicándose por este evento
  de punta a punta (`samples/Sample.Eventing.Tests/EndToEndEventingReferenceTests.cs`), pero
  `samples/` sigue siendo, igual que `Sample.Api`, un proyecto piloto/demo del propio repositorio de
  framework, no un bounded context de negocio consumidor real — mismo motivo por el que
  `Sample.Api.Productos.Producto` tampoco figura en ningún catálogo productivo equivalente de la Fase
  2/Auditoría. Ver `docs/fase-3-plataforma-eventos.md` para el detalle de la prueba de referencia.

Ninguno de ellos se publica nunca contra un broker productivo; existen solo para probar el mecanismo.
Por eso el criterio de aceptación literal de F3-12 ("Todo evento productivo registrado") se cumple hoy
de forma vacua: no hay ningún evento productivo sin registrar, porque no hay ningún evento productivo.
El entregable real de esta tarea no es "llenar filas" (no hay nada que llenar todavía) sino dejar (a)
la ESTRUCTURA/plantilla lista, (b) un EJEMPLO ilustrativo usando uno de los eventos de test ya
existentes (marcado explícitamente como ejemplo, nunca como entrada real), y (c) el PROCESO obligatorio
para que el primer bounded context de negocio real (Fase 6, "Plataforma funcional empresarial") registre
su primer evento productivo como parte de su propia Definition of Done — ver la regla dura 27 en
`docs/convenciones.md`.

## Plantilla / columnas

| Columna | Significado |
|---|---|
| **Nombre lógico (`EventType`)** | Valor exacto de `IIntegrationEvent.EventType` — el identificador estable usado para enrutamiento/nombre de tópico (`IKafkaTopicNameResolver`, F3-02). Es la clave primaria del catálogo: dos entradas con el mismo `EventType` describen distintas versiones del MISMO evento, no dos eventos distintos. |
| **Tipo .NET / proyecto** | Tipo concreto (`record`) que implementa `IIntegrationEvent`, y el proyecto (`.csproj`) del bounded context donde vive — nunca `Shared.*` (esos son solo el mecanismo genérico). |
| **`SchemaVersion` actual** | Valor vigente de `IIntegrationEvent.SchemaVersion` para la versión activa. Si conviven varias versiones dentro de la ventana de coexistencia (`docs/politica-versionado.md`, sección 5), se listan todas como filas separadas del mismo `EventType`. |
| **Owner** | Bounded context / equipo responsable de decidir la forma del payload y de aprobar cambios de esquema — un nombre de rol/equipo, no una persona individual (mismo criterio que `docs/catalogo-slo-sla.md`). |
| **Consumidores conocidos** | Qué otro(s) bounded context(s) implementan `IEventConsumer<TEvent>` para este evento, y con qué propósito de negocio. Un evento sin ningún consumidor conocido todavía sigue siendo válido de publicar (otros bounded contexts pueden suscribirse después), pero debe declararlo explícitamente como `(ninguno conocido aún)` — nunca dejar la celda vacía sin aclaración. |
| **PII (sí/no + campos)** | Si el payload transporta algún dato personal. Usa la misma taxonomía que `AuditRedactionOptions.SensitiveMetadataKeys` (F2-19, `docs/guia-auditoria-inmutable.md`) para consistencia de vocabulario en todo el repositorio: identificador directo (nombre, DNI/CUIT/CUIL, email, teléfono), dato financiero (tarjeta/cuenta), credencial/secreto (nunca debería viajar en un evento — si aparece, es un hallazgo de seguridad, no una entrada normal del catálogo). Si "sí", lista los campos concretos del payload. |
| **Tópico Kafka** | Nombre de tópico resuelto por `IKafkaTopicNameResolver` (`DefaultKafkaTopicNameResolver`, F3-02) a partir del `EventType` — hoy es el `EventType` tal cual con caracteres no válidos reemplazados por `_`, salvo que el proyecto registre un resolver custom. |
| **`PartitionKey`** | Si el evento implementa `IHasPartitionKey` (F3-05): qué campo se usa (`AggregateId` o `TenantId`, ver `docs/guia-eventing-contratos.md`, sección "Particionamiento") y por qué. Si no la implementa: `(sin partición explícita — Key = EventId, sin garantía de orden)`. |
| **Alta / última modificación** | Fecha (`AAAA-MM-DD`) de la primera publicación real y de la última vez que cambió `SchemaVersion` o cualquier otra columna de esta fila. |

## Ejemplo ilustrativo (NO es un evento productivo)

La fila siguiente muestra cómo se completa la plantilla, usando `TestOrderCreatedIntegrationEvent` /
`TestOrderCreatedV2IntegrationEvent` (`tests/Shared.Application.Tests/Eventing/TestIntegrationEvents.cs`)
como caso de muestra. **Este evento nunca se publica contra ningún broker productivo — es un tipo de
test usado por `EventSchemaCompatibilityTests`/`KafkaEventPublisherConsumerIntegrationTests`.** Se
incluye únicamente para dejar claro el formato esperado de una fila real.

| Nombre lógico (`EventType`) | Tipo .NET / proyecto | `SchemaVersion` | Owner | Consumidores conocidos | PII | Tópico Kafka | `PartitionKey` | Alta / última modificación |
|---|---|---|---|---|---|---|---|---|
| `Pedidos.PedidoCreado` *(EJEMPLO — no productivo)* | `TestOrderCreatedIntegrationEvent` — `tests/Shared.Application.Tests` (proyecto de test, no un bounded context real) | 1 (vigente) — `TestOrderCreatedV2IntegrationEvent` es un ejemplo adicional de evolución NO compatible a `SchemaVersion = 2`, agrega `Total` no nullable | *(ejemplo — sería "Pedidos", el bounded context ficticio del que este tipo simula formar parte)* | *(ejemplo — sería, por ejemplo, "Facturación": genera una factura preliminar al recibir el pedido)* | Sí — `CustomerName` (identificador directo, nombre de cliente) | `Pedidos.PedidoCreado` (resuelto tal cual por `DefaultKafkaTopicNameResolver`, sin caracteres a reemplazar) | `(sin partición explícita — Key = EventId)`. Ver `TestOrderCreatedWithAggregateIdPartitionKeyIntegrationEvent`/`TestOrderCreatedWithTenantIdPartitionKeyIntegrationEvent` en el mismo archivo para los dos ejemplos de `IHasPartitionKey` (por `OrderId` y por `TenantId` respectivamente) | *(ejemplo — no aplica fecha real)* |

## Eventos productivos registrados

Primeras filas reales del catálogo, dadas de alta por Organization (Fase 6, módulo 2): `Empresa` y
`Sucursal` son `AggregateRoot<TId>` propios del módulo (a diferencia de Identity Administration, módulo
1, que reutilizaba tipos ajenos de Security 2.0 sin poder levantar eventos de dominio reales), así que
sus operaciones de negocio levantan estos eventos vía `RaiseDomainEvent`, y `OutboxSaveChangesInterceptor`
(F1-23) los persiste atómicamente junto con el cambio de negocio en `OrganizationDbContext`. Ningún host
de referencia del repositorio (`samples/Sample.Organization.Api`) los publica hoy contra un broker Kafka
productivo real (no registra `AddSharedKafkaEventing`) — quedan en la tabla `OutboxMessage`, sin relay
activo; el mecanismo de publicación en sí (F3-02/F3-03) ya está probado de punta a punta por
`Sample.Eventing`, F3-13, así que no es un pendiente de esta tarea, ver `docs/guia-organization.md`,
sección "Pendientes".

Catalogs and Parameters (Fase 6, módulo 3) agrega las dos filas siguientes, mismo criterio:
`CatalogoVersion` y `ParametroVigencia` son `AggregateRoot<Guid>` propios del módulo, así que sus
operaciones de negocio (`Publicar`/el constructor de alta respectivamente) levantan estos eventos vía
`RaiseDomainEvent`, persistidos atómicamente en `CatalogsDbContext` por el mismo mecanismo de Outbox.
Tampoco publicados hoy contra un broker productivo real (`samples/Sample.Catalogs.Api` no registra
`AddSharedKafkaEventing`), ver `docs/guia-catalogs.md`, sección "Pendientes".

Feature Management (Fase 6, módulo 4) agrega las tres filas siguientes, mismo criterio: `FeatureFlag` y
`Rollout` son `AggregateRoot<Guid>` propios del módulo, así que sus operaciones de negocio
(`Activar`/`Desactivar`/el constructor de alta respectivamente) levantan estos eventos vía
`RaiseDomainEvent`, persistidos atómicamente en `FeatureManagementDbContext` por el mismo mecanismo de
Outbox. Tampoco publicados hoy contra un broker productivo real (`samples/Sample.FeatureManagement.Api`
no registra `AddSharedKafkaEventing`), ver `docs/guia-feature-management.md`, sección "Pendientes".

| Nombre lógico (`EventType`) | Tipo .NET / proyecto | `SchemaVersion` | Owner | Consumidores conocidos | PII | Tópico Kafka | `PartitionKey` | Alta / última modificación |
|---|---|---|---|---|---|---|---|---|
| `Organizacion.EmpresaCreada` | `EmpresaCreadaIntegrationEvent` — `src/Platform/BitCode.Platform.Organization` (`Empresas/EmpresaCreadaIntegrationEvent.cs`) | 1 (vigente) | Organization (Fase 6, módulo 2) | `(ninguno conocido aún)` | No — `RazonSocial`/`Identificador` son datos de la persona jurídica (empresa), no de una persona física; no hay identificador directo de individuo en el payload | `Organizacion.EmpresaCreada` (resuelto tal cual por `DefaultKafkaTopicNameResolver`, sin caracteres a reemplazar) | `EmpresaId` (`IHasPartitionKey`) — todos los eventos de una misma empresa quedan en la misma partición | 2026-09-08 |
| `Organizacion.EmpresaDesactivada` | `EmpresaDesactivadaIntegrationEvent` — `src/Platform/BitCode.Platform.Organization` (`Empresas/EmpresaDesactivadaIntegrationEvent.cs`) | 1 (vigente) | Organization (Fase 6, módulo 2) | `(ninguno conocido aún)` | No | `Organizacion.EmpresaDesactivada` | `EmpresaId` (`IHasPartitionKey`) | 2026-09-08 |
| `Organizacion.SucursalCreada` | `SucursalCreadaIntegrationEvent` — `src/Platform/BitCode.Platform.Organization` (`Sucursales/SucursalCreadaIntegrationEvent.cs`) | 1 (vigente) | Organization (Fase 6, módulo 2) | `(ninguno conocido aún)` | No — `Nombre`/`Direccion` son datos de un establecimiento comercial, no de una persona física | `Organizacion.SucursalCreada` | `SucursalId` (`IHasPartitionKey`) | 2026-09-08 |
| `Catalogos.CatalogoVersionPublicada` | `CatalogoVersionPublicadaIntegrationEvent` — `src/Platform/BitCode.Platform.Catalogs` (`Catalogos/CatalogoVersionPublicadaIntegrationEvent.cs`) | 1 (vigente) | Catalogs and Parameters (Fase 6, módulo 3) | `(ninguno conocido aún)` — un consumidor típico sería un módulo que cachea los ítems de un catálogo y necesita invalidar su copia al publicarse una versión nueva | No — el payload solo transporta identificadores (`CatalogoVersionId`/`CatalogoId`), número de versión y fechas de vigencia, sin ningún dato de persona física | `Catalogos.CatalogoVersionPublicada` (resuelto tal cual por `DefaultKafkaTopicNameResolver`, sin caracteres a reemplazar) | `CatalogoId` (`IHasPartitionKey`) — todos los eventos de publicación de un mismo catálogo quedan en la misma partición | 2026-09-09 |
| `Catalogos.ParametroVigenciaCreada` | `ParametroVigenciaCreadaIntegrationEvent` — `src/Platform/BitCode.Platform.Catalogs` (`Parametros/ParametroVigenciaCreadaIntegrationEvent.cs`) | 1 (vigente) | Catalogs and Parameters (Fase 6, módulo 3) | `(ninguno conocido aún)` — un consumidor típico sería un módulo que cachea el valor vigente de un parámetro y necesita invalidarlo al darse de alta una vigencia nueva | Depende del parámetro — el payload incluye `Valor` como `string` libre; si un consumidor real modela un parámetro cuyo valor es un dato personal (poco frecuente, pero posible), debe reclasificar esta fila explícitamente. Para los parámetros de ejemplo del módulo (tasas, límites) no hay PII | `Catalogos.ParametroVigenciaCreada` | `ParametroId` (`IHasPartitionKey`) — todas las vigencias de un mismo parámetro quedan en la misma partición | 2026-09-09 |
| `FeatureManagement.FeatureFlagActivado` | `FeatureFlagActivadoIntegrationEvent` — `src/Platform/BitCode.Platform.FeatureManagement` (`Flags/FeatureFlagActivadoIntegrationEvent.cs`) | 1 (vigente) | Feature Management (Fase 6, módulo 4) | `(ninguno conocido aún)` — un consumidor típico sería un módulo cliente que cachea el estado de un flag y necesita invalidar su copia al activarse | No — el payload solo transporta `FeatureFlagId` y `Nombre` (identificador lógico de una capacidad de negocio, no de una persona física) | `FeatureManagement.FeatureFlagActivado` (resuelto tal cual por `DefaultKafkaTopicNameResolver`, sin caracteres a reemplazar) | `FeatureFlagId` (`IHasPartitionKey`) — todos los eventos de un mismo flag (activaciones/desactivaciones sucesivas) quedan en la misma partición | 2026-09-09 |
| `FeatureManagement.FeatureFlagDesactivado` | `FeatureFlagDesactivadoIntegrationEvent` — `src/Platform/BitCode.Platform.FeatureManagement` (`Flags/FeatureFlagDesactivadoIntegrationEvent.cs`) | 1 (vigente) | Feature Management (Fase 6, módulo 4) | `(ninguno conocido aún)` | No | `FeatureManagement.FeatureFlagDesactivado` | `FeatureFlagId` (`IHasPartitionKey`) | 2026-09-09 |
| `FeatureManagement.RolloutIniciado` | `RolloutIniciadoIntegrationEvent` — `src/Platform/BitCode.Platform.FeatureManagement` (`Rollouts/RolloutIniciadoIntegrationEvent.cs`) | 1 (vigente) | Feature Management (Fase 6, módulo 4) | `(ninguno conocido aún)` — un consumidor típico sería un panel de observabilidad de rollouts en curso | No — el payload solo transporta identificadores (`RolloutId`/`FeatureFlagId`/`SegmentoId`) | `FeatureManagement.RolloutIniciado` | `FeatureFlagId` (`IHasPartitionKey`) — todos los rollouts del mismo flag quedan en la misma partición | 2026-09-09 |
| `Documents.DocumentoSubido` | `DocumentoSubidoIntegrationEvent` — `src/Platform/BitCode.Platform.Documents` (`Documentos/DocumentoSubidoIntegrationEvent.cs`) | 1 (vigente) | Documents (Fase 6, módulo 5) | `(ninguno conocido aún)` — un consumidor típico sería Import and Export (Fase 6, módulo 10, depende explícitamente de Documents) | No — el payload transporta identificadores (`DocumentoId`/`VersionId`), el nombre de archivo original y el hash SHA-256; el nombre de archivo podría ocasionalmente contener un dato personal según lo que suba cada consumidor real (por ejemplo, "DNI-Juan-Perez.pdf") — un consumidor que sepa que sus documentos llevan ese patrón de nombrado debe reclasificar esta fila explícitamente | `Documents.DocumentoSubido` (resuelto tal cual por `DefaultKafkaTopicNameResolver`, sin caracteres a reemplazar) | `DocumentoId` (`IHasPartitionKey`) — todos los eventos del mismo documento (subida, nuevas versiones, escaneos) quedan en la misma partición | 2026-09-09 |
| `Documents.DocumentoVersionCreada` | `DocumentoVersionCreadaIntegrationEvent` — `src/Platform/BitCode.Platform.Documents` (`Documentos/DocumentoVersionCreadaIntegrationEvent.cs`) | 1 (vigente) | Documents (Fase 6, módulo 5) | `(ninguno conocido aún)` | No — el payload solo transporta identificadores, número de versión y hash | `Documents.DocumentoVersionCreada` | `DocumentoId` (`IHasPartitionKey`) | 2026-09-09 |
| `Documents.DocumentoEscaneado` | `DocumentoEscaneadoIntegrationEvent` — `src/Platform/BitCode.Platform.Documents` (`Documentos/DocumentoEscaneadoIntegrationEvent.cs`) | 1 (vigente) | Documents (Fase 6, módulo 5) | `(ninguno conocido aún)` — un consumidor típico sería un mecanismo de alertas de seguridad que reacciona a `Resultado = Infectado` | No — el payload solo transporta identificadores, número de versión y el resultado del escaneo (`EstadoEscaneo`) | `Documents.DocumentoEscaneado` | `DocumentoId` (`IHasPartitionKey`) | 2026-09-09 |

## Proceso obligatorio de mantenimiento

**Todo bounded context que agregue un `IIntegrationEvent` productivo nuevo, o que cambie el
`SchemaVersion`/los consumidores/la clasificación de PII de uno ya existente, DEBE agregar o actualizar
la fila correspondiente en la tabla "Eventos productivos registrados" de este documento como parte de
su propia Definition of Done — antes de considerar terminada la tarea que introduce el evento, no como
seguimiento posterior.** Esto queda formalizado como regla dura 27 en `docs/convenciones.md` (ver esa
sección para el texto completo y el detalle de qué se considera "productivo" a los efectos de esta
regla).

Checklist concreto al agregar un evento nuevo (o una versión nueva de uno existente):

1. Confirmar `EventType` estable y sin colisión con uno ya registrado en este catálogo (salvo que sea,
   deliberadamente, una versión nueva del mismo evento — misma fila, `SchemaVersion` actualizado).
2. Completar las nueve columnas de la plantilla — ninguna queda vacía sin una anotación explícita (por
   ejemplo, `(ninguno conocido aún)` para consumidores, nunca una celda en blanco).
3. Clasificar PII usando la misma taxonomía que `AuditRedactionOptions.SensitiveMetadataKeys` (F2-19) —
   si el payload transporta un identificador directo, dato financiero o cualquier dato personal, la
   celda "PII" no puede quedar en "No" sin justificación explícita.
4. Si el evento reemplaza/evoluciona uno anterior con cambio de `SchemaVersion`, correr
   `EventSchemaCompatibilityChecker.CheckBackwardCompatibility` (F3-06, `docs/guia-eventing-contratos.md`)
   entre la versión previa y la nueva, y dejar constancia en la fila de si la evolución fue aditiva o
   requirió ventana de coexistencia (`docs/politica-versionado.md`, sección 5).
5. Commitear el cambio a este archivo en el MISMO pull request que introduce el evento — un PR que
   agrega un `IIntegrationEvent` productivo sin tocar `docs/catalogo-eventos.md` está incompleto.

## Por qué no hay (todavía) un test de arquitectura que verifique esto automáticamente

Se evaluó agregar un mecanismo semi-automatizado — un test que enumere por reflexión, en los
ensamblados cargados, todos los tipos que implementan `IIntegrationEvent` y falle si alguno no tiene
entrada correspondiente en este catálogo (mismo espíritu que `EventSchemaCompatibilityChecker`, F3-06,
que también usa reflexión sobre tipos de evento concretos). Se decide **no implementarlo en esta
tarea**, por las siguientes razones concretas, no como una omisión:

- **No hay nada que enumerar todavía.** Los únicos tipos que implementan `IIntegrationEvent` hoy son de
  test (`TestIntegrationEvents.cs`, `TestPartitionedIntegrationEvent.cs` y similares) — un test que
  recorra ensamblados cargados y "falle si algo no está en el catálogo" tendría que excluir
  explícitamente todo el namespace de test para no fallar contra sus propios fixtures, lo cual es
  exactamente el problema inverso al que el test debería resolver.
- **No existe todavía ningún ensamblado de "bounded context de negocio" que cargar.** `EventSchemaCompatibilityChecker`
  (F3-06) compara dos tipos .NET concretos que el propio test nombra explícitamente — no enumera
  ensamblados por descubrimiento. Un test de catálogo por reflexión necesitaría decidir QUÉ ensamblados
  cargar como "productivos" (`AppDomain.CurrentDomain.GetAssemblies()` en el proceso de test no incluye
  automáticamente los ensamblados de un futuro consumidor, y cargar `*.dll` por convención de nombre
  desde `bin/` es frágil y no tiene, hoy, ningún caso real contra el cual validarse) — sin al menos un
  bounded context de negocio real, cualquier mecanismo de descubrimiento que se implemente ahora sería
  no verificable (no hay ningún caso positivo ni negativo real con el que probarlo) y, por definición de
  la sección 3.2 del Plan Maestro, sobre-ingeniería.
- **El framework (`src/`) es una librería, no una aplicación desplegada** — no tiene un único punto de
  entrada que cargue "todos los ensamblados de negocio" del que un test de este repositorio pueda
  partir; ese punto de entrada lo tiene el proyecto CONSUMIDOR (Fase 6 en adelante), no `BitCode.Framework`
  en sí.

**Decisión:** el catálogo queda como proceso MANUAL obligatorio (regla dura 27) hasta que un bounded
context de negocio real declare su primer `IIntegrationEvent` productivo. En ese momento (Fase 6 o
posterior, dentro del proyecto consumidor concreto, no necesariamente dentro de este repositorio de
framework) es el punto correcto para reevaluar si automatizar la verificación — por ejemplo, con un
test de arquitectura en el proyecto consumidor que enumere los tipos de SU PROPIO ensamblado de
aplicación (`Assembly.GetExecutingAssembly()`/`typeof(AlgunMarkerDeEseProyecto).Assembly`, un universo
de reflexión acotado y verificable) y los compare contra un archivo de catálogo — ahí sí habría al
menos un caso real (el primer evento) contra el que probar el mecanismo. Revisar esta decisión cuando
ese momento llegue; no es una decisión permanente, es "prematuro ahora, con la información disponible
hoy".

## Referencias

- `docs/guia-eventing-contratos.md` — contratos, adapter Kafka, Outbox/Inbox, particionamiento y
  compatibilidad de esquema (F3-01 a F3-06); mecanismo que este catálogo referencia sin duplicar.
- `docs/politica-versionado.md`, sección 5 — reglas de compatibilidad forward/backward de eventos.
- `docs/politica-reintentos-eventos.md`, `docs/runbook-dlq.md` — reintentos y DLQ (F3-07/F3-08/F3-09).
- `docs/guia-observabilidad-eventos.md` — métricas de publish/consume/error/lag/DLQ (F3-10).
- `docs/politica-seguridad-kafka.md` — TLS/ACL/identidad de transporte (F3-11).
- `docs/guia-auditoria-inmutable.md`, `src/Shared.Infrastructure.Security/Audit/AuditRedactionOptions.cs`
  (F2-19) — taxonomía de PII reutilizada por la columna "PII" de este catálogo.
- `tests/Shared.Application.Tests/Eventing/TestIntegrationEvents.cs`,
  `tests/Shared.Infrastructure.Messaging.Kafka.Tests/TestPartitionedIntegrationEvent.cs` — eventos de
  EJEMPLO/test usados como caso de muestra en este documento; nunca eventos productivos.
- `docs/convenciones.md`, regla dura 27 (F3-12) — obligación de mantener este catálogo como parte de la
  Definition of Done de cualquier `IIntegrationEvent` productivo nuevo.
