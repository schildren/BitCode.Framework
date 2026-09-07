# Plan Maestro de Evolución de BitCode

## Plataforma empresarial de misión crítica desarrollada con asistencia de IA

**Versión:** 1.0  
**Fecha:** 6 de septiembre de 2026  
**Estado:** Propuesta para aprobación y ejecución  
**Arquitectura base:** Monolito modular, integración orientada a eventos y extracción selectiva de microservicios  
**Stack objetivo:** .NET 10 LTS, ASP.NET Core, Angular 22, SQL Server, HybridCache, Valkey, Kafka, Quartz, YARP, OpenTelemetry y Kubernetes

---

## 1. Propósito

Este documento convierte la propuesta arquitectónica de BitCode en un plan ejecutable por una IA de desarrollo. Define el orden de implementación, las dependencias, los productos esperados, los controles de calidad y los criterios que deben cumplirse antes de avanzar entre fases.

El resultado esperado es evolucionar BitCode.Framework hacia una plataforma empresarial que permita construir aplicaciones:

- Seguras bajo un modelo Zero Trust.
- Escalables horizontalmente.
- Disponibles bajo un SLA objetivo de 99,99 %.
- Observables y auditables.
- Preparadas para recuperación ante desastres.
- Capaces de integrar bounded contexts mediante eventos.
- Productivas para equipos backend y frontend.
- Aptas para extraer microservicios únicamente cuando exista una justificación técnica y operativa.

Este plan no autoriza a la IA a implementar todas las fases de una sola vez. Cada fase se ejecutará como un conjunto controlado de épicas y tareas, con revisión humana en los puntos de decisión indicados.

---

## 2. Decisiones arquitectónicas rectoras

| Tema | Decisión |
|---|---|
| Modelo inicial | Monolito modular |
| Integración entre bounded contexts | Event-Driven |
| Microservicios | Extracción selectiva, nunca obligatoria |
| Consistencia interna | ACID dentro de cada bounded context |
| Consistencia entre contextos | Eventual Consistency |
| Transacciones distribuidas | Prohibidas |
| Persistencia predeterminada | SQL Server |
| Cache distribuido | Abstracción neutral; Valkey como proveedor inicial |
| Mensajería | Kafka |
| Seguridad de usuarios | OIDC/OAuth2 con Authorization Code y PKCE; BFF para aplicaciones críticas |
| Seguridad entre servicios | Workload identity, OAuth2 Client Credentials y mTLS cuando existan servicios separados |
| Autorización | RBAC más ABAC |
| Gateway | YARP |
| Observabilidad | OpenTelemetry y Serilog |
| Scheduler | Quartz con persistencia y clustering para trabajos críticos |
| Ejecución | Contenedores sobre Kubernetes |
| Multi-región | Cómputo activo/activo y un único propietario de escritura por agregado o bounded context |
| Licenciamiento propuesto | Open-Core: núcleo Apache-2.0 y capacidades empresariales bajo licencia propietaria |

### 2.1 Regla de consistencia

La IA deberá aplicar la siguiente regla sin excepción:

> ACID dentro de un bounded context. Eventual Consistency entre bounded contexts.

Una llamada HTTP, publicación a un broker o interacción con un sistema externo no deberá ejecutarse dentro de una transacción SQL prolongada. La transacción persistirá el cambio de negocio y el evento Outbox; el procesamiento externo ocurrirá después del commit.

### 2.2 Regla para microservicios

Un módulo solo podrá proponerse como microservicio si demuestra al menos una necesidad real de:

- Escalabilidad independiente.
- SLA independiente.
- Aislamiento de fallos.
- Aislamiento regulatorio o de seguridad.
- Ciclo de despliegue independiente.
- Tecnología especializada.
- Propiedad de datos claramente separada.

Además, deberá tener contratos versionados, eventos de integración, observabilidad, operación y ownership definidos. La existencia de un módulo distinto no constituye por sí sola una justificación.

---

## 3. Modelo de ejecución por IA

### 3.1 Responsabilidad de la IA

Para cada tarea, la IA deberá:

1. Inspeccionar el código, pruebas, documentación y configuración afectados.
2. Identificar convenciones existentes y cambios no relacionados que deban preservarse.
3. Proponer un plan breve y verificable.
4. Implementar el cambio mínimo completo.
5. Crear o actualizar pruebas.
6. Ejecutar las validaciones aplicables.
7. Documentar las decisiones y evidencia.
8. Informar riesgos, supuestos y trabajo pendiente.

### 3.2 Acciones prohibidas

La IA no deberá:

- Reescribir módulos completos sin justificación.
- Introducir una dependencia sin revisar licencia, mantenimiento, seguridad y compatibilidad.
- Cambiar contratos públicos sin análisis de compatibilidad.
- Mezclar refactorizaciones ajenas a la tarea.
- Declarar una tarea finalizada con pruebas fallidas.
- Deshabilitar controles de calidad para lograr que el pipeline apruebe.
- Guardar secretos, tokens o certificados en el repositorio.
- Prometer exactamente una vez de extremo a extremo en mensajería.
- Usar cache como fuente de verdad para saldos, ledger, auditoría o transacciones.
- Crear microservicios antes de demostrar sus límites de datos y operación.
- Avanzar a la siguiente fase si el gate vigente no fue aprobado.

### 3.3 Ciclo obligatorio por tarea

| Paso | Acción | Evidencia mínima |
|---|---|---|
| 1. Descubrimiento | Revisar solución, dependencias, pruebas y documentación | Archivos y componentes afectados |
| 2. Diseño | Definir comportamiento, contratos, riesgos y pruebas | Plan de implementación |
| 3. Implementación | Realizar cambios pequeños y cohesionados | Diff acotado y trazable |
| 4. Verificación | Ejecutar build, análisis y pruebas | Resultado reproducible |
| 5. Documentación | Actualizar ADR, guía o referencia cuando corresponda | Documento actualizado |
| 6. Cierre | Comparar resultado con criterios de aceptación | Checklist completo |

### 3.4 Definition of Ready

Una tarea puede comenzar cuando:

- Tiene objetivo, alcance y exclusiones explícitos.
- Se conocen los módulos afectados.
- Sus dependencias previas están completadas.
- Los criterios de aceptación son comprobables.
- Se identificó si cambia una API, esquema, evento o paquete.
- Se definió la estrategia de prueba.
- Las decisiones pendientes de negocio o arquitectura fueron resueltas por una persona responsable.

### 3.5 Definition of Done

Una tarea se considera terminada cuando:

- Compila sin errores ni nuevas advertencias injustificadas.
- Supera pruebas unitarias, de integración, arquitectura y contrato aplicables.
- No degrada el benchmark por encima del umbral aprobado.
- Mantiene compatibilidad o documenta la ruptura y migración.
- Incluye telemetría y manejo de errores cuando corresponde.
- No expone secretos ni datos personales.
- Cumple la política de dependencias y licencias.
- Actualiza documentación y ejemplos.
- Incluye evidencia de ejecución.
- No deja marcadores temporales sin un ticket vinculado.

### 3.6 Formato de reporte por tarea

La IA entregará siempre:

| Campo | Contenido |
|---|---|
| Tarea | Identificador y nombre |
| Estado | Completada, parcial o bloqueada |
| Cambios | Archivos y componentes modificados |
| Decisiones | Razón técnica de las decisiones relevantes |
| Pruebas | Comandos ejecutados y resultado |
| Rendimiento | Comparación cuando afecte hot paths |
| Seguridad | Controles revisados |
| Riesgos | Riesgos residuales |
| Pendientes | Trabajo explícitamente fuera de alcance |

---

## 4. Arquitectura objetivo por capas

| Capa | Responsabilidad | Componentes principales |
|---|---|---|
| Aplicaciones de negocio | Lógica específica de seguros, pagos, RR. HH., compras y otros dominios | Módulos de negocio |
| BitCode Enterprise Platform | Capacidades empresariales reutilizables | Identity Administration, Organization, Workflow, Documents, Audit, Configuration, Notifications, Integrations y Reporting |
| BitCode Framework Core | Building blocks técnicos | Kernel, CQRS, Result, Persistence, Specifications, Transactions, Cache, Observability, Resilience, Outbox, Inbox, Idempotency y Testing |
| Runtime e infraestructura | Ejecución, datos, seguridad y operación | Kubernetes, SQL Server, Valkey, Kafka, IdP, Storage, KMS, Gateway, WAF y OpenTelemetry Collector |

### 4.1 Estado funcional inicial

| Capacidad | Estado inicial estimado | Tratamiento |
|---|---|---|
| Shared Kernel | Disponible | Mantener y estabilizar |
| Repository y Specification | Disponible | Mantener y optimizar casos críticos |
| CQRS y MediatR | Disponible | Mantener; permitir bypass en hot paths justificados |
| Transaction Behavior | Parcial | Rediseñar para transacciones explícitas |
| Multi-tenancy | Riesgo de rendimiento | Rediseñar y benchmarkear |
| JWT propio | Insuficiente como default empresarial | Reemplazar por integración OIDC/OAuth2 |
| Permisos | RBAC inicial | Extender a RBAC más ABAC |
| HybridCache | Disponible | Mantener con proveedor L2 intercambiable |
| Quartz | Base local | Agregar persistencia, clustering e idempotencia |
| OpenTelemetry | Base disponible | Completar cobertura y estándares |
| Messaging | Ausente | Construir |
| Outbox, Inbox e Idempotency | Ausentes | Construir |
| API Gateway | Ausente | Integrar YARP |
| Auditoría inmutable | Ausente | Construir |
| HA, DR y multi-región | Ausentes | Diseñar, automatizar y probar |
| Supply-chain security | Parcial | Industrializar |
| Licencia del producto | Pendiente de confirmación | Resolver antes de publicar artefactos |

---

## 5. Mapa de fases y dependencias

| Fase | Nombre | Dependencia principal | Resultado |
|---:|---|---|---|
| 0 | Gobierno y línea base | Ninguna | Decisiones, métricas y políticas aprobadas |
| 1 | BitCode Core 2.0 | Fase 0 | Núcleo estable, performante y versionable |
| 2 | Security 2.0 | Fase 1 | Zero Trust, RBAC, ABAC y auditoría |
| 3 | Event Platform | Fases 1 y 2 | Comunicación confiable por eventos |
| 4 | Runtime de alta disponibilidad | Fases 1 a 3 | Ejecución escalable y tolerante a fallos |
| 5 | DR y multi-región | Fase 4 | Recuperación demostrada y ownership regional |
| 6 | Plataforma funcional empresarial | Fases 1 a 5 | Capacidades empresariales reutilizables |
| 7 | Plataforma Angular empresarial | Fases 2 y 6 | Frontend alineado con contratos y seguridad |
| 8 | Developer Experience y productización | Fases 1 a 7 | Golden paths, paquetes y plantillas |
| 9 | Capacidad de extracción de microservicios | Fases 3 a 8 | Extracción controlada sin reescritura |
| 10 | Certificación y liberación | Todas | Release productivo, operable y auditable |

### 5.1 Regla de avance

Las fases pueden ejecutar tareas internas en paralelo únicamente cuando no comparten contratos inestables. Ninguna fase dependiente podrá cerrar antes de que la fase proveedora haya superado su gate.

---

## 6. Plan detallado por fases

### Fase 0 — Gobierno, arquitectura y línea base

#### Objetivo

Establecer decisiones, métricas y controles reproducibles antes de modificar componentes transversales.

#### Precondiciones

- Acceso al repositorio completo y a su historial.
- Acceso al pipeline actual.
- Ambiente reproducible de pruebas.
- Responsables de arquitectura, seguridad, infraestructura y producto identificados.

#### Backlog

| ID | Trabajo | Actividades de IA | Entregable | Validación |
|---|---|---|---|---|
| F0-01 | Inventario técnico | Mapear soluciones, proyectos, paquetes, dependencias, pruebas, pipelines y documentación | Inventario versionado | Revisión de arquitectura |
| F0-02 | Mapa de capacidades | Relacionar capacidad actual, brecha, criticidad y fase objetivo | Matriz de brechas | Sin capacidades huérfanas |
| F0-03 | Principios de arquitectura | Formalizar modularidad, consistencia, seguridad, observabilidad y compatibilidad | Architecture Principles | Aprobación humana |
| F0-04 | ADR iniciales | Crear ADR para arquitectura, persistencia, tenancy, identidad, mensajería, cache, gateway y licencias | Conjunto de ADR | Estado Accepted o Proposed con responsable |
| F0-05 | Contratos de compatibilidad | Definir SemVer, deprecación, versionado de API, eventos, esquemas y paquetes | Política de versionado | Casos de ejemplo aprobados |
| F0-06 | Política de dependencias | Definir licencias permitidas, proceso de excepción, SCA, CVE y actualización | Política de dependencias | Aplicada en CI |
| F0-07 | Threat model | Modelar activos, actores, fronteras de confianza, amenazas y mitigaciones | Threat model | Revisión de seguridad |
| F0-08 | SLI, SLO y SLA | Definir disponibilidad, latencia, error rate, throughput, RPO y RTO por perfil | Catálogo SLO/SLA | Dueños y cálculo documentados |
| F0-09 | Entorno de referencia | Describir CPU, memoria, red, SQL, datos y configuración para benchmarks | Reference Performance Environment | Reproducible |
| F0-10 | Línea base | Medir build, pruebas, RPS, TPS, p50, p95, p99, CPU, memoria, GC, conexiones y consultas | Informe baseline | Tres ejecuciones comparables |
| F0-11 | Estructura de documentación | Ordenar guías, ADR, runbooks, contratos, ejemplos y reportes | Portal o índice documental | Enlaces sin errores |
| F0-12 | Registro de riesgos | Crear riesgos, probabilidad, impacto, mitigación y owner | Risk register | Revisado por responsables |

#### Pruebas obligatorias

- Build limpio de toda la solución.
- Ejecución completa de pruebas actuales.
- BenchmarkDotNet para componentes internos.
- k6 para APIs de referencia.
- Captura de planes y métricas SQL.
- Medición de consumo de memoria y asignaciones.

#### Gate de salida

- [ ] Arquitectura objetivo aprobada.
- [ ] ADR críticos registrados.
- [ ] Línea base reproducible.
- [ ] Métricas y hardware de referencia documentados.
- [ ] Políticas de versión, licencia y dependencias aprobadas.
- [ ] Threat model revisado.
- [ ] Riesgos P0 con plan de tratamiento.

#### Hito

> El comportamiento actual de BitCode está medido y las decisiones futuras pueden compararse contra evidencia.

---

### Fase 1 — BitCode Core 2.0

#### Objetivo

Convertir el núcleo en una base estable, compatible, eficiente y preparada para cargas críticas.

#### Backlog

##### Épica F1-A — Runtime, paquetes y compatibilidad

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F1-01 | Migración a .NET 10 LTS | Analizar compatibilidad, actualizar target frameworks, SDK, paquetes y CI | Solución migrada | Build y pruebas al 100 % |
| F1-02 | Empaquetado NuGet | Definir paquetes públicos, internos, metadatos, símbolos y Source Link | Paquetes reproducibles | Instalación en app limpia |
| F1-03 | API compatibility | Incorporar análisis de superficie pública y breaking changes | Gate de compatibilidad | Rupturas bloqueadas o aprobadas |
| F1-04 | Versionado | Aplicar SemVer y versionado automatizado | Paquetes versionados | Versiones trazables a source |
| F1-05 | Matriz de soporte | Definir runtime, SQL, cache, broker y sistema operativo soportados | Documento de soporte | Validado en CI |

##### Épica F1-B — Transacciones y concurrencia

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F1-06 | Contratos de comando | Separar ICommand, ITransactionalCommand e IIdempotentCommand | Contratos públicos | Solo comandos explícitos abren transacción |
| F1-07 | Transaction Behavior | Reducir duración, evitar llamadas externas y controlar SaveChanges | Pipeline transaccional | Rollback verificado |
| F1-08 | Concurrencia optimista | Incorporar tokens de concurrencia y manejo uniforme de conflictos | Primitive y ProblemDetails | Conflictos devuelven respuesta definida |
| F1-09 | Unit of Work | Revisar ownership, nesting y límites transaccionales | Implementación estabilizada | Sin commits implícitos inesperados |
| F1-10 | Timeouts y cancelación | Propagar CancellationToken y límites de tiempo | Convención transversal | Requests cancelados liberan recursos |

##### Épica F1-C — Multi-tenancy

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F1-11 | Benchmark del modelo actual | Medir compilación de modelos, memoria, startup y throughput por tenant | Informe comparativo | Datos suficientes para ADR |
| F1-12 | Estrategia T1 | Implementar Shared DB más TenantId con filtros parametrizados | Provider y filtros | Cero fuga entre tenants |
| F1-13 | Estrategia T2 | Diseñar routing a shards por grupos de tenants | Contratos y prototipo | Resolución determinística |
| F1-14 | Estrategia T3 | Diseñar base dedicada por tenant | Contratos y operación | Provisioning documentado |
| F1-15 | Tenant context | Unificar resolución, propagación, validación y logging | ITenantContext estable | Tenant no puede sobrescribirse desde payload |
| F1-16 | Pruebas de aislamiento | Crear pruebas negativas, concurrencia y contaminación de cache | Suite automatizada | Ninguna lectura cruzada |

##### Épica F1-D — Persistencia y rendimiento

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F1-17 | Queries eficientes | Establecer AsNoTracking, proyección, selección y paginación | Guía y analyzers/tests | Sin entidades completas innecesarias |
| F1-18 | Hot paths | Permitir queries y repositorios especializados | Contratos de extensión | Benchmark justifica cada bypass |
| F1-19 | DbContext pooling | Evaluar compatibilidad con tenancy y estado | ADR y configuración | Sin contaminación de estado |
| F1-20 | Índices y SQL | Crear política de revisión de índices y consultas | Checklist y pruebas | Planes sin regresiones críticas |
| F1-21 | Paginación | Incorporar límites máximos y cursores donde aplique | Primitive común | Ningún endpoint ilimitado |

##### Épica F1-E — Primitivas de confiabilidad

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F1-22 | Idempotencia API | Soportar Idempotency-Key, hash, resultado y expiración | Middleware y store SQL | POST repetido no duplica operación |
| F1-23 | Outbox base | Persistir evento junto al cambio de negocio | Modelo y writer | Evento no se pierde tras commit |
| F1-24 | Inbox base | Registrar y deduplicar mensajes recibidos | Modelo y processor | Duplicados descartados |
| F1-25 | Health checks | Separar liveness y readiness | Endpoints estándar | Dependencias críticas evaluadas correctamente |
| F1-26 | Resiliencia HTTP | Incorporar timeout, retry con jitter, circuit breaker y bulkhead | Policies configurables | Retry solo en operaciones seguras |
| F1-27 | API versioning | Definir versión, deprecación y headers | Convención y middleware | Versiones coexistentes |
| F1-28 | OpenAPI | Estandarizar errores, seguridad, examples y generación | Contrato OpenAPI | Validación automática |

#### Pruebas obligatorias

- Unitarias de cada primitive.
- Integración con SQL Server real mediante Testcontainers.
- Aislamiento multi-tenant.
- Conflictos de concurrencia.
- Idempotencia bajo solicitudes simultáneas.
- Fallo entre actualización de negocio y commit.
- Cancelación y timeout.
- Comparación de benchmark contra Fase 0.

#### Gate de salida

- [ ] Toda la solución ejecuta sobre .NET 10 LTS.
- [ ] No existen transacciones implícitas para todo ICommand.
- [ ] Multi-tenancy supera pruebas de aislamiento y benchmark aprobado.
- [ ] Idempotencia, Outbox e Inbox tienen pruebas de fallos y concurrencia.
- [ ] API pública cuenta con control de compatibilidad.
- [ ] Health checks y resiliencia están disponibles como building blocks.
- [ ] No existe una regresión de hot path mayor al umbral aprobado.

#### Hito

> BitCode Core 2.0 es apto para aplicaciones de alta concurrencia y puede evolucionar sin romper consumidores silenciosamente.

---

### Fase 2 — Security 2.0 y auditoría

#### Objetivo

Adoptar Zero Trust, delegar autenticación a un IdP y ofrecer autorización fina, secretos seguros y auditoría resistente a manipulación.

#### Backlog

##### Épica F2-A — Identidad y autenticación

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F2-01 | Integración IdP | Definir Entra ID, Keycloak u otro proveedor aprobado | Adapter OIDC/OAuth2 | Proveedor intercambiable por configuración |
| F2-02 | Usuarios web | Implementar Authorization Code más PKCE | Flujo de autenticación | Sin flujo implícito ni credenciales en SPA |
| F2-03 | BFF | Diseñar BFF para aplicaciones Angular críticas | Host BFF | Tokens no quedan expuestos al navegador |
| F2-04 | Identidad de servicio | Implementar Client Credentials o workload identity | Contratos y ejemplo | Cada workload tiene identidad propia |
| F2-05 | Validación de tokens | Validar issuer, audience, firma, vigencia y clock skew | Middleware | Casos negativos automatizados |
| F2-06 | Rotación y revocación | Diseñar rotación, JWKS, sesiones y revocación | Política y componentes | Rotación sin downtime |

##### Épica F2-B — Autorización

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F2-07 | RBAC 2.0 | Normalizar roles, permisos, scopes y tenancy | Modelo y evaluador | Permisos efectivos trazables |
| F2-08 | ABAC | Crear Subject, Resource, Action y Context | IAuthorizationPolicyEvaluator | Reglas por monto, empresa y sucursal |
| F2-09 | Cache de permisos | Implementar L1/L2 e invalidación por evento | Permission cache | Sin consulta SQL por request normal |
| F2-10 | Operaciones privilegiadas | Definir step-up, reevaluación y segregación de funciones | Policies | Operaciones críticas protegidas |
| F2-11 | Pruebas de autorización | Crear matriz allow/deny y pruebas de bypass | Suite automática | Default deny comprobado |

##### Épica F2-C — Secretos y criptografía

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F2-12 | Secret provider | Integrar Vault, Key Vault o KMS aprobado | Abstracción y provider | Cero secretos en repositorio |
| F2-13 | Cifrado | Definir datos que requieren cifrado y gestión de claves | Política criptográfica | Rotación y recuperación probadas |
| F2-14 | mTLS contracts | Preparar identidad y certificados entre servicios | Contratos operativos | Aplicable al extraer servicios |

##### Épica F2-D — Auditoría inmutable

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F2-15 | Modelo de audit | Registrar actor, tenant, acción, recurso, hashes y trazas | Esquema append-only | Campos críticos completos |
| F2-16 | Cadena de integridad | Encadenar PreviousAuditHash y AuditHash | Servicio de integridad | Manipulación detectable |
| F2-17 | Firma y timestamp | Integrar firma de lotes o eventos | Mecanismo aprobado | Verificación independiente |
| F2-18 | WORM | Exportar a almacenamiento inmutable | Pipeline de retención | Escritura y lectura probadas |
| F2-19 | PII y redacción | Clasificar y evitar datos sensibles innecesarios | Política y filtros | Logs sin PII no autorizada |
| F2-20 | Consulta de auditoría | Exponer búsquedas controladas y exportación | API administrativa | Acceso auditado y paginado |

#### Pruebas obligatorias

- Token válido, expirado, alterado, issuer incorrecto y audience incorrecto.
- Matriz RBAC/ABAC positiva y negativa.
- Aislamiento de permisos por tenant.
- Invalidación de cache de permisos.
- Rotación de clave sin indisponibilidad.
- Detección de alteración de auditoría.
- Fallo del destino WORM con reintento seguro.
- Escaneo de secretos y dependencias.

#### Gate de salida

- [ ] OIDC/OAuth2 es el mecanismo predeterminado.
- [ ] Los tokens empresariales usan firma asimétrica.
- [ ] RBAC y ABAC están disponibles y aplican default deny.
- [ ] Secretos externos al repositorio.
- [ ] Operaciones críticas generan auditoría íntegra.
- [ ] Las amenazas críticas del threat model tienen mitigación.
- [ ] SAST, SCA y secret scanning forman parte del pipeline.

#### Hito

> BitCode cumple el modelo Zero Trust y puede demostrar quién hizo qué, sobre qué recurso y bajo qué contexto.

---

### Fase 3 — Plataforma de eventos

#### Objetivo

Permitir comunicación confiable, versionada y observable entre bounded contexts sin transacciones distribuidas.

#### Backlog

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F3-01 | Contratos | Crear IIntegrationEvent, IEventPublisher e IEventConsumer | Paquete de contratos | Sin dependencia al proveedor |
| F3-02 | Provider Kafka | Configurar producer, consumer, serializers y autenticación | Adapter Kafka | Pruebas con broker real |
| F3-03 | Outbox Publisher | Leer por lotes, bloquear, publicar y marcar | Worker | Reinicio no pierde eventos |
| F3-04 | Inbox Consumer | Deduplicar y coordinar procesamiento local | Consumer base | Duplicados no repiten efectos |
| F3-05 | Particionamiento | Definir AggregateId o TenantId según orden requerido | Estándar de keys | Orden demostrado por partición |
| F3-06 | Schema y versionado | Definir compatibilidad forward/backward y evolución | Política de eventos | Contratos validados en CI |
| F3-07 | Retries | Clasificar errores transitorios y permanentes | Policies | Backoff con jitter y límites |
| F3-08 | DLQ | Crear dead-letter topics, metadatos y reprocess | Runbook y tooling | Reprocesamiento auditado |
| F3-09 | Poison messages | Aislar mensajes inválidos sin bloquear partición | Handler | Consumer continúa operando |
| F3-10 | Observabilidad | Métricas de publish, consume, error, lag y DLQ | Dashboard y alertas | Correlación end-to-end |
| F3-11 | Seguridad | Aplicar TLS, ACL, identidad y mínimo privilegio | Configuración | Acceso cruzado denegado |
| F3-12 | Event catalog | Documentar owner, schema, versión, PII y consumidores | Catálogo | Todo evento productivo registrado |
| F3-13 | Prueba de referencia | Implementar flujo completo entre dos módulos | Ejemplo ejecutable | Consistencia eventual comprobada |

#### Semántica exigida

- Entrega: al menos una vez.
- Procesamiento: idempotente.
- Confirmación: únicamente después de persistir el efecto o Inbox.
- Orden: solo garantizado dentro de la partición definida.
- Reprocesamiento: soportado y auditado.
- Exactly-once end-to-end: no se promete.

#### Pruebas obligatorias

- Broker temporalmente no disponible.
- Caída del Outbox Processor antes y después del publish.
- Mensaje duplicado.
- Mensaje fuera de orden donde corresponda.
- Consumer reiniciado durante el procesamiento.
- Schema compatible e incompatible.
- Saturación, lag y recuperación.
- Reprocesamiento desde DLQ.

#### Gate de salida

- [ ] Un cambio de negocio y su evento Outbox se confirman atómicamente.
- [ ] La caída del broker no pierde eventos.
- [ ] Los consumidores toleran duplicados.
- [ ] Existe política de versiones y catálogo.
- [ ] Lag, errores y DLQ son observables.
- [ ] No existe dependencia de dominio hacia Kafka.

#### Hito

> Los bounded contexts se integran de forma confiable sin transacciones distribuidas.

---

### Fase 4 — Runtime de alta disponibilidad

#### Objetivo

Ejecutar BitCode como workload stateless, escalable y tolerante a la pérdida de instancias o nodos.

#### Backlog

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F4-01 | Contenedores | Crear imágenes mínimas, non-root y reproducibles | Containerfiles | Escaneo sin CVE crítica |
| F4-02 | Manifiestos | Crear Helm o Kustomize por ambiente | Paquetes de despliegue | Configuración validada |
| F4-03 | Stateless | Eliminar estado local requerido y sticky sessions | Runtime stateless | Pod reemplazable |
| F4-04 | Probes | Implementar startup, liveness y readiness | Probes | Sin bucles de reinicio por dependencia no crítica |
| F4-05 | Shutdown | Drenar tráfico y terminar jobs/consumers correctamente | Lifecycle hooks | Sin requests o mensajes perdidos |
| F4-06 | HPA | Escalar por CPU y métricas de aplicación | Política HPA | Escala bajo carga |
| F4-07 | PDB y spread | Distribuir réplicas entre nodos y zonas | Políticas | Pérdida de nodo sin caída |
| F4-08 | Gateway YARP | Routing, auth boundary, rate limits y headers | BitCode.Gateway | Overhead dentro del target |
| F4-09 | WAF y límites | Definir tamaño, rate, timeout y protección externa | Política perimetral | Casos abusivos bloqueados |
| F4-10 | OpenTelemetry Collector | Centralizar exportación de logs, métricas y trazas | Pipeline OTel | Telemetría correlacionada |
| F4-11 | Quartz HA | Persistent JobStore, cluster, misfire e idempotencia | Scheduler productivo | Un job lógico no duplica efectos |
| F4-12 | Configuración | Externalizar config y feature flags | Config provider | Cambios controlados y auditados |
| F4-13 | Despliegue gradual | Rolling, canary o blue/green según riesgo | Pipeline | Zero downtime comprobado |
| F4-14 | Capacity tests | Carga, estrés, soak y escalamiento | Informe | SLO sostenido |

#### Telemetría mínima

Cada request, comando, job y evento propagará cuando aplique:

- trace_id.
- span_id.
- correlation_id.
- tenant_id.
- user_id o workload identity.
- module.
- region.
- instance.
- operation.
- outcome.

#### Pruebas obligatorias

- Muerte de un pod de API.
- Muerte de un worker.
- Drenado de nodo.
- Escalamiento horizontal.
- Cache no disponible.
- Broker temporalmente no disponible.
- Dependencia lenta.
- Rolling deployment con tráfico.
- Soak test prolongado.
- Recuperación de Quartz en cluster.

#### Gate de salida

- [ ] La pérdida de un pod no produce interrupción observable.[^f4-gate-1]
- [ ] La pérdida de un nodo no interrumpe el servicio.[^f4-gate-2]
- [ ] El sistema escala horizontalmente con eficiencia objetivo.[^f4-gate-3]
- [ ] Los despliegues son zero-downtime.[^f4-gate-4]
- [x] Logs, métricas y trazas están correlacionados.[^f4-gate-5]
- [x] Los jobs críticos son persistentes e idempotentes.[^f4-gate-6]

[^f4-gate-1]: Evidencia real parcial (`docs/informe-capacity-tests-f4-14.md`, sección 1.1): con
    tráfico repartido entre réplicas reales, perder una instancia confina el impacto a la fracción de
    tráfico de esa instancia (0 fallos medidos en las réplicas supervivientes) — pero no se demostró
    "sin interrupción observable" en sentido literal contra un `Service`/Gateway real ni contra el
    drenado gracioso completo de un clúster real. Sin marcar hasta esa evidencia.
[^f4-gate-2]: Requiere `kubectl drain` contra un clúster Kubernetes multi-nodo real, no disponible en
    este entorno (mismo gap que F4-07 dejó explícito). Runbook exacto en
    `docs/informe-capacity-tests-f4-14.md` sección 6.1.
[^f4-gate-3]: Evidencia real de que escala (más instancias → más throughput con 0 % de errores,
    `docs/informe-capacity-tests-f4-14.md` sección 1.7), pero no se midió la eficiencia ≥ 75 % formal
    de la sección 8.1 contra el cluster de referencia (que no existe, ver `docs/entorno-referencia.md`)
    ni se usó un HPA real reaccionando a métricas (runbook en la sección 6.2 del informe).
[^f4-gate-4]: F4-13 declaró y validó sintácticamente `RollingUpdate` (`maxSurge: 1`,
    `maxUnavailable: 0`); la comprobación en ejecución real con tráfico continuo contra un clúster real
    sigue pendiente (runbook en `docs/informe-capacity-tests-f4-14.md` sección 6.3).
[^f4-gate-5]: Demostrado en ejecución real por F3-10/F4-10 (OTel Collector real + `samples/Sample.Api`
    real) — no depende de un clúster Kubernetes real, ver `docs/guia-otel-collector.md` y
    `docs/guia-observabilidad-eventos.md`.
[^f4-gate-6]: Demostrado en ejecución real por F4-11 (`AdoJobStore` clusterizado, dos schedulers
    Quartz.NET reales sobre el mismo SQL Server, sin duplicar el disparo) y confirmado de nuevo en
    F4-14 (`docs/informe-capacity-tests-f4-14.md` sección 1.2). El recovery real ante la muerte del
    nodo que ejecuta un job a mitad de la ejecución sigue sin verificarse con dos procesos de sistema
    operativo reales (gap heredado de F4-11, documentado, no bloqueante para este ítem del gate porque
    "persistente e idempotente" ya tiene evidencia real de coordinación).

#### Hito

> La falla de una instancia o nodo no interrumpe la operación.

---

### Fase 5 — Disaster Recovery y multi-región

#### Objetivo

Definir y demostrar recuperación ante fallas de zona o región, respetando las limitaciones físicas de consistencia, latencia y disponibilidad.

#### Perfiles iniciales

| Perfil | RPO objetivo | RTO objetivo | Uso |
|---|---:|---:|---|
| Standard | Hasta 15 minutos | Hasta 60 minutos | Backoffice |
| Gold | Hasta 60 segundos | Hasta 5 minutos | Operaciones críticas |
| Platinum | Cercano a cero | Hasta 1 minuto | Ledger o transacción crítica |

Los perfiles deberán recalibrarse con negocio, infraestructura y presupuesto. Platinum requiere una decisión explícita sobre replicación síncrona, consenso y latencia.

#### Backlog

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F5-01 | Business Impact Analysis | Clasificar módulos, datos y dependencias | BIA | Perfil DR asignado |
| F5-02 | Ownership regional | Asignar escritor por tenant, agregado o contexto | Mapa de ownership | Sin escrituras concurrentes ambiguas |
| F5-03 | Routing regional | Implementar afinidad y failover controlado | Política global | Requests llegan al owner |
| F5-04 | Replicación SQL | Definir topología, consistencia y failover | Arquitectura de datos | RPO medido |
| F5-05 | Replicación Kafka | Definir topics, offsets y recuperación | Arquitectura de eventos | Mensajes preservados |
| F5-06 | Cache regional | Definir warming, invalidación y degradación | Estrategia | Cache no condiciona recuperación |
| F5-07 | Backups | Automatizar full, differential, log y retención | Política y jobs | Restauración validada |
| F5-08 | PITR | Probar point-in-time restore | Runbook | Tiempo medido |
| F5-09 | Backups inmutables | Aplicar aislamiento, cifrado y WORM | Repositorio seguro | Resistencia a borrado probada |
| F5-10 | Failover automatizado | Crear automatización con controles de seguridad | Workflow | Ejecución repetible |
| F5-11 | Failback | Diseñar resincronización y retorno | Runbook | Evita split-brain |
| F5-12 | DR drills | Ejecutar simulacros programados | Reporte | RPO/RTO demostrados |
| F5-13 | Chaos regional | Simular dependencia, zona y región | Suite | Comportamiento dentro de SLO |

#### Gate de salida

- [ ] Cada módulo tiene perfil RPO/RTO.
- [ ] El propietario de escritura está definido.
- [ ] Backup y restore fueron probados.
- [ ] Failover y failback tienen runbooks ejecutables.
- [ ] No existe split-brain bajo el modelo aprobado.
- [ ] Los objetivos se demuestran con tiempos medidos.

#### Hito

> RPO y RTO están demostrados mediante pruebas, no solamente declarados en documentación.

---

### Fase 6 — Plataforma funcional empresarial

#### Objetivo

Crear capacidades empresariales reutilizables para que nuevas aplicaciones concentren su esfuerzo en la lógica de negocio.

#### Orden de implementación

Los módulos se desarrollarán como bounded contexts internos. Cada uno deberá tener ownership de datos, API, eventos, pruebas, observabilidad y documentación propios.

| Orden | Módulo | Capacidades mínimas | Dependencias |
|---:|---|---|---|
| 1 | Identity Administration | Usuarios, roles, permisos, delegaciones y sesiones | Security 2.0 |
| 2 | Organization | Empresas, sucursales, áreas, cargos y jerarquías | Core y Audit |
| 3 | Catalogs and Parameters | Catálogos versionados, parámetros y vigencia | Core |
| 4 | Feature Management | Flags, segmentos, rollout y auditoría | Core y Security |
| 5 | Documents | Metadata, versiones, almacenamiento, antivirus y retención | Security y Storage |
| 6 | Workflow | Definición, versión, estado, transición, reglas, tareas y SLA | Events, Quartz y Audit |
| 7 | Task Inbox | Asignación, bandeja, delegación, escalamiento y filtros | Workflow |
| 8 | Notifications | Plantillas, canales, preferencias, retry y tracking | Events |
| 9 | Integration Hub | Conectores, mapping, credenciales, colas y monitoreo | Events y Resilience |
| 10 | Import and Export | Validación, lotes, progreso, errores y reanudación | Documents y Events |
| 11 | Reporting | Read models, exportación y control de acceso | Events y Data |
| 12 | Dashboard | Widgets, métricas y preferencias | Reporting |

#### Requisitos comunes por módulo

- Límite de dominio explícito.
- Esquema o ownership de datos definido.
- API versionada.
- Eventos de dominio e integración.
- Idempotencia en comandos externos.
- Outbox e Inbox cuando corresponda.
- RBAC y ABAC.
- Auditoría de operaciones críticas.
- Métricas, logs y trazas.
- Migraciones compatibles.
- Pruebas unitarias, integración, contrato y seguridad.
- Guía de consumo y ejemplo.

#### Épica de Workflow

El primer alcance incluirá:

- WorkflowDefinition.
- WorkflowVersion.
- State.
- Transition.
- Rule.
- Assignment.
- Instance.
- Task.
- History.
- Aprobación y rechazo.
- Delegación y escalamiento.
- Timeout y SLA.
- Pasos paralelos.
- Pasos condicionales.

Quedan fuera del primer alcance:

- Diseñador BPMN completo.
- DSL visual de bajo código.
- Orquestación distribuida general.
- Sustitución de motores especializados para workflows extremadamente largos.

#### Épica de Documents

El primer alcance incluirá:

- Metadata y clasificación.
- Versionado.
- Hash de contenido.
- Carga y descarga segura.
- Escaneo antivirus.
- Retención y disposición.
- Autorización por recurso.
- Auditoría.
- Integración con almacenamiento desacoplado.

#### Gate de salida

- [ ] Cada módulo cumple los requisitos comunes.
- [ ] No existen dependencias circulares.
- [ ] Ningún módulo accede directamente a tablas privadas de otro módulo.
- [ ] La integración cruzada utiliza contratos o eventos aprobados.
- [ ] Workflow y Documents tienen pruebas de seguridad y recuperación.
- [ ] Existe al menos una aplicación de referencia que consume la plataforma.

#### Hito

> Una nueva aplicación desarrolla principalmente su lógica de negocio y reutiliza capacidades empresariales maduras.

---

### Fase 7 — Plataforma Angular empresarial

#### Objetivo

Crear una experiencia frontend consistente, accesible, segura y alineada con los contratos del backend.

#### Paquetes objetivo

| Paquete | Responsabilidad |
|---|---|
| @bitcode/core | Configuración, errores, logging, HTTP y convenciones |
| @bitcode/auth | Sesión, guards, claims y BFF |
| @bitcode/ui | Design system y componentes base |
| @bitcode/grid | Grillas empresariales, paginación, filtros y exportación |
| @bitcode/forms | Formularios, validación y componentes de datos |
| @bitcode/workflow | Bandeja, tareas, historial y acciones |
| @bitcode/documents | Carga, descarga, versiones y visualización |

#### Backlog

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F7-01 | Workspace | Configurar monorepo, build y versionado | Workspace Angular | Builds reproducibles |
| F7-02 | Design tokens | Colores, tipografía, espacios y estados | Tokens | Cumplimiento de línea gráfica |
| F7-03 | Autenticación | Integrar BFF/OIDC sin almacenar tokens inseguros | @bitcode/auth | Flujo seguro probado |
| F7-04 | Autorización UI | Guards y directivas RBAC/ABAC-aware | Componentes | La UI no sustituye validación backend |
| F7-05 | Menú dinámico | Generar navegación por módulos y permisos | Navigation shell | Actualización controlada |
| F7-06 | Errores | Mapear ProblemDetails y correlation id | Error experience | Mensajes consistentes |
| F7-07 | Grilla | Server paging, sorting, filtering y virtualización | @bitcode/grid | Datos masivos sin bloqueo |
| F7-08 | Forms | Validación accesible, máscaras y componentes | @bitcode/forms | Errores claros y consistentes |
| F7-09 | Workflow UI | Bandeja, detalle, acciones e historial | @bitcode/workflow | Flujos críticos completos |
| F7-10 | Documents UI | Carga segura, progreso, versiones y descarga | @bitcode/documents | Archivos grandes y errores controlados |
| F7-11 | Localización | Idiomas, fechas, moneda y zona horaria | i18n | Formatos regionales correctos |
| F7-12 | Accesibilidad | WCAG objetivo aprobado, teclado y lector | Suite a11y | Cero hallazgos críticos |
| F7-13 | Telemetría | Web vitals, errores, trazas y contexto | Frontend telemetry | Correlación con backend |
| F7-14 | Performance | Lazy loading, budgets y análisis de bundle | Budgets CI | Sin regresión fuera del umbral |
| F7-15 | Testing | Unit, component, E2E y visual regression | Suite | Flujos principales cubiertos |

#### Gate de salida

- [ ] Paquetes publicados y versionados.
- [ ] Autenticación y autorización integradas.
- [ ] Accesibilidad sin hallazgos críticos.
- [ ] Budgets de performance activos.
- [ ] Contratos TypeScript generados desde OpenAPI.
- [ ] Flujos principales cubiertos por E2E.

#### Hito

> Frontend y backend comparten contratos, seguridad, trazabilidad y convenciones.

---

### Fase 8 — Developer Experience y productización

#### Objetivo

Industrializar la creación, mantenimiento y adopción de soluciones BitCode mediante golden paths.

#### Backlog

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F8-01 | Plantilla app | Crear dotnet new bitcode-app o bitcode-enterprise | Template | Aplicación base ejecutable |
| F8-02 | Plantilla módulo | Crear dotnet new bitcode-module | Template | Módulo con límites y pruebas |
| F8-03 | Plantilla feature | Crear dotnet new bitcode-feature | Template | Vertical slice completo |
| F8-04 | Cliente TypeScript | Automatizar OpenAPI a TypeScript | Generator | Sin edición manual del cliente |
| F8-05 | Registry NuGet | Configurar publicación, firma y retención | Feed | Consumo autenticado |
| F8-06 | Registry NPM | Configurar paquetes Angular | Feed | Versiones alineadas |
| F8-07 | Migraciones | Crear tooling de generación, validación y rollout | CLI/guía | Forward y rollback ensayados |
| F8-08 | Architecture tests | Bloquear dependencias inválidas | Test suite | Violaciones fallan CI |
| F8-09 | Portal técnico | Publicar documentación, ADR, APIs y ejemplos | Portal | Búsqueda y navegación funcional |
| F8-10 | Golden paths | Documentar CRUD, workflow, events, documents e integración | Guías ejecutables | Ejemplos verificados |
| F8-11 | Entorno local | Automatizar dependencias con contenedores | Dev environment | Onboarding reproducible |
| F8-12 | CLI diagnóstico | Validar configuración, conectividad y versiones | Herramienta | Diagnóstico accionable |
| F8-13 | Release automation | Changelog, SemVer, firma y publicación | Pipeline | Release reproducible |
| F8-14 | SBOM y notices | Generar SBOM y THIRD-PARTY-NOTICES | Artefactos por release | Cobertura del 100 % |

#### Experiencia objetivo

La ejecución de la plantilla empresarial deberá producir:

- Solución compilable.
- Host backend.
- Aplicación Angular.
- Módulo de ejemplo.
- Autenticación configurada por placeholders seguros.
- Persistencia y migraciones.
- Health checks.
- OpenTelemetry.
- Pruebas base.
- Contenedores.
- Pipeline inicial.
- Documentación de arranque.

#### Gate de salida

- [ ] Una persona nueva puede levantar el entorno siguiendo la guía.
- [ ] La plantilla supera build y pruebas sin edición manual.
- [ ] Los paquetes tienen versionado, firma y provenance.
- [ ] El pipeline produce SBOM, license report y vulnerabilidades.
- [ ] Los golden paths están cubiertos por pruebas de humo.

#### Hito

> Crear una solución empresarial BitCode es un proceso repetible, seguro y documentado.

---

### Fase 9 — Capacidad de extracción de microservicios

#### Objetivo

Demostrar que un módulo elegible puede extraerse como servicio independiente sin reescribir a sus consumidores.

#### Criterios de elegibilidad

| Pregunta | Requisito |
|---|---|
| ¿Requiere escala o SLA independiente? | Evidencia cuantitativa |
| ¿Tiene ownership de datos claro? | Tablas y migraciones privadas |
| ¿Tiene contrato estable? | API y eventos versionados |
| ¿Tolera consistencia eventual? | Casos y compensaciones definidos |
| ¿Tiene equipo u owner operativo? | Responsabilidad explícita |
| ¿Puede desplegarse y observarse solo? | Pipeline, SLO, dashboards y alertas |
| ¿La separación reduce más riesgo del que agrega? | ADR aprobado |

#### Backlog

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F9-01 | Selección piloto | Evaluar módulos con matriz cuantitativa | ADR de selección | Aprobación humana |
| F9-02 | Contract boundary | Eliminar accesos directos y estabilizar contratos | Boundary limpio | Architecture tests |
| F9-03 | Data ownership | Separar esquema, migraciones y acceso | Store propio | Sin joins entre bases |
| F9-04 | Eventos | Completar eventos, Inbox, Outbox y compensaciones | Contratos | Flujo eventual probado |
| F9-05 | Host independiente | Crear runtime, config, health y telemetry | Servicio | Operación autónoma |
| F9-06 | Routing | Migrar tráfico mediante gateway | Política | Cambio reversible |
| F9-07 | Strangler rollout | Duplicar lectura o migrar gradualmente según riesgo | Plan de rollout | Sin big bang |
| F9-08 | Data migration | Migrar y reconciliar datos | Herramienta | Conteos y hashes conciliados |
| F9-09 | Resiliencia | Probar latencia, timeout, circuit breaker y bulkhead | Tests | Fallo aislado |
| F9-10 | Operación | Crear SLO, alertas, runbooks y ownership | Paquete operativo | On-call preparado |
| F9-11 | Reversión | Probar retorno al módulo original | Runbook | Rollback dentro del tiempo objetivo |

#### Gate de salida

- [ ] Consumidores no fueron reescritos.
- [ ] El servicio tiene datos, despliegue y operación independientes.
- [ ] El fallo del servicio no derriba la plataforma.
- [ ] La migración y reversión fueron ensayadas.
- [ ] Se demostró beneficio frente al costo operativo adicional.

#### Hito

> BitCode puede extraer un módulo cuando existe una razón válida, manteniendo contratos y continuidad.

---

### Fase 10 — Certificación, adopción y liberación

#### Objetivo

Certificar que la plataforma satisface los objetivos de rendimiento, disponibilidad, seguridad, operación y experiencia de desarrollo.

#### Backlog

| ID | Trabajo | Actividades | Entregable | Criterio de aceptación |
|---|---|---|---|---|
| F10-01 | Release candidate | Congelar alcance y generar artefactos firmados | RC | Artefactos reproducibles |
| F10-02 | Performance certification | Ejecutar carga, estrés, spike y soak | Informe | Targets aprobados |
| F10-03 | Resilience certification | Ejecutar matriz de chaos tests | Informe | Comportamiento esperado |
| F10-04 | Security assessment | SAST, DAST, SCA, secrets y pen-test | Informe | Cero críticos abiertos |
| F10-05 | DR certification | Ejecutar restore, failover y failback | Evidencia | RPO/RTO logrados |
| F10-06 | Operational readiness | Validar dashboards, alertas, on-call y runbooks | Checklist ORR | Aprobación de Operaciones |
| F10-07 | Documentation review | Validar instalación, actualización y troubleshooting | Paquete documental | Prueba por usuario nuevo |
| F10-08 | Migration guide | Preparar adopción desde BitCode anterior | Guía | Aplicación piloto migrada |
| F10-09 | Training | Preparar talleres backend, frontend y operación | Material | Equipos habilitados |
| F10-10 | Go-live | Ejecutar rollout gradual y monitoreo | Release | SLO sostenido |
| F10-11 | Post-implementation review | Analizar métricas, incidentes y deuda | PIR | Acciones asignadas |

#### Gate de salida

- [ ] Targets técnicos satisfechos en entorno de referencia.
- [ ] Cero vulnerabilidades críticas abiertas.
- [ ] DR validado.
- [ ] Runbooks y alertas operativos.
- [ ] Aplicación piloto estable.
- [ ] Documentación aprobada.
- [ ] Responsables de soporte y mantenimiento asignados.

#### Hito

> BitCode Enterprise Platform está liberado como producto interno operable, medible y mantenible.

---

## 7. Workstreams transversales

### 7.1 Calidad y pruebas

Cada fase deberá conservar una pirámide de pruebas equilibrada:

| Nivel | Alcance |
|---|---|
| Unitarias | Dominio, policies, serializers, validators y primitives |
| Arquitectura | Límites de módulos, dependencias y convenciones |
| Integración | SQL Server, Valkey, Kafka, IdP y storage reales o equivalentes |
| Contrato | APIs y eventos forward/backward compatible |
| Componentes | Módulo con sus dependencias controladas |
| E2E | Flujos de negocio críticos |
| Rendimiento | Microbenchmark, carga, estrés, spike y soak |
| Resiliencia | Fallos de pods, workers, cache, broker, DB y región |
| Seguridad | SAST, DAST, SCA, secretos, autorización y pen-test |
| DR | Backup, restore, failover y failback |

### 7.2 Observabilidad

Cada nueva capacidad deberá aportar:

- Logs estructurados.
- Métricas técnicas y de negocio.
- Trazas distribuidas.
- Correlation ID.
- Dashboards.
- Alertas vinculadas a SLO.
- Runbook de diagnóstico.

No se aprobará una capacidad crítica que solo sea observable mediante revisión manual de logs.

### 7.3 DevSecOps y supply chain

El pipeline objetivo incluirá, en este orden lógico:

1. Restore.
2. Build.
3. Unit tests.
4. Architecture tests.
5. Integration tests.
6. Contract tests.
7. SAST.
8. SCA.
9. License scan.
10. Secret scan.
11. SBOM.
12. Container scan.
13. Performance smoke.
14. Firma de paquetes e imágenes.
15. Publicación.
16. Despliegue a DEV.
17. E2E.
18. Security tests.
19. Load tests.
20. Promoción controlada.

Cada release producirá:

- Artefacto.
- Firma.
- Checksum.
- SBOM.
- THIRD-PARTY-NOTICES.
- License report.
- Dependency graph.
- Vulnerability report.
- Changelog.
- Referencia exacta al código fuente.

### 7.4 Documentación

La IA actualizará como parte del mismo cambio:

- ADR cuando cambie una decisión.
- Referencia de API o evento.
- Guía de configuración.
- Guía de migración si hay impacto.
- Ejemplo ejecutable.
- Runbook si la capacidad requiere operación.
- Threat model si cambia una frontera de confianza.

---

## 8. Criterios técnicos globales

### 8.1 Rendimiento inicial

Estos valores son objetivos iniciales para un entorno de referencia. No constituyen garantías independientes del hardware; deberán congelarse después de la Fase 0.

| Métrica | Objetivo inicial |
|---|---:|
| Read API p95 | Hasta 100 ms |
| Read API p99 | Hasta 250 ms |
| Write API p95 | Hasta 200 ms |
| Write API p99 | Hasta 500 ms |
| Error rate de plataforma | Menor a 0,1 % |
| Cached read p95 | Hasta 10 ms |
| Gateway overhead p95 | Hasta 10 ms |
| Framework middleware overhead p95 | Hasta 5 ms |
| Tráfico API sostenido | Al menos 5.000 RPS por cluster de referencia |
| Escrituras transaccionales sostenidas | Al menos 1.000 TPS por cluster de referencia |
| CPU en estado estable | Menor a 70 % |
| Utilización del pool DB | Menor a 80 % |
| Eficiencia de escalamiento horizontal | Al menos 75 % |
| Lag normal de consumidores Kafka | Menor a 5 segundos |

### 8.2 Alta disponibilidad

| Métrica | Objetivo |
|---|---:|
| SLA de disponibilidad | 99,99 % |
| Recuperación tras falla de pod | Menor a 30 segundos |
| Impacto por falla de nodo | Sin downtime observable |
| Failover zonal | Hasta 60 segundos |
| RTO regional Gold | Hasta 5 minutos |
| RPO regional Gold | Hasta 60 segundos |
| Eventos Outbox perdidos | 0 |
| Pérdida tras publish confirmado | 0 |
| Eventos duplicados | Tolerados y deduplicados |
| Despliegues | Zero-downtime |

### 8.3 Seguridad

| Control | Objetivo |
|---|---|
| TLS externo | Obligatorio |
| OIDC/OAuth2 | Obligatorio |
| mTLS entre servicios extraídos | Obligatorio |
| Secretos en código o repositorio | 0 |
| CVE críticas abiertas | 0 |
| CVE altas sin excepción aprobada | 0 |
| SBOM por release | 100 % |
| Auditoría de operaciones críticas | 100 % |
| Validación de tokens | 100 % |
| Operaciones privilegiadas con RBAC/ABAC | 100 % |
| Rotación de claves | Automatizada |
| Pen-test antes de producción | Obligatorio |

### 8.4 Matriz de resiliencia

| Falla provocada | Resultado esperado |
|---|---|
| API pod eliminado | Servicio continuo |
| Worker eliminado | Mensaje reprocesado |
| Cache no disponible | Degradación controlada, no caída |
| Broker no disponible | Outbox acumula sin perder |
| Dependencia con timeout | Circuit breaker actúa |
| Dependencia lenta | Bulkhead protege recursos |
| Réplica SQL falla | Failover según perfil |
| Región no disponible | DR según perfil |
| Evento duplicado | Inbox descarta el duplicado |
| POST repetido | Idempotencia evita duplicado |
| Despliegue de nueva versión | Sin downtime |

---

## 9. Prioridades consolidadas

| Prioridad | Componentes |
|---|---|
| P0 | Gobierno, licencia, baseline, multi-tenancy, transacciones, OIDC/OAuth2, RBAC/ABAC, secretos, Outbox, Inbox, idempotencia, Kafka, resiliencia, health checks, auditoría, observabilidad, gateway, HA, DR, SBOM y seguridad del pipeline |
| P1 | Workflow, Rules, Documents, Feature Management, Notifications, Quartz HA, chaos testing, Angular platform, Integration Hub y experiencia de desarrollo |
| P2 | Reporting avanzado, dashboards, service mesh y extracción de microservicios |

### Elementos que no deben adelantarse

- No construir un service mesh durante la etapa de monolito modular.
- No crear un diseñador BPMN completo en la primera versión de Workflow.
- No introducir persistencia políglota sin un workload comprobado.
- No extraer microservicios por organización del código.
- No usar Elasticsearch, una base documental o una base de series temporales sin ADR y mediciones.
- No optimizar sin baseline, perfilado y comparación.

---

## 10. Estructura sugerida del repositorio

La estructura exacta deberá adaptarse al repositorio real durante Fase 0.

- src
  - Framework
    - BitCode.Shared.Kernel
    - BitCode.Application
    - BitCode.Persistence
    - BitCode.Security.Contracts
    - BitCode.Caching
    - BitCode.Resilience
    - BitCode.Observability
    - BitCode.Idempotency
    - BitCode.Messaging.Contracts
    - BitCode.Messaging.Kafka
    - BitCode.Outbox
    - BitCode.Inbox
  - Platform
    - BitCode.Platform.Identity
    - BitCode.Platform.Organization
    - BitCode.Platform.Configuration
    - BitCode.Platform.Audit
    - BitCode.Platform.Documents
    - BitCode.Platform.Workflow
    - BitCode.Platform.Notifications
    - BitCode.Platform.Integrations
    - BitCode.Platform.Reporting
  - Hosts
    - BitCode.Host
    - BitCode.Gateway
    - BitCode.Workers
  - Web
    - packages
    - applications
- tests
  - UnitTests
  - ArchitectureTests
  - IntegrationTests
  - ContractTests
  - EndToEndTests
  - PerformanceTests
  - ResilienceTests
  - SecurityTests
- deploy
  - containers
  - helm
  - terraform
  - environments
- docs
  - architecture
  - adr
  - api
  - events
  - guides
  - migration
  - runbooks
  - security
  - performance

---

## 11. Estrategia de migración desde BitCode actual

La evolución deberá ser incremental:

1. Congelar y medir el comportamiento actual.
2. Incorporar tests de caracterización.
3. Introducir nuevos contratos junto a los anteriores.
4. Marcar como obsoletos los contratos reemplazados.
5. Migrar una aplicación piloto.
6. Medir compatibilidad y rendimiento.
7. Publicar guía y herramientas de migración.
8. Mantener una ventana de coexistencia definida.
9. Retirar componentes anteriores únicamente en una versión mayor.

### Reglas de compatibilidad

- Un cambio breaking requiere ADR, versión mayor y guía de migración.
- Los eventos deberán admitir al menos una ventana de coexistencia acordada.
- Las migraciones de base de datos deberán seguir expand-and-contract cuando exista despliegue sin downtime.
- Un nuevo campo deberá ser opcional o tener default durante la transición.
- La eliminación física ocurrirá después de confirmar que no existen consumidores.

---

## 12. Riesgos principales

| Riesgo | Impacto | Mitigación |
|---|---|---|
| Convertir el framework en una plataforma monolítica rígida | Alto | Límites de módulos, contratos y arquitectura tests |
| Adoptar microservicios prematuramente | Alto | Gate de elegibilidad y ADR |
| Multi-tenancy con fuga de datos | Crítico | Default deny, filtros, pruebas negativas y pentest |
| Regresión por migración de runtime | Alto | Matriz de compatibilidad y piloto |
| Duplicación de efectos por mensajería | Crítico | Inbox, idempotencia y pruebas de reinicio |
| Pérdida de eventos | Crítico | Outbox transaccional y pruebas de falla |
| Exceso de retries | Alto | Retry budget, jitter, límites e idempotencia |
| Cascada por dependencia lenta | Alto | Timeout, circuit breaker y bulkhead |
| Auditoría manipulable | Crítico | Append-only, hash chain, firma y WORM |
| Objetivos de rendimiento irreales | Alto | Entorno de referencia y baseline |
| DR no ejecutable | Crítico | Drills periódicos y evidencia |
| Dependencias con licencia incompatible | Alto | Policy, scan y aprobación |
| Secretos en configuración | Crítico | Vault/KMS y secret scan |
| Divergencia backend/frontend | Medio | OpenAPI y generación automática |
| Complejidad operativa excesiva | Alto | Adopción gradual y no adelantar mesh/microservicios |

---

## 13. Decisiones que requieren aprobación humana

La IA deberá detenerse y solicitar decisión cuando se presente alguno de estos puntos:

- Elección final del IdP.
- Elección del proveedor de secretos/KMS.
- Cambio de licencia del producto.
- Breaking change de API pública.
- Eliminación o migración destructiva de datos.
- Cambio del modelo multi-tenant.
- Introducción de una nueva base de datos o broker.
- Aprobación de una excepción de seguridad o licencia.
- Definición contractual de SLA, RPO o RTO.
- Extracción de un microservicio.
- Habilitación de tráfico productivo.
- Failover o failback productivo.

---

## 14. Plantilla de instrucción para cada tarea de IA

Copiar y completar esta plantilla para ejecutar una tarea:

### Tarea

**ID:** [identificador]  
**Objetivo:** [resultado observable]  
**Alcance:** [componentes incluidos]  
**Fuera de alcance:** [exclusiones]  
**Dependencias:** [tareas o decisiones previas]  
**Criterios de aceptación:** [lista comprobable]

### Instrucciones de ejecución

1. Inspeccioná el repositorio antes de editar.
2. Identificá convenciones y pruebas existentes.
3. Presentá un plan breve de archivos y cambios.
4. Implementá el cambio mínimo completo.
5. No modifiqués componentes no relacionados.
6. Agregá pruebas unitarias y de integración aplicables.
7. Ejecutá build, tests, análisis y benchmark requeridos.
8. Actualizá documentación, ADR y ejemplos cuando corresponda.
9. Informá cualquier desviación o decisión pendiente.
10. No marques la tarea como completada si existe una validación fallida.

### Evidencia requerida

- Resumen de cambios.
- Lista de archivos modificados.
- Pruebas ejecutadas y resultados.
- Comparación de rendimiento, cuando corresponda.
- Revisión de seguridad.
- Revisión de compatibilidad.
- Riesgos residuales.
- Pendientes y siguiente tarea habilitada.

---

## 15. Plantilla de prompt para ejecutar una fase completa

### Rol

Actuá como arquitecto y desarrollador principal de BitCode. Tu responsabilidad es ejecutar únicamente la fase indicada, preservando las decisiones del Plan Maestro.

### Entrada

- Fase: [número y nombre].
- Repositorio: [ruta o URL].
- Rama de trabajo: [rama].
- Entorno objetivo: [detalle].
- Decisiones ya aprobadas: [lista].
- Restricciones: [lista].

### Procedimiento

1. Analizá el estado real y comparalo con el backlog de la fase.
2. Creá una matriz: implementado, parcial, ausente o bloqueado.
3. Ordená las tareas por dependencia y riesgo.
4. Ejecutá una tarea por vez.
5. Después de cada tarea, verificá su Definition of Done.
6. Conservá compatibilidad y cambios existentes no relacionados.
7. No avances a una tarea bloqueada.
8. No cierres la fase hasta ejecutar todas las pruebas del gate.
9. Generá un informe de cierre con evidencias.

### Salida esperada

- Matriz de cumplimiento.
- Código y configuración.
- Pruebas automatizadas.
- ADR y documentación.
- Resultados de seguridad y rendimiento.
- Riesgos y excepciones.
- Checklist del gate.
- Recomendación explícita: aprobar, aprobar con condiciones o rechazar el avance.

---

## 16. Control de avance

| Fase | Estado inicial | Gate | Responsable de aprobación |
|---:|---|---|---|
| 0 | No iniciada | Gobierno y baseline | Arquitectura y Tecnología |
| 1 | No iniciada | Core 2.0 | Arquitectura |
| 2 | No iniciada | Security 2.0 | Seguridad y Arquitectura |
| 3 | No iniciada | Event Platform | Arquitectura e Integraciones |
| 4 | No iniciada | HA Runtime | Infraestructura y Operaciones |
| 5 | No iniciada | DR y multi-región | Continuidad, Infraestructura y Negocio |
| 6 | No iniciada | Enterprise Platform | Producto y Arquitectura |
| 7 | No iniciada | Angular Platform | UX, Frontend y Arquitectura |
| 8 | No iniciada | Productización | Engineering Enablement |
| 9 | No iniciada | Extracción piloto | Comité de Arquitectura |
| 10 | No iniciada | Liberación | Tecnología, Seguridad y Negocio |

Los estados permitidos son:

- No iniciada.
- En análisis.
- En desarrollo.
- En validación.
- Bloqueada.
- Aprobada con condiciones.
- Completada.

---

## 17. Secuencia recomendada de inicio

Las primeras tareas que la IA deberá ejecutar son:

1. F0-01 — Inventario técnico.
2. F0-10 — Línea base funcional y de rendimiento.
3. F0-03 y F0-04 — Principios y ADR iniciales.
4. F0-07 — Threat model.
5. F0-05 y F0-06 — Versionado, licencias y dependencias.
6. F1-11 — Benchmark del multi-tenancy actual.
7. F1-06 y F1-07 — Rediseño transaccional.
8. F1-12 a F1-16 — Multi-tenancy y aislamiento.
9. F1-22 a F1-26 — Confiabilidad y resiliencia.
10. F2-01 en adelante — Security 2.0.

No se recomienda comenzar por Workflow, Reporting, dashboards o microservicios. Esas capacidades dependen de que el núcleo, la seguridad, la mensajería, la observabilidad y la operación sean confiables.

---

## 18. Resultado final esperado

Al completar el plan, BitCode deberá ofrecer:

- Un framework core estable y versionado.
- Una plataforma empresarial modular.
- Seguridad Zero Trust.
- Autorización RBAC y ABAC.
- Auditoría inmutable.
- Persistencia multi-tenant segura y performante.
- Idempotencia, Outbox e Inbox.
- Integración orientada a eventos.
- Resiliencia y tolerancia a fallos.
- Runtime Kubernetes con alta disponibilidad.
- Recuperación ante desastre demostrada.
- Paquetes Angular empresariales.
- Plantillas y golden paths.
- Supply chain segura y auditable.
- Capacidad real, pero no obligación, de extraer microservicios.

La secuencia arquitectónica oficial será:

> Core 2.0 → Performance y multi-tenancy → Consistencia e idempotencia → Zero Trust → Eventos y resiliencia → Alta disponibilidad → DR y multi-región → Módulos empresariales → Plataforma Angular → Productización → Extracción selectiva → Certificación.

---

### Aprobaciones

| Rol | Nombre | Decisión | Fecha |
|---|---|---|---|
| Sponsor tecnológico |  |  |  |
| Arquitectura de software |  |  |  |
| Seguridad de la información |  |  |  |
| Infraestructura y operaciones |  |  |  |
| Responsable de desarrollo |  |  |  |
| Producto o negocio |  |  |  |
