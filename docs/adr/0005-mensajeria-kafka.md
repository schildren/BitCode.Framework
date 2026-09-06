# 0005. Mensajería: Kafka como broker de eventos de integración

**Estado:** Proposed
**Fecha:** 2026-09-05
**Responsable:** Pendiente de asignación

## Contexto

El Plan Maestro (sección 2) fija Kafka como decisión rectora de mensajería, y la Fase 3 ("Event Platform") lo desarrolla como mecanismo de comunicación confiable entre bounded contexts (consistente con la regla de eventual consistency de la sección 2.1). El estado actual del repositorio (`docs/inventario-tecnico.md`, `docs/plan-maestro-bitcode-ia.md` sección 4.1) no tiene ninguna infraestructura de mensajería: "Messaging: Ausente — Construir", y tampoco existen Outbox/Inbox/Idempotency (prerequisito para publicar eventos de forma confiable tras el commit de una transacción SQL, sección 2.1 del plan). En la estructura de repositorio sugerida (Plan Maestro, sección 10) ya se prevén los proyectos `BitCode.Messaging.Contracts` y `BitCode.Messaging.Kafka`, aún no creados.

## Decisión

Se adopta Kafka como broker de eventos de integración entre bounded contexts, a construirse en Fase 3 sobre el patrón Outbox/Inbox de Fase 1 (transacción de negocio + evento Outbox en la misma transacción SQL; publicación después del commit). Se marca este ADR como `Proposed` (no `Accepted`) porque introducir Kafka es la incorporación de un **nuevo broker** al stack de runtime, y la sección 13 del Plan Maestro exige aprobación humana explícita para "introducción de una nueva base de datos o broker" — aun cuando ya esté listado como decisión rectora en la sección 2, formalizar su incorporación real al entorno de ejecución (aprovisionamiento, operación, costos) requiere esa aprobación antes de construir la infraestructura de Fase 3.

## Alternativas consideradas

- **RabbitMQ o Azure Service Bus:** no evaluados en profundidad en este documento; el Plan Maestro ya fija Kafka como decisión rectora del stack objetivo (sección 2), por lo que no se reabre la comparación sin justificación nueva.
- **Sin broker, solo polling de Outbox vía HTTP o base de datos compartida:** descartado como estrategia definitiva; no escala para eventos entre múltiples bounded contexts ni ofrece particionamiento/retención comparable a Kafka.

## Consecuencias

- No implica código nuevo en esta tarea; formaliza la dirección para F3 y para las tareas relacionadas de Outbox/Inbox en Fase 1.
- Ninguna transacción SQL debe extenderse para incluir la publicación síncrona a Kafka (regla de consistencia, `docs/architecture-principles.md` sección 2) — el evento se persiste en Outbox dentro de la transacción; la publicación real a Kafka ocurre en un proceso posterior al commit.
- El Plan Maestro (sección 3.2) prohíbe explícitamente prometer exactly-once de extremo a extremo en mensajería; el diseño de Fase 3 debe asumir at-least-once con Inbox/idempotencia del lado consumidor.

## Riesgos y mitigación

- **Riesgo:** aprovisionar o habilitar tráfico productivo sobre Kafka sin aprobación humana. Mitigación: la introducción operativa de Kafka (aprovisionamiento real, no solo el diseño) se trata como decisión de la sección 13 del Plan Maestro y requiere aprobación antes de F3.
- Vinculado al registro de riesgos de F0-12 (pendiente de creación).

## Addendum F1-23 (Outbox base — lado emisor, sin broker todavía)

Este ADR se mantiene `Proposed` (Kafka en sí sigue sin implementarse, sigue requiriendo la aprobación de la sección 13 antes de F3), pero el prerequisito que menciona la Decisión de arriba ("construirse en Fase 3 sobre el patrón Outbox/Inbox de Fase 1") ya quedó resuelto del lado del Outbox:

- `OutboxSaveChangesInterceptor` (Shared.Infrastructure.Persistence) escribe cada `DomainEvent` levantado por un `AggregateRoot<TId>` (`Shared.Kernel`, vía `RaiseDomainEvent`/`IHasDomainEvents`) como fila `OutboxMessage` (Shared.Domain) dentro del MISMO `SaveChangesAsync` que persiste el cambio de negocio del agregado — nunca en una escritura separada. Esto es lo que garantiza el criterio de aceptación de F1-23 ("evento no se pierde tras commit"): si el commit tiene éxito, el evento ya está en la base junto con el cambio de negocio; si la transacción hace rollback, ninguno de los dos queda persistido.
- `OutboxMessage.ProcessedAtUtc` queda siempre en `null` tras F1-23 — no existe todavía ningún proceso que lo marque como publicado. El relay/publisher que lea las filas pendientes (`ProcessedAtUtc IS NULL`, ya indexado por `OutboxModelConfigurator`) y las publique a Kafka es trabajo de Fase 3, condicionado a la aprobación humana de este ADR; F1-23 no lo implementa.
- Ver `docs/convenciones.md` (regla dura 3 y la nueva entrada de Outbox) para cómo levantar un evento desde un agregado, y `tests/Shared.Infrastructure.Persistence.Tests/Integration/OutboxIntegrationTests.cs` para la verificación de atomicidad contra SQL Server real.
