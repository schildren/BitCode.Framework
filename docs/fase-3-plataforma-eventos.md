# Fase 3 — Plataforma de eventos (Plan Maestro)

**Estado:** Completa (F3-01 a F3-13)
**Fecha de cierre:** 2026-09-07
**Plan de referencia:** [plan-maestro-bitcode-ia.md](plan-maestro-bitcode-ia.md), sección "Fase 3 — Plataforma de eventos".

> Este documento cierra la Fase 3 del Plan Maestro (distinta, en numeración, de
> `docs/fase-3-seguridad-autorizacion.md` — ese documento pertenece al roadmap original del framework
> por capas técnicas, no al Plan Maestro; ver `docs/README.md`, tabla "Fases", vs. la sección "Plan
> Maestro vigente" de ese mismo índice).

## Objetivo de la fase

> Permitir comunicación confiable, versionada y observable entre bounded contexts sin transacciones
> distribuidas.

## Resumen ejecutivo

La Fase 3 construyó, en 13 tareas incrementales, una plataforma de eventos de integración completa
sobre Kafka (ADR [0005](adr/0005-mensajeria-kafka.md), Accepted): contratos agnósticos de proveedor,
adapter Kafka real, el relay de Outbox y el consumidor coordinado con Inbox (ambos ya con su base en
Fase 1, F1-23/F1-24), particionamiento, compatibilidad de esquema, reintentos con backoff, DLQ,
aislamiento de mensajes poison, observabilidad end-to-end, seguridad de transporte, un catálogo de
eventos (proceso + plantilla) y, finalmente (F3-13), una prueba de referencia ejecutable que integra
TODA la infraestructura anterior en un escenario de negocio realista con dos bounded contexts.

Semántica exigida por el plan y verificada en el código (no solo declarada):

- **Entrega:** al menos una vez — verificado explícitamente en `OutboxPublisherIntegrationTests.ProcessBatchAsync_CrashAfterPublish_RestartRepublishesAndMarks` (duplicado aceptable tras una caída simulada del relay).
- **Procesamiento:** idempotente — `InboxConsumerIntegrationTests`/`InboxIntegrationTests` y, de punta a punta, `EndToEndEventingReferenceTests` (F3-13).
- **Confirmación:** solo después de persistir el efecto o Inbox — `KafkaEventConsumer<TEvent>.ConsumeAndHandleOnceAsync` únicamente confirma el offset después de que `IInboxMessageProcessor.ProcessAsync` retorna sin lanzar.
- **Orden:** solo dentro de la partición definida — `KafkaEventPublisherPartitioningIntegrationTests` (F3-05).
- **Reprocesamiento:** soportado y auditado — `docs/runbook-dlq.md` (F3-08).
- **Exactly-once end-to-end:** explícitamente NO prometido (regla dura del Plan Maestro, sección 3.2, y documentado en cada guía de la fase).

## Tareas (F3-01 a F3-13)

| ID | Trabajo | Commit | Entregable | Evidencia del criterio de aceptación |
|---|---|---|---|---|
| F3-01 | Contratos | `bed84f8` | `IIntegrationEvent`/`IEventPublisher`/`IEventConsumer<TEvent>` (`Shared.Application.Eventing`) | `Shared.Application` no referencia ningún paquete de broker — verificado por referencias de proyecto (`Shared.Application.csproj`) y por `docs/guia-eventing-contratos.md`. |
| F3-02 | Provider Kafka | `8f56a46` | `KafkaEventPublisher`/`KafkaEventConsumer<TEvent>` (`Shared.Infrastructure.Messaging.Kafka`) | `KafkaEventPublisherConsumerIntegrationTests` contra broker real (Testcontainers). |
| F3-03 | Outbox Publisher | `2a4c718` | `OutboxBatchProcessor`/`OutboxPublisherBackgroundService` | `OutboxPublisherIntegrationTests` — reinicio antes/después de publicar nunca pierde la fila. |
| F3-04 | Inbox Consumer | `830a96f` | `KafkaEventConsumer<TEvent>` coordinado con `IInboxMessageProcessor` | `InboxConsumerIntegrationTests` — mismo `EventId` entregado dos veces ejecuta el handler una sola vez. |
| F3-05 | Particionamiento | `bd6dded` | `IHasPartitionKey` + `KafkaEventPublisher` | `KafkaEventPublisherPartitioningIntegrationTests` — mismo `AggregateId`/`TenantId` → misma partición, orden preservado dentro de ella. |
| F3-06 | Schema y versionado | `7e2e6c7` | `EventSchemaCompatibilityChecker` | `EventSchemaCompatibilityTests` (`Shared.Application.Tests`) — compatibilidad forward/backward validada de forma ejecutable. |
| F3-07 | Retries | `0497485` | `IEventPublishFailureClassifier`/`EventRetryBackoff`/`EventRetryPolicyOptions` | `OutboxPublisherRetryTests`/pruebas de `KafkaEventConsumer` — backoff con jitter y límite máximo verificados. |
| F3-08 | DLQ | `da4abeb` | `IDeadLetterPublisher`/`KafkaDeadLetterPublisher` + `docs/runbook-dlq.md` | Publicación a tópico dead-letter al agotar reintentos, con metadatos de reprocesamiento (`DeadLetterEnvelope`). |
| F3-09 | Poison messages | `ff0b9a9` | Aislamiento en `KafkaEventConsumer<TEvent>.IsolatePoisonMessageAsync` | `KafkaEventConsumerPoisonMessageIntegrationTests` — un mensaje no deserializable se aísla y el consumidor sigue operando sobre los siguientes. |
| F3-10 | Observabilidad | `d8fe43e` | `KafkaEventingDiagnostics` (métricas + `traceparent`/`tracestate`) | `KafkaEventingObservabilityIntegrationTests` + `docs/guia-observabilidad-eventos.md`. |
| F3-11 | Seguridad | `b1c4d28` | `KafkaMessagingOptionsValidator`/`KafkaClientConfigFactory` | `KafkaClientConfigFactoryAndSecurityValidationTests` + `docs/politica-seguridad-kafka.md` (fail-fast en el arranque ante configuración inconsistente). |
| F3-12 | Event catalog | `9ceb69a` | `docs/catalogo-eventos.md` | Plantilla + proceso obligatorio (regla dura 27, `docs/convenciones.md`); vacío de eventos productivos porque el repositorio aún no tiene ningún bounded context de negocio real (Fase 6 en adelante). |
| F3-13 | Prueba de referencia | *(este cierre)* | `samples/Sample.Eventing` + `samples/Sample.Eventing.Tests` | `EndToEndEventingReferenceTests` — ver sección siguiente. |

## F3-13 — Prueba de referencia (detalle)

### Por qué un sample nuevo y no extender `Sample.Api`

`samples/Sample.Api` (Fase 8) es un piloto de la capa HTTP (versionado de API, OpenAPI, idempotencia,
health checks) sin ningún módulo que publique o consuma eventos de integración. Extenderlo habría
mezclado dos preocupaciones no relacionadas (versionado HTTP vs. eventing asíncrono) en el mismo
proyecto y hubiera obligado a levantar un `WebApplicationFactory` HTTP completo solo para ejercitar un
flujo que no tiene ningún endpoint involucrado. Se creó, con la MISMA convención de csproj/estructura
que `Sample.Api`/`Sample.Api.Tests` (`samples/<Nombre>` + `samples/<Nombre>.Tests`, `IsPackable=false`
heredado de `samples/Directory.Build.props`), un sample dedicado:

- **`samples/Sample.Eventing`** — el código de "producción" del ejemplo: dos módulos de negocio reales
  (namespaces `Pedidos` y `Facturacion`), sin ninguna referencia a
  `Shared.Infrastructure.Messaging.Kafka` (verificación a nivel de referencia de proyecto, no solo de
  convención, del Gate de salida "No existe dependencia de dominio hacia Kafka" — ver más abajo).
- **`samples/Sample.Eventing.Tests`** — la prueba de referencia ejecutable (`EndToEndEventingReferenceTests`),
  con Testcontainers (SQL Server + Kafka reales), mismo patrón que
  `tests/Shared.Infrastructure.Persistence.Tests/Integration/OutboxPublisherIntegrationTests.cs` e
  `InboxConsumerIntegrationTests.cs` — es este proyecto, no `Sample.Eventing`, el que referencia el
  adapter Kafka concreto (composición del host de referencia).

### Escenario de negocio

- **Módulo A (Pedidos):** `Pedido : AggregateRoot<Guid>` (`samples/Sample.Eventing/Pedidos/Pedido.cs`).
  `Pedido.Confirmar()` es una operación de negocio real que levanta
  `PedidoConfirmadoIntegrationEvent` — un `DomainEvent` (F1-23) que TAMBIÉN implementa
  `IIntegrationEvent` (F3-01) e `IHasPartitionKey` (F3-05, `PartitionKey = PedidoId`), mismo patrón que
  `OutboxPublisherTestEvent` de F3-03 pero ahora levantado por un agregado de negocio real, no un
  evento de test suelto.
- **Comando de aplicación:** `ConfirmarPedidoCommand`/`ConfirmarPedidoCommandHandler` (MediatR,
  `ICommand`) — pasa por el pipeline completo (`ValidationBehavior`/`LoggingBehavior`/`TransactionBehavior`,
  Fase 2), cuyo `SaveChangesAsync` es el mismo que el interceptor de Outbox (F1-23) usa para escribir la
  fila `OutboxMessage` atómicamente con el `Pedido` confirmado.
- **Relay real:** `OutboxPublisherBackgroundService` (F3-03) corriendo de verdad (se inicia
  manualmente vía `IHostedService.StartAsync` sobre el `ServiceProvider` del test, con
  `PollingInterval` reducido para no alargar la prueba) — no un loop manual ciclo a ciclo como en los
  tests aislados de F3-03/F3-07.
- **Módulo B (Facturación):** `FacturaPendiente : Entity<Guid>` (entidad propia, sin ningún join ni
  dependencia de las tablas de Pedidos) y `PedidoConfirmadoEventConsumer : IEventConsumer<PedidoConfirmadoIntegrationEvent>`
  (`samples/Sample.Eventing/Facturacion/`), consumido por un `KafkaEventConsumer<PedidoConfirmadoIntegrationEvent>`
  real (F3-02/F3-04/F3-09) coordinado con Inbox real contra SQL Server.
- **Espera de consistencia eventual:** polling acotado con timeout (30 s, intervalo de 200 ms) —
  nunca un `Thread.Sleep` fijo — hasta que la `FacturaPendiente` del Módulo B aparece.
- **Duplicados no repiten efectos (F3-04), reutilizado en este mismo escenario end-to-end:** un
  decorador (`RecordingEventPublisherDecorator`) recuerda el evento efectivamente publicado por el
  relay real y lo republica (mismo `EventId`, misma redelivery que produciría un rebalance de Kafka o
  el duplicado aceptable de un relay que muere entre publicar y marcar la fila) — el test verifica que
  el consumidor procesa la segunda entrega (offset confirmado) pero la `FacturaPendiente` sigue siendo
  una sola fila.

### Resultado de ejecución

```
dotnet test samples/Sample.Eventing.Tests/Sample.Eventing.Tests.csproj -c Debug
Pruebas totales: 1
     Correcto: 1
```

Un único método de test (`ConfirmarPedido_PropagaAFacturacion_ConConsistenciaEventualYSinDuplicarEnRedelivery`)
cubre deliberadamente todo el flujo en una sola prueba de referencia (caso feliz + duplicado) en vez de
fragmentarlo — el objetivo de F3-13 es demostrar el flujo COMPLETO integrado, no añadir más pruebas
aisladas de una sola pieza (eso ya lo hacen F3-01 a F3-11).

## Gate de salida de la Fase 3

| # | Criterio | Estado | Evidencia |
|---|---|---|---|
| 1 | Un cambio de negocio y su evento Outbox se confirman atómicamente | **Cumplido** | `OutboxIntegrationTests.SuccessfulCommand_PersistsBusinessChangeAndOutboxMessageTogether`/`TransactionalCommand_WhenLaterStepFails_RollsBackBusinessChangeAndOutboxTogether` (F1-23/F3-03); reforzado end-to-end por `EndToEndEventingReferenceTests` (F3-13), donde `ConfirmarPedidoCommand` persiste `Pedido` + `OutboxMessage` en el mismo `SaveChangesAsync`. |
| 2 | La caída del broker no pierde eventos | **Cumplido** | `OutboxPublisherIntegrationTests.ProcessBatchAsync_MessageNeverAttempted_IsClaimedAndPublishedOnNextCycle`/`ProcessBatchAsync_PublishFails_LeavesMessageUnprocessedAndRetriesNextCycle`/`ProcessBatchAsync_CrashAfterPublish_RestartRepublishesAndMarks` (F3-03/F3-07): una fila de Outbox nunca se marca como procesada sin publicación exitosa confirmada; un reinicio siempre la reclama de nuevo. |
| 3 | Los consumidores toleran duplicados | **Cumplido** | `InboxConsumerIntegrationTests.ConsumeAndHandleOnceAsync_SameEventPublishedTwice_ExecutesHandlerOnlyOnce` (F3-04) y, de punta a punta con un efecto de negocio real de un segundo bounded context, `EndToEndEventingReferenceTests` (F3-13, paso de republicación). |
| 4 | Existe política de versiones y catálogo | **Cumplido** | `docs/politica-versionado.md` (sección 5) + `EventSchemaCompatibilityChecker`/`EventSchemaCompatibilityTests` (F3-06) + `docs/catalogo-eventos.md` (F3-12, plantilla y proceso obligatorio — regla dura 27 de `docs/convenciones.md`). El catálogo está vacío de eventos PRODUCTIVOS a propósito (ver ese documento); esto no es una brecha de F3-12/F3-13, es la constatación honesta de que el repositorio de framework todavía no aloja ningún bounded context de negocio real. |
| 5 | Lag, errores y DLQ son observables | **Cumplido** | `KafkaEventingDiagnostics` (F3-10): métricas de publish/consume/error/lag/DLQ + correlación `traceparent`/`tracestate` end-to-end, verificado por `KafkaEventingObservabilityIntegrationTests`; DLQ documentado operativamente en `docs/runbook-dlq.md` (F3-08/F3-09). |
| 6 | No existe dependencia de dominio hacia Kafka | **Cumplido** | `Shared.Application`/`Shared.Domain`/`Shared.Kernel` no referencian `Shared.Infrastructure.Messaging.Kafka` (separación estructural desde F3-01). Reforzado por F3-13: `samples/Sample.Eventing.csproj` (donde vive el agregado `Pedido`, el evento y el consumidor de negocio `PedidoConfirmadoEventConsumer`) NO tiene ninguna `ProjectReference` a `Shared.Infrastructure.Messaging.Kafka` — solo `samples/Sample.Eventing.Tests.csproj` (la composición del host de referencia) lo referencia. |

**Los 6 puntos del Gate de salida de la Fase 3 están cumplidos**, con evidencia ejecutable (no solo
documental) para cada uno.

## Documentación generada por la fase

- [guia-eventing-contratos.md](guia-eventing-contratos.md) — contratos de eventos de integración (F3-01), adapter Kafka (F3-02) y relay de Outbox (F3-03).
- [guia-outbox-publisher.md](guia-outbox-publisher.md) — relay de Outbox (F3-03) en detalle.
- [guia-inbox-consumer.md](guia-inbox-consumer.md) — Inbox Consumer (F3-04).
- [politica-reintentos-eventos.md](politica-reintentos-eventos.md) — reintentos (F3-07).
- [runbook-dlq.md](runbook-dlq.md) — DLQ y poison messages (F3-08/F3-09).
- [guia-observabilidad-eventos.md](guia-observabilidad-eventos.md) — observabilidad de eventos (F3-10).
- [politica-seguridad-kafka.md](politica-seguridad-kafka.md) — seguridad de transporte Kafka (F3-11).
- [catalogo-eventos.md](catalogo-eventos.md) — catálogo de eventos de integración (F3-12).
- [adr/0005-mensajeria-kafka.md](adr/0005-mensajeria-kafka.md) — decisión de Kafka como broker (Accepted).

## Pendiente / fuera de alcance de esta fase

- El catálogo de eventos productivos (F3-12) sigue vacío por diseño — se completa cuando el primer
  bounded context de negocio real (Fase 6 del Plan Maestro en adelante) publique su primer evento.
- Ningún test de integración de este repositorio ejercita todavía SASL/SSL contra un broker real (no
  hay imagen de Testcontainers con esa configuración habilitada) — documentado explícitamente como
  brecha en `docs/politica-seguridad-kafka.md` (F3-11).
- `samples/Sample.Eventing` es, deliberadamente, un ejemplo de dos módulos dentro de UN ÚNICO proceso
  (`ServiceProvider`)/UNA ÚNICA base de datos por simplicidad — un consumidor real que despliegue
  Pedidos y Facturación como procesos/bases de datos separados sigue funcionando igual, porque el
  desacople real entre ambos ya está garantizado por diseño (Facturación nunca referencia las tablas
  internas de Pedidos, solo el contrato público del evento).
