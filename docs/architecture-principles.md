# Architecture Principles — BitCode.Framework

**Tarea:** F0-03 (Fase 0 — Gobierno, arquitectura y línea base) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-05
**Estado:** **Propuesto — pendiente de aprobación humana.** El criterio de aceptación de F0-03 exige aprobación humana explícita; este documento no debe tratarse como vigente hasta que un responsable de arquitectura lo apruebe (ver sección "Aprobación" al final).

Este documento formaliza los cinco ejes exigidos por F0-03 — modularidad, consistencia, seguridad, observabilidad y compatibilidad — como principios accionables. No son aspiraciones abstractas: cada principio se ancla a una regla, un componente o un archivo que ya existe en el repositorio (ver `docs/inventario-tecnico.md` y `docs/convenciones.md`), y señala explícitamente dónde el estado actual todavía no lo cumple.

Estos principios no contradicen las reglas duras de `docs/convenciones.md`; las presuponen. Donde un principio requeriría cambiar una regla dura vigente, se indica como decisión pendiente, no como principio ya aplicable.

---

## 1. Modularidad

**Principio:** Cada capacidad del framework es un módulo con una frontera explícita — un proyecto `Shared.*` con dependencias declaradas por `ProjectReference`, o un `IFrameworkModule`/`IWebFrameworkModule` con `[DependsOn]` explícito en tiempo de composición. Ninguna capacidad nueva se agrega mezclando responsabilidades dentro de un proyecto existente ni acoplando un módulo a otro por referencia implícita (reflexión oculta, servicio estático, `IQueryable` compartido entre capas).

**Regla accionable:**
- Un módulo de infraestructura transversal (cache, observabilidad, background jobs) no depende de `Shared.Kernel` ni `Shared.Domain` salvo que su función lo exija — la independencia es intencional y debe preservarse al extender esos módulos.
- Toda nueva capacidad de negocio se organiza como "un feature = una carpeta = un `IWebFrameworkModule`" (ver `docs/convenciones.md`), no como controladores o servicios sueltos fuera de esa convención.
- Un módulo no se propone como microservicio solo por estar bien delimitado en código; aplica la regla de la sección 2.2 del Plan Maestro (necesidad real de escalabilidad, SLA, aislamiento de fallos, etc.).

**Ejemplo concreto ya vigente:** `src/Shared.Infrastructure.Caching`, `Shared.Infrastructure.Observability` y `Shared.Infrastructure.BackgroundJobs` no tienen `ProjectReference` entre sí ni hacia `Shared.Kernel`/`Shared.Domain` (ver `docs/inventario-tecnico.md`, sección 2) — son módulos standalone conectados únicamente a través de `IFrameworkModule`/DI en el proyecto consumidor.

**Brecha actual:** no existe todavía un template de scaffolding para `*Module.cs` ni para Query (inventario-tecnico.md, brecha #12); la convención de "un feature = un módulo" depende hoy de disciplina manual, no de generación automática.

---

## 2. Consistencia

**Principio:** ACID dentro de un bounded context; Eventual Consistency entre bounded contexts (Plan Maestro, sección 2.1). Ninguna operación de negocio combina una transacción SQL prolongada con una llamada HTTP, una publicación a un broker o una interacción con un sistema externo dentro de la misma transacción. El cambio de negocio y el evento de integración (Outbox) se persisten juntos; el efecto externo ocurre después del commit.

**Regla accionable:**
- Un `ICommand` nunca llama `IUnitOfWork.SaveChangesAsync` explícitamente; `TransactionBehavior` confirma la transacción al finalizar el pipeline (regla dura 1 de `docs/convenciones.md`).
- Un `IQuery` nunca modifica datos — solo `IBaseCommand` tiene la protección transaccional de `TransactionBehavior` (regla dura 2).
- Una consulta nunca expone `IQueryable`; toda lectura pasa por `ISpecification<T>` o métodos tipados de `IRepository`/`IReadRepository` (regla dura 3) — esto evita que un consumidor introduzca side effects o N+1 fuera del control del framework.
- Un error de negocio esperado es `Result.Failure`, nunca una excepción (regla dura 4); las excepciones quedan reservadas a lo verdaderamente inesperado.

**Ejemplo concreto ya vigente:** `CrearProductoCommandHandler` en `samples/Sample.Api` no invoca `SaveChangesAsync`; delega en `TransactionBehavior` (Fase 2), documentado en `docs/convenciones.md` regla 1.

**Brecha actual:** el propio Plan Maestro (sección 4.1) clasifica `Transaction Behavior` como "Parcial — rediseñar para transacciones explícitas" y a Outbox/Inbox/Idempotency como "Ausentes — construir". El principio de consistencia entre bounded contexts (Eventual Consistency vía eventos) todavía no tiene mecanismo de entrega confiable en el repo; es objetivo de Fase 1 y Fase 3, no un hecho ya cumplido.

---

## 3. Seguridad

**Principio:** Zero Trust por defecto — ninguna operación se autoriza por pertenecer a una red confiable ni por poseer un token sin validar issuer, audience, firma y vigencia. La autorización aplica *default deny*: un recurso protegido rechaza el acceso salvo permiso explícito. Ningún secreto, token o certificado se guarda en el repositorio (Plan Maestro, sección 3.2). Toda entidad multi-tenant declara su tenencia mediante la interfaz `ITenantEntity` para que el filtro global de EF Core la aplique sin configuración manual — omitirla es una brecha de aislamiento entre tenants, no un detalle de implementación.

**Regla accionable:**
- Toda entidad que necesite auditoría, soft-delete o multi-tenancy implementa `IAuditedEntity`/`ISoftDelete`/`ITenantEntity` y nada más — el framework la detecta por reflexión (regla dura 5 de `docs/convenciones.md`); no se agregan columnas de tenant sin la interfaz porque el filtro global de `MultiTenantDbContext` no las alcanzaría.
- Un endpoint que requiere permiso usa `[RequirePermission("entidad.accion")]` o `.RequireAuthorization(...)`, nunca una verificación manual dispersa en el handler.
- Ninguna dependencia se incorpora sin revisar licencia, mantenimiento, seguridad y compatibilidad (Plan Maestro, sección 3.2) — la política formal de esto es F0-06, pendiente.

**Ejemplo concreto ya vigente:** `MultiTenantDbContext.OnModelCreating` aplica `MultiTenancyModelConfigurator.ApplyGlobalFilters` de forma automática sobre toda entidad `ITenantEntity` (`src/Shared.Infrastructure.Persistence/MultiTenancy/MultiTenantDbContext.cs:24-28`), sin necesidad de configuración adicional por entidad.

**Brecha actual:** el Plan Maestro (sección 4.1) clasifica el JWT propio actual como "insuficiente como default empresarial" (a reemplazar por OIDC/OAuth2 en Fase 2) y la multi-tenancy como "riesgo de rendimiento — rediseñar y benchmarkear". La elección de IdP y de proveedor de secretos/KMS son decisiones que requieren aprobación humana explícita (Plan Maestro, sección 13) y no se resuelven en este documento.

---

## 4. Observabilidad

**Principio:** Toda operación relevante de negocio o infraestructura es trazable, medible y auditable sin depender de inspección manual de logs no estructurados. La telemetría (trazas, métricas, logs correlacionados) es un requisito de diseño del componente, no un agregado posterior — y una operación crítica que falla debe ser distinguible de una que nunca se auditó (Definition of Done, Plan Maestro sección 3.5: "no deja marcadores temporales sin ticket", "incluye telemetría y manejo de errores cuando corresponde").

**Regla accionable:**
- Un componente nuevo que cruza un límite de proceso (HTTP, SQL, cache, cola) usa la instrumentación ya provista por `Shared.Infrastructure.Observability` (OpenTelemetry + Serilog) en vez de logging ad hoc.
- Ninguna optimización de rendimiento se realiza sin baseline, perfilado y comparación (Plan Maestro, sección 9, "Elementos que no deben adelantarse") — el punto de referencia es `docs/linea-base-rendimiento.md` (F0-10).
- El uso de cache (`HybridCache`) no oculta el origen de verdad: la escritura a Redis L2 es asíncrona y no debe asumirse consistente entre instancias (ver `docs/convenciones.md`, tabla "Cuándo usar qué") — cache nunca es fuente de verdad para saldos, ledger, auditoría o transacciones (Plan Maestro, sección 3.2).

**Ejemplo concreto ya vigente:** `Shared.Infrastructure.Observability` ya integra `OpenTelemetry.Instrumentation.AspNetCore/Http/Runtime` y `Serilog.AspNetCore` (ver `docs/inventario-tecnico.md`, sección 3) — es la base a extender, no a reconstruir, en Fase 1/Fase 2.

**Brecha actual:** el Plan Maestro (sección 4.1) describe la cobertura de OpenTelemetry como "base disponible — completar cobertura y estándares"; no hay todavía auditoría inmutable (F2-15 a F2-20) ni pipeline con SAST/SCA/secret scanning (inventario-tecnico.md, sección 5).

---

## 5. Compatibilidad

**Principio:** Ningún cambio de contrato público (API HTTP, evento de integración, esquema de base de datos, paquete NuGet) se libera sin análisis explícito de impacto en consumidores existentes. Un breaking change requiere versión mayor documentada y guía de migración; nunca se introduce silenciosamente dentro de una versión menor o parche (Plan Maestro, sección 3.2 y 3.5).

**Regla accionable:**
- Un endpoint siempre construye su respuesta HTTP a partir de `Result` vía `.ToOkOrProblem()`/`.ToProblemDetails()` (regla dura 6 de `docs/convenciones.md`) — cambiar esta forma de respuesta es en sí mismo un cambio de contrato y debe tratarse como tal.
- Un breaking change de API pública es una decisión que requiere aprobación humana explícita (Plan Maestro, sección 13); no se decide unilateralmente durante la ejecución de una tarea.
- No se introduce persistencia políglota, un nuevo broker o una nueva base de datos sin ADR y mediciones (Plan Maestro, sección 9) — la compatibilidad con la arquitectura ya decidida (sección 2 del Plan Maestro) se preserva salvo decisión explícita registrada en un ADR.

**Ejemplo concreto ya vigente:** la ausencia de Central Package Management (`Directory.Packages.props`) permite hoy que cada `.csproj` fije su propia versión de paquete, lo que ya produjo deriva de versión entre proyectos `net8.0` (`Microsoft.Extensions.*` en 8.0.x vs 9.0.0, ver `docs/inventario-tecnico.md`, brecha #3) — es el caso concreto que motiva la política de versionado formal de F0-05 y F0-06.

**Brecha actual:** F0-05 (Contratos de compatibilidad — SemVer, deprecación, versionado de API/eventos/esquemas/paquetes) todavía no se ejecutó; este principio describe la intención, no un mecanismo de enforcement ya activo en el pipeline.

---

## Relación con decisiones que exigen aprobación humana

Ninguno de los cinco principios anteriores decide por sí mismo un IdP, un proveedor de secretos/KMS, un cambio de licencia, un breaking change concreto, un cambio de modelo multi-tenant, una nueva base de datos/broker, un SLA/RPO/RTO contractual, una extracción de microservicio o una habilitación de tráfico productivo. Esas decisiones siguen la sección 13 del Plan Maestro y se documentan en ADR individuales marcados `Proposed` (ver `docs/adr/`), nunca aprobadas en nombre del usuario por este documento.

## Aprobación

| Campo | Valor |
|---|---|
| Estado | Propuesto |
| Aprobado por | Pendiente |
| Fecha de aprobación | Pendiente |

Este documento pasa a estar vigente únicamente cuando un responsable de arquitectura identificado (Plan Maestro, sección "Precondiciones" de Fase 0) registre su aprobación explícita, actualizando esta tabla.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — secciones 2, 3.2, 3.5, 4, 9 y 13.
- [`convenciones.md`](convenciones.md) — reglas duras vigentes.
- [`inventario-tecnico.md`](inventario-tecnico.md) — estado real del repositorio (F0-01).
- [`adr/`](adr/) — decisiones arquitectónicas individuales derivadas de estos principios (F0-04).
