# 0009. Contratos de comando con transacción explícita (`ITransactionalCommand`)

**Estado:** Accepted
**Fecha:** 2026-09-05
**Responsable:** Equipo BitCode.Framework

## Contexto

Desde la Fase 2 (`docs/fase-2-capa-aplicacion.md`), `TransactionBehavior` envolvía **todo** `ICommand`
(cualquier tipo que implementara `IBaseCommand`) en una transacción explícita de base de datos:
`BeginTransactionAsync` antes del handler y `CommitAsync`/`RollbackAsync` al finalizar según el
`Result` devuelto. Esta convención estaba documentada como regla dura en `docs/convenciones.md`
("un handler nunca llama `SaveChangesAsync` explícitamente; `TransactionBehavior` ya lo hace").

El backlog de la Épica F1-B (Transacciones y concurrencia) del Plan Maestro identifica esto como un
problema: abrir una transacción de base de datos para **cada** comando, incluso los que modifican un
único agregado en una sola operación, agrega duración y superficie de bloqueo innecesarias (ver
también F1-07, que reduce further la duración del pipeline transaccional). La tarea F1-06 pide que
"solo comandos explícitos abran transacción", separando el contrato en:

- `ICommand`/`ICommand<TResponse>`: contrato base, sin transacción implícita.
- `ITransactionalCommand`: marcador que un comando agrega explícitamente cuando su ejecución
  necesita coordinar más de una operación de escritura (varios agregados, varios `SaveChanges`,
  efectos que deben confirmarse o revertirse en conjunto) dentro de una transacción real con
  rollback automático.
- `IIdempotentCommand`: marcador para comandos que deben tolerar reintentos sin duplicar efectos
  (el mecanismo de detección de duplicados se implementa en F1-22, Idempotencia API).

## Decisión

`TransactionBehavior` deja de abrir una transacción real (`BeginTransactionAsync`/`CommitAsync`/
`RollbackAsync`) para todo `IBaseCommand`. En su lugar:

- Si el comando implementa `ITransactionalCommand`, se comporta como antes: abre una transacción
  explícita, hace `CommitAsync` si el `Result` es exitoso, y `RollbackAsync` si el `Result` es
  fallido o el handler lanza una excepción.
- Si el comando **no** implementa `ITransactionalCommand` (el caso común: un solo agregado, una sola
  operación de escritura), `TransactionBehavior` solo llama `IUnitOfWork.SaveChangesAsync` cuando el
  `Result` es exitoso, sin abrir una transacción explícita — un único `SaveChangesAsync` ya es
  atómico para el conjunto de cambios rastreados por el `DbContext`.

Se agrega `IIdempotentCommand` como contrato marcador puro, sin comportamiento asociado todavía; su
middleware se implementa en F1-22.

El handler de un `ICommand` sigue sin llamar `SaveChangesAsync` explícitamente en ningún caso: la
regla dura 1 de `docs/convenciones.md` no cambia, solo se precisa cuándo hay transacción explícita
de por medio (reglas 3 y 4, actualizadas en esta misma tarea).

Se migró `CrearProductoCommand` (único comando real del piloto, en `samples/Sample.Api`) revisando
si necesitaba `ITransactionalCommand`: como modifica un único agregado en una sola operación, se
mantiene como `ICommand<Guid>` simple sin el marcador — su comportamiento observable (persistencia
tras un `Result` exitoso) no cambia.

## Alternativas consideradas

- **Mantener la transacción implícita para todo comando.** Descartada: es exactamente lo que el
  Plan Maestro (F1-06) pide corregir; además acopla el overhead de una transacción de base de datos
  a comandos que no la necesitan.
- **Eliminar `TransactionBehavior` para comandos simples y forzar que cada handler llame
  `SaveChangesAsync` a mano.** Descartada: rompería la regla dura 1 de `docs/convenciones.md` y el
  patrón ya documentado en toda la guía de uso (`docs/guia-uso-proyectos.md`), obligando a un cambio
  de comportamiento mucho más amplio en cada handler existente y futuro.
- **Detectar en tiempo de compilación (constraint genérico) en lugar de `is ITransactionalCommand`
  en tiempo de ejecución.** Descartada: un constraint genérico exigiría dos registros de
  `TransactionBehavior` (uno por cada combinación de constraints) o una jerarquía de tipos más
  compleja; el pattern-matching en tiempo de ejecución es más simple y suficientemente eficiente
  para un pipeline de MediatR (ya se ejecuta una comparación de tipo por request).

## Consecuencias

- **Compatibilidad:** cambio de comportamiento en `TransactionBehavior` (proyecto `Shared.Application`,
  todavía en `PublicAPI.Unshipped.txt`, sin versión estable publicada) y adición de dos interfaces
  públicas nuevas (`ITransactionalCommand`, `IIdempotentCommand`). No es un breaking change de una
  API ya publicada a consumidores externos (el framework sigue en Fase 1, sin release 1.0), pero
  **sí** es breaking para cualquier consumidor interno/piloto que ya dependiera de la transacción
  implícita en un comando con múltiples pasos de escritura: ese comando debe agregar
  `ITransactionalCommand` explícitamente o perderá la protección transaccional. Se relevó todo el
  repo (incluido `samples/Sample.Api` y `templates/feature-cqrs`) y no se encontró ningún comando
  real que dependiera de un rollback multi-paso; el único comando existente (`CrearProductoCommand`)
  no necesita el marcador.
- **Rendimiento:** los comandos simples (la mayoría esperada) dejan de pagar el costo de abrir una
  transacción de base de datos explícita (una ida y vuelta adicional al motor); solo pagan el costo
  ya existente de `SaveChangesAsync`. Los comandos que sí necesitan `ITransactionalCommand`
  mantienen exactamente el mismo comportamiento y costo que antes.
- **Seguridad/auditoría:** sin cambios — la auditoría e interceptores de `Shared.Infrastructure.Persistence`
  no dependen de si hay una transacción explícita abierta.
- **Documentación:** se actualizaron `docs/convenciones.md` (reglas 1, 3 y 4) y
  `docs/fase-2-capa-aplicacion.md` (sección "Actualización F1-06") para reflejar el nuevo contrato.

## Riesgos y mitigación

- **Riesgo:** un equipo consumidor migra un comando multi-paso sin darse cuenta de que ya no tiene
  transacción implícita, y queda con escrituras parciales ante un fallo a mitad de camino.
  **Mitigación:** la regla dura 3 de `docs/convenciones.md` documenta explícitamente el criterio
  ("más de una operación de escritura → `ITransactionalCommand`"); se recomienda reforzarlo con un
  analizador de Roslyn en una tarea futura si se detecta que el criterio no se sigue en la práctica
  (fuera de alcance de F1-06).
- **Riesgo:** F1-07 (reducir aún más la duración del pipeline transaccional) puede requerir ajustar
  de nuevo `TransactionBehavior`; este ADR no bloquea esa tarea, solo establece el contrato de qué
  comandos participan de una transacción explícita.

## Addendum — F1-07 (endurecimiento del pipeline transaccional)

F1-07 revisó `TransactionBehavior` bajo el criterio de aceptación "Rollback verificado" y confirmó
que el diseño de este ADR ya cumple la reducción de duración exigida, sin requerir un rediseño:

- **Apertura/cierre de la transacción:** `BeginTransactionAsync` ya se ejecutaba inmediatamente antes
  de invocar el handler (lo más tarde posible) y `CommitAsync`/`RollbackAsync` inmediatamente después
  de que el handler retorna (lo más pronto posible). El único ajuste fue de orden: en las ramas de
  fallo (`Result` fallido o excepción), el logging se movió a **después** de `RollbackAsync` en lugar
  de antes, para no demorar la liberación de locks con una llamada a un sink de logging potencialmente
  remoto mientras la transacción seguía abierta.
- **Llamadas externas dentro de la transacción:** se relevó el repositorio completo; no existe hoy
  ningún handler de `ITransactionalCommand` en producción (el único comando real,
  `CrearProductoCommand`, es un `ICommand` simple sin el marcador) que realice llamadas HTTP, a cache
  distribuido o a un broker de mensajería. Se documentó como regla dura (`docs/convenciones.md`,
  regla 3) que ese tipo de llamadas debe diferirse hasta después del commit (por ejemplo, vía Outbox
  en F1-23) precisamente para prevenir esta clase de problema antes de que aparezca el primer handler
  real de este tipo.
- **Control de `SaveChangesAsync`:** `TransactionBehavior` nunca invoca `SaveChangesAsync`
  directamente en el camino transaccional; delega toda la persistencia final a un único
  `IUnitOfWork.CommitAsync`. Se agregaron aserciones explícitas (`tests/Shared.Application.Tests/TransactionBehaviorTests.cs`)
  que verifican, con mocks, que ni éxito ni fallo ni excepción disparan una llamada directa a
  `SaveChangesAsync` desde el behavior. Se aclaró en la regla dura 1 de `docs/convenciones.md` que un
  handler de `ITransactionalCommand` sí puede llamar `SaveChangesAsync` de forma intermedia cuando
  coordina escrituras dependientes entre sí (a diferencia de un `ICommand` simple, que nunca debe
  hacerlo): esas llamadas intermedias quedan protegidas por la misma transacción de base de datos.
- **Rollback verificado (criterio de aceptación literal):** no existía ningún comando
  `ITransactionalCommand` real que coordinara múltiples escrituras, así que se creó
  `TwoStepTransactionalCommand` (comando de prueba, en
  `tests/Shared.Infrastructure.Persistence.Tests/Integration/TransactionBehaviorIntegrationTests.cs`)
  que ejecuta dos escrituras dependientes (con un `SaveChangesAsync` intermedio real hacia SQL Server)
  y falla intencionalmente en la segunda. Contra un SQL Server real (Testcontainers) y a través del
  pipeline completo de MediatR (`AddSharedApplication` + `TransactionBehavior` + `AddSharedPersistence`),
  se verificó que tras el fallo **ningún** dato queda persistido, ni siquiera el de la primera
  escritura ya enviada al motor antes de que la segunda fallara.
