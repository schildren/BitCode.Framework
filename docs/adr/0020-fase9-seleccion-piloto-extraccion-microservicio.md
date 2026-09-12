# 0020. Fase 9 (F9-01): selección de módulo piloto para extracción como microservicio

**Estado:** Accepted
**Fecha:** 2026-09-12 (propuesto) — 2026-09-12 (aceptado)
**Responsable:** Javier León (aprobación humana explícita)

## Aprobación

Aprobado explícitamente por Javier León el 2026-09-12: **Workflow** es el módulo piloto elegido, entre
las tres opciones presentadas (Workflow / Reporting / posponer Fase 9). Con esta aprobación queda
habilitada F9-02 (Contract boundary), condicionada — como recomienda este mismo ADR — a resolver primero
el acoplamiento de compilación (`ProjectReference` directo) que `BitCode.Platform.TaskInbox` y
`BitCode.Platform.Notifications` tienen hoy contra `BitCode.Platform.Workflow`.

## Contexto

F9-01 (`docs/plan-maestro-bitcode-ia.md`, Fase 9, fila del backlog) pide "evaluar módulos con matriz
cuantitativa" contra los 7 criterios de elegibilidad de esa misma sección, y producir un ADR de
selección. El criterio de aceptación literal de F9-01 es **"Aprobación humana"** — no "módulo
seleccionado", no "extracción iniciada". Este ADR es, por diseño, una **propuesta razonada**, no una
decisión ejecutable: queda en estado `Proposed` y ninguna tarea posterior de Fase 9 (F9-02 en adelante)
debe comenzar hasta que una persona (no un agente) lo revise y lo pase a `Accepted`.

Los 12 módulos candidatos son toda la Plataforma Funcional Empresarial de Fase 6
(`src/Platform/BitCode.Platform.*`): Catalogs, Dashboard, Documents, FeatureManagement, Identity,
ImportExport, IntegrationHub, Notifications, Organization, Reporting, TaskInbox, Workflow.

### Limitación estructural que aplica a los 12 módulos por igual (léase antes de la matriz)

Dos de los 7 criterios de elegibilidad no pueden responderse honestamente en positivo para **ningún**
módulo hoy, y esto no es un defecto de un módulo en particular sino una propiedad del estado actual del
proyecto:

- **"¿Requiere escala o SLA independiente?" (evidencia cuantitativa):** BitCode.Framework no tiene
  ningún despliegue productivo real. No existe tráfico real, ni métricas de producción, ni un release
  publicado (`docs/plan-maestro-bitcode-ia.md`, Fase 8, Gate de salida: "Los paquetes tienen
  versionado, firma y provenance" sigue sin cumplirse; único tag existente `v0.1.0`, sin release
  publicado). Cualquier número que se pusiera acá sería inventado. Se documenta explícitamente como "sin
  evidencia" para los 12 módulos.
- **"¿Tiene equipo u owner operativo?" (responsabilidad explícita):** `git log --format='%an' | sort -u`
  devuelve un único autor (Javier León) en todo el historial del repositorio, y no existe archivo
  `CODEOWNERS` propio del proyecto (el único `CODEOWNERS` que aparece bajo el repo pertenece a una
  dependencia de `frontend/node_modules/`, no al código de BitCode). No hay evidencia de "equipos"
  reales en este framework de referencia — es correcto documentar esta limitación en vez de simular una
  estructura organizacional que no existe.

Estos dos puntos se repiten igual en la matriz para los 12 módulos: no es que un módulo los cumpla mejor
que otro, es que la pregunta no tiene todavía un dato real que la responda para ninguno. El resto de la
matriz sí discrimina entre módulos, porque depende de código y arquitectura verificables hoy.

### Método de evaluación

Para cada módulo se inspeccionó código real: `*DbContext.cs` (ownership de esquema), presencia de joins
o SQL crudo cross-módulo (`grep` de `JOIN`/`FromSqlRaw` en `src/Platform/**/*.cs` — resultado: ningún
archivo), `ProjectReference` entre módulos de plataforma (acoplamiento de compilación), eventos de
integración publicados y sus consumidores reales (o su ausencia), endpoints versionados (`api/v1`),
health checks (Fase 4) y existencia de un host ejecutable propio (`samples/Sample.<Módulo>.Api`).

Hallazgo transversal relevante: **`grep -r "JOIN\|FromSqlRaw\|FromSqlInterpolated" src/Platform` no
devuelve ningún archivo** — ningún handler de ningún módulo hace joins ni SQL crudo contra el esquema de
otro módulo. Esto respalda "ownership de datos" en términos de acceso a datos para los 12 módulos por
igual; la matriz distingue entre ellos por otros ejes (acoplamiento de compilación, consumidores reales
de eventos, dependencias síncronas conceptuales).

## Matriz de elegibilidad (12 módulos × 7 criterios)

Leyenda: **Sí** = criterio cumplido con evidencia verificable en código. **Parcial** = el mecanismo
existe pero no está ejercitado por un caso real (por ejemplo, evento publicado sin ningún consumidor en
el repo). **No** = criterio no cumplido o evidencia en contra.

| Módulo | 1. Escala/SLA independiente | 2. Ownership de datos | 3. Contrato estable (API+eventos) | 4. Tolera consistencia eventual | 5. Equipo/owner operativo | 6. Despliega/observa solo | 7. Separación reduce más riesgo del que agrega |
|---|---|---|---|---|---|---|---|
| **Catalogs** | No — sin evidencia (ver limitación estructural) | Sí — `CatalogsDbContext` propio, sin joins externos | Parcial — endpoints `api/v1`, eventos con `SchemaVersion` (`CatalogoVersionPublicadaIntegrationEvent`, `ParametroVigenciaCreadaIntegrationEvent`) pero **sin consumidor real** en el repo | Parcial — publica al Outbox, ningún módulo lo consume hoy | No — sin evidencia | Parcial — `samples/Sample.Catalogs.Api` corre standalone (`Program.cs`, `Sdk.Web`) y tiene health check propio, pero sin `Dockerfile`/manifiesto K8s propio (solo existe `docker/sample-api` para el host base de Fase 4) | No — sin consumidores reales que arriesgar, separar hoy no reduce nada medible |
| **Dashboard** | No — sin evidencia | Parcial — `DashboardDbContext` propio para `DashboardWidget`, pero funcionalmente depende en tiempo de ejecución de la API HTTP de Reporting (`DashboardReportingHttpClient`) para resolver sus métricas | Parcial — endpoints `api/v1` propios; no publica ningún evento de integración (es consumidor puro) | No — su única integración cross-módulo es una llamada HTTP **síncrona** con resiliencia (`Shared.Infrastructure.Http.Resilience`, `DashboardServiceCollectionExtensions.cs:19-26`), lo opuesto a tolerar consistencia eventual | No — sin evidencia | Parcial — mismo patrón sample host + health check, sin infra propia | No — es un agregador funcionalmente acoplado a Reporting; extraerlo no aísla nada, solo desplaza la dependencia síncrona |
| **Documents** | No — sin evidencia | Sí — `DocumentsDbContext` propio (`Documento`, `DocumentoVersion`) | Parcial — eventos versionados (`DocumentoSubidoIntegrationEvent`, `DocumentoVersionCreadaIntegrationEvent`, `DocumentoEscaneadoIntegrationEvent`), **sin consumidor real** | Parcial — publica, nadie consume hoy | No — sin evidencia | Parcial — mismo patrón sample host | No — sin consumidores reales que validar |
| **FeatureManagement** | No — sin evidencia | Sí — `FeatureManagementDbContext` propio | Parcial — eventos versionados (`FeatureFlagActivadoIntegrationEvent`, `FeatureFlagDesactivadoIntegrationEvent`, `RolloutIniciadoIntegrationEvent`), **sin consumidor real** | Parcial — publica, nadie consume hoy | No — sin evidencia | Parcial — mismo patrón sample host | No — sin consumidores reales que validar |
| **Identity** (Identity Administration) | No — sin evidencia | Parcial — `IdentityAdministrationDbContext` es dueño formal de `AspNetUsers`/`AspNetRoles`/`RefreshTokens`, pero esas MISMAS tablas son las que resuelve `UserManager<ApplicationUser>`/`RoleManager<ApplicationRole>` de Security 2.0, consumido de forma **síncrona en cada request** por todos los demás módulos vía middleware de autenticación (`Shared.Infrastructure.Security`) — no es un límite de bounded context aislado, es la base de autenticación transversal de la plataforma | Parcial — endpoints `api/v1` propios; no se encontraron eventos de integración propios publicados por este módulo | No — la autenticación es, por naturaleza, un camino síncrono crítico; no hay ni debe haber un patrón de "compensación eventual" si el servicio de identidad no responde | No — sin evidencia | Parcial — mismo patrón sample host | **No** — es el módulo de mayor radio de impacto de los 12: si se extrae mal, cae la autenticación de toda la plataforma; separar hoy, sin patrón de eventual consistency para auth, aumenta el riesgo en vez de reducirlo |
| **ImportExport** | No — sin evidencia cuantitativa, aunque conceptualmente el procesamiento batch es del tipo que normalmente justifica escalado independiente (picos de CPU/memoria distintos al resto) | Sí — `ImportExportDbContext` propio (`ImportJob`, `ExportJob`) | Parcial — eventos versionados (`Importacion/ExportacionCompletada/Fallida`), **sin consumidor real** | Parcial — ya es asíncrono por diseño (jobs en background), pero sin consumidor de sus eventos que ejercite una compensación real | No — sin evidencia | Parcial — mismo patrón sample host | Parcial — argumento conceptual razonable (aislar carga batch), pero sin datos reales de contención de recursos que lo confirmen hoy |
| **IntegrationHub** | No — sin evidencia cuantitativa, aunque conceptualmente el más candidato "natural" a SLA independiente por depender de sistemas externos de terceros con latencia variable | Sí — `IntegrationHubDbContext` propio (`IntegrationRequest`) | Parcial — eventos versionados (`SolicitudIntegracionEnviada/FallidaIntegrationEvent`), **sin consumidor real** en el repo | Parcial — ya usa `Shared.Infrastructure.Http` con resiliencia saliente hacia terceros, pero sin consumidor interno de sus propios eventos | No — sin evidencia | Parcial — mismo patrón sample host | Parcial — argumento conceptual fuerte (aislar fallos de integraciones externas del resto de la plataforma), pero sin eventos consumidos por nadie hoy que demuestren el beneficio |
| **Notifications** | No — sin evidencia | Sí — `NotificationsDbContext` propio (`Notification`, `NotificationDelivery`, plantillas) | Sí — consume `TareaAsignadaIntegrationEvent` **real** de Workflow (evento con `SchemaVersion`) y publica sus propios eventos versionados | **Sí** — el consumo de `TareaAsignadaIntegrationEvent` pasa por `IInboxMessageProcessor` (idempotente) y tiene reintentos propios (`NotificationRetryJob`) — patrón demostrado, no solo declarado | No — sin evidencia | Parcial — mismo patrón sample host | Parcial — buen candidato en el eje de consistencia eventual, pero tiene `ProjectReference` de **compilación directa** a `BitCode.Platform.Workflow` (`BitCode.Platform.Notifications.csproj`) para usar el tipo .NET del evento — el "contract boundary" (F9-02) todavía no está limpio, sería trabajo previo obligatorio |
| **Organization** | No — sin evidencia | Parcial — `OrganizationDbContext` propio (`Empresa`, `Sucursal`) sin joins externos, pero el concepto `TenantId` que expone es la clave de particionamiento multi-tenant que usa `ITenantProvider` en los 12 módulos — acoplamiento conceptual transversal, aunque no de acceso a datos | Parcial — eventos versionados (`EmpresaCreada/DesactivadaIntegrationEvent`, `SucursalCreadaIntegrationEvent`), **sin consumidor real** | Parcial — publica, nadie consume hoy | No — sin evidencia | Parcial — mismo patrón sample host | No — al igual que Identity, es un módulo fundacional (tenancy) del que el resto depende conceptualmente; separarlo temprano, sin evidencia de necesidad, es más riesgo que beneficio |
| **Reporting** | No — sin evidencia | Sí — `ReportingDbContext` propio (`ReporteWorkflowInstancia`) | **Sí** — expone una API HTTP pública ya invocada por otro módulo (`DashboardReportingHttpClient`), el único caso en todo el código donde el contrato HTTP de un módulo de plataforma ya es consumido "over the wire" (con cliente resiliente) en vez de por referencia de compilación | **Sí** — consume 2 eventos reales de Workflow (`WorkflowInstanciaIniciada/FinalizadaIntegrationEvent`) vía `IEventConsumer`/Inbox idempotente, mismo patrón maduro que TaskInbox | No — sin evidencia | Parcial — mismo patrón sample host | **Sí** — es, junto con TaskInbox, el módulo con el perfil más completo: ownership limpio, contrato ya ejercitado por un consumidor real (Dashboard) y consumo eventual ya demostrado; sin `ProjectReference` directo a Workflow (a diferencia de Notifications/TaskInbox, ver más abajo) — su boundary de eventos ya es más limpio |
| **TaskInbox** | No — sin evidencia | Sí — `TaskInboxItem` es un read-model propio, documentado explícitamente como "nunca toca `WorkflowDbContext`" (`TaskInboxDbContext.cs:10-14`) | Sí — consume 3 eventos reales de Workflow (`TareaAsignada/Aprobada/RechazadaIntegrationEvent`), documentado en `docs/guia-inbox-consumer.md` | **Sí** — es el caso más maduro y documentado de consumo idempotente vía Inbox de los 12 módulos, con manejo explícito de reordenamiento de eventos fuera de orden (`TaskInboxItem.cs:92-96`) | No — sin evidencia | Parcial — mismo patrón sample host | Parcial — buen perfil de consumo, pero al igual que Notifications tiene `ProjectReference` de compilación directa a `BitCode.Platform.Workflow` — el boundary de contrato todavía no está limpio, sería trabajo de F9-02 |
| **Workflow** | No — sin evidencia cuantitativa real, aunque es el módulo con más actividad estructural (46 archivos `.cs`, motor de estados/transiciones/escalamiento, el mayor de los 12) | Sí — `WorkflowDbContext` es dueño exclusivo de su esquema, documentado explícitamente ("ningún otro módulo de plataforma debe leer/escribir estas tablas directamente", `WorkflowDbContext.cs:10-17`) | **Sí** — 6 eventos de integración con `SchemaVersion`, y es el **único** módulo de los 12 cuyos eventos tienen consumidores reales demostrados, en 3 módulos distintos (TaskInbox, Notifications, Reporting) | **Sí** — es la **fuente** del único flujo de eventual consistency realmente ejercitado en el framework (fan-out real a 3 consumidores vía Outbox/Inbox) | No — sin evidencia | Parcial — mismo patrón sample host, sin infra de despliegue propia todavía | Parcial — es el módulo con el boundary más *probado* (hay consumidores reales contra los cuales validar que la extracción no los rompe, que es literalmente el objetivo de Fase 9), pero también el de **mayor radio de impacto**: 2 de sus 3 consumidores (TaskInbox, Notifications) todavía tienen `ProjectReference` de compilación directa a Workflow, acoplamiento que F9-02 debe eliminar antes de continuar |

### Resumen cuantitativo (conteo de "Sí" por módulo, de 7 criterios posibles)

| Módulo | Sí | Parcial | No |
|---|---|---|---|
| Workflow | 3 | 3 | 1 |
| Reporting | 3 | 3 | 1 |
| TaskInbox | 2 | 4 | 1 |
| Notifications | 2 | 4 | 1 |
| Catalogs | 0 | 3 | 4 |
| Documents | 0 | 3 | 4 |
| FeatureManagement | 0 | 3 | 4 |
| ImportExport | 0 | 4 | 3 |
| IntegrationHub | 0 | 4 | 3 |
| Organization | 0 | 4 | 3 |
| Dashboard | 0 | 3 | 4 |
| Identity | 0 | 3 | 4 |

## Decisión (propuesta, no ejecutable sin aprobación humana)

**Ningún módulo cumple hoy los 7 criterios de elegibilidad de forma estricta** — y, como se explicó en
"Limitación estructural", dos de los 7 (escala cuantitativa, equipo/owner operativo) no pueden cumplirse
honestamente para ningún módulo mientras el proyecto no tenga tráfico productivo real ni una
organización con equipos asignados. Forzar una recomendación que ignore esto sería impreciso.

Dicho eso, dentro del conjunto de 12, **Workflow** y **Reporting** tienen el perfil relativo más sólido,
por ser los únicos con un contrato ya *ejercitado* por un consumidor real (no solo declarado):

- **Workflow** es la única fuente real de fan-out de eventos de integración del framework (3
  consumidores reales: TaskInbox, Notifications, Reporting) — extraerlo es la prueba más exigente y más
  representativa del objetivo de Fase 9 ("demostrar que un módulo elegible puede extraerse sin
  reescribir a sus consumidores"), precisamente porque ya existen consumidores reales contra los que
  validar eso. Es también, por el mismo motivo, el de mayor radio de impacto si algo sale mal.
- **Reporting** tiene el boundary más limpio de los dos (sin `ProjectReference` de compilación directa
  hacia el módulo que consume, a diferencia de TaskInbox/Notificaciones hacia Workflow) y ya es
  consumido por otro módulo (Dashboard) vía HTTP con resiliencia — un patrón de acoplamiento más
  parecido al que tendría un servicio realmente extraído.

**Recomendación propuesta (sujeta a aprobación humana):** si se decide continuar con Fase 9 como
ejercicio de **demostración de capacidad técnica** (que es el objetivo textual de la fase, no una
extracción motivada por una necesidad de negocio real todavía inexistente), **Workflow** es el candidato
recomendado para F9-02 en adelante, condicionado explícitamente a resolver primero, dentro de F9-02
("Contract boundary — eliminar accesos directos y estabilizar contratos"), el acoplamiento de
compilación de TaskInbox y Notifications hacia `BitCode.Platform.Workflow` (hoy `ProjectReference`
directo, debería pasar a depender solo de un paquete de contratos de eventos compartido, sin referenciar
el ensamblado completo del módulo).

Es igualmente válido, y esta ADR no descarta, que la persona responsable decida **no continuar con
Fase 9 todavía** — por ejemplo, esperando a que exista al menos un consumidor productivo real y datos de
carga reales antes de invertir en F9-02 en adelante — dado que 2 de los 7 criterios de elegibilidad no
tienen hoy ninguna evidencia real que los respalde, para ningún módulo.

## Alternativas consideradas

- **Elegir un módulo "de cero riesgo" sin consumidores reales** (por ejemplo Catalogs o
  FeatureManagement): descartado como recomendación principal porque, sin consumidores reales, F9-02 en
  adelante no podría demostrar el criterio central de Fase 9 ("sin reescribir a sus consumidores") — no
  habría a quién no reescribir. Sí serían opciones más seguras si el objetivo fuera minimizar el radio de
  impacto de un primer experimento, a costa de que la prueba sea menos representativa.
- **Elegir Identity u Organization** por ser conceptualmente "core": descartados explícitamente — ambos
  son dependencias transversales síncronas (autenticación y tenancy) de las que dependen los 12 módulos;
  extraerlos primero maximiza el radio de impacto sin que exista todavía el patrón de eventual
  consistency necesario para tolerar su indisponibilidad temporal.
- **No recomendar ningún módulo y declarar la fase bloqueada:** se consideró, dado que 2 de los 7
  criterios no tienen evidencia real. Se descarta como única salida porque el objetivo de Fase 9,
  leído literalmente, es "demostrar capacidad" — algo que sí puede evaluarse con los 5 criterios
  técnicos restantes, dejando expresamente documentado (como hace este ADR) que los otros 2 son, por
  ahora, indemostrables para cualquier módulo.

## Riesgos

- **Riesgo de sobre-interpretar esta ADR como luz verde para F9-02.** Mitigación: esta ADR queda en
  `Proposed`, con una sección explícita más abajo citando el criterio de aceptación literal de F9-01.
- **Riesgo de que Workflow, al ser el de mayor fan-out real, cause una regresión visible en 3 módulos
  distintos si F9-02/F9-03 se ejecutan mal.** Mitigación: el propio backlog de Fase 9 ya secuencia
  "Contract boundary" (F9-02) y "Data ownership" (F9-03) antes de cualquier cambio de runtime — este ADR
  no propone saltarse esa secuencia.
- **Riesgo de que "ningún módulo cumple estrictamente" se lea como que el framework está mal diseñado.**
  No es así: la ausencia de evidencia de escala/equipo es esperable en un framework de referencia sin
  despliegue productivo, no un defecto de arquitectura — los 5 criterios técnicos restantes sí muestran
  diferencias reales y verificables entre módulos, que es lo que esta matriz aprovecha.
- **Riesgo de decisión unilateral por un agente.** Mitigación explícita: ver la sección siguiente.

## Aprobación humana recibida — F9-02 habilitada

El criterio de aceptación literal de F9-01 en `docs/plan-maestro-bitcode-ia.md` (Fase 9, backlog) es
**"Aprobación humana"**. Esa aprobación ya se registró arriba (sección "Aprobación"): Javier León eligió
Workflow como módulo piloto el 2026-09-12. En consecuencia:

- Este ADR pasa a `Accepted`.
- F9-02 (Contract boundary) queda habilitada, con la condición explícita fijada en la recomendación
  original de este ADR: antes de cualquier otro cambio, eliminar el `ProjectReference` directo que
  `BitCode.Platform.TaskInbox` y `BitCode.Platform.Notifications` tienen hoy contra
  `BitCode.Platform.Workflow`, reemplazándolo por dependencia solo hacia un paquete/ensamblado de
  contratos de eventos compartido (sin referenciar el módulo completo).
- Ningún módulo fue tocado, movido ni preparado para extracción como parte de F9-01: esa tarea fue
  puramente de análisis y documentación. El trabajo de código empieza recién en F9-02.

## Referencias

- [`plan-maestro-bitcode-ia.md`](../plan-maestro-bitcode-ia.md) — Fase 9, "Criterios de elegibilidad" y
  fila F9-01 del backlog.
- [`docs/convenciones.md`](../convenciones.md) — reglas duras de acceso a datos y comandos/queries
  verificadas al inspeccionar el código de los 12 módulos (ningún join cross-módulo encontrado).
- `src/Platform/BitCode.Platform.Workflow/` — módulo con el fan-out de eventos real más amplio.
- `src/Platform/BitCode.Platform.Reporting/`, `src/Platform/BitCode.Platform.TaskInbox/`,
  `src/Platform/BitCode.Platform.Notifications/` — consumidores reales de eventos de Workflow.
- `src/Shared.Application/Eventing/IIntegrationEvent.cs` — contrato de evento con `SchemaVersion`
  explícito (versionado de eventos, criterio 3 de la matriz).
- [ADR 0005](0005-mensajeria-kafka.md) — Kafka como broker aceptado, base de la mensajería que sostiene
  el criterio "tolera consistencia eventual".
- `docker/sample-api/Dockerfile`, `k8s/sample-api/` — único host con infraestructura de despliegue
  containerizada hoy (el host base de Fase 4); ninguno de los 12 módulos de plataforma tiene todavía
  `Dockerfile`/manifiesto K8s propio, evidencia usada en el criterio 6 de la matriz.
