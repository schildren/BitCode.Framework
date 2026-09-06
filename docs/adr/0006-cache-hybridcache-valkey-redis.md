# 0006. Cache: HybridCache con proveedor L2 distribuido intercambiable (Valkey/Redis)

**Estado:** Accepted
**Fecha:** 2026-09-05
**Responsable:** Pendiente de asignación

## Contexto

El Plan Maestro (sección 2) fija "cache distribuido: abstracción neutral; Valkey como proveedor inicial". El repositorio ya implementa una capa de cache (`Shared.Infrastructure.Caching`) usando `Microsoft.Extensions.Caching.Hybrid` (HybridCache, L1 en memoria + L2 distribuido) con `Microsoft.Extensions.Caching.StackExchangeRedis` como proveedor L2 (`docs/inventario-tecnico.md`, sección 1.1 y 3), y las pruebas de integración usan `RedisContainerFixture` de `Shared.Testing` (`Shared.Infrastructure.Caching.Tests/Integration/HybridCacheRedisIntegrationTests.cs`, según inventario). No es una decisión nueva: la abstracción HybridCache ya existe y ya se prueba contra Redis real vía Testcontainers.

## Decisión

Se ratifica HybridCache como la abstracción de cache del framework (L1 en memoria + L2 distribuido), manteniendo el proveedor L2 intercambiable por configuración, consistente con la decisión rectora del Plan Maestro (Valkey como proveedor inicial recomendado, compatible con el protocolo Redis ya usado en pruebas e implementación actuales). El cambio concreto de proveedor L2 (de Redis/StackExchangeRedis a Valkey en el entorno de referencia y producción) es una configuración de infraestructura, no un cambio de contrato de código, dado que ambos son compatibles con el protocolo Redis y con `Microsoft.Extensions.Caching.StackExchangeRedis`.

**Regla dura preservada (sección 3.2 del Plan Maestro, ya reflejada en `docs/convenciones.md`):** cache nunca es fuente de verdad para saldos, ledger, auditoría o transacciones. La escritura a L2 es asíncrona; no debe asumirse consistencia inmediata entre instancias.

## Alternativas consideradas

- **Cache únicamente en memoria (L1, sin L2 distribuido):** descartado para despliegues multi-instancia/Kubernetes (Fase 4), ya que no permite invalidación coherente entre réplicas.
- **Acoplar el código directamente a la API de StackExchange.Redis sin la abstracción HybridCache:** descartado; rompería la intercambiabilidad de proveedor exigida por la decisión rectora del Plan Maestro y ya evitada por el diseño actual.

## Consecuencias

- Ratifica el estado actual del código; no requiere migración inmediata.
- La migración operativa de Redis a Valkey en el entorno de referencia/producción (Fase 0-4) debe validarse con las mismas pruebas de integración ya existentes (`RedisContainerFixture`), sin cambios de contrato en `Shared.Infrastructure.Caching`.
- Compatible con el principio de observabilidad (`docs/architecture-principles.md`, sección 4): el cache complementa, no reemplaza, la fuente de verdad transaccional en SQL Server (ADR 0002).

## Riesgos y mitigación

- **Riesgo:** uso incorrecto de cache como fuente de verdad en un handler nuevo, violando la regla dura de la sección 3.2 del Plan Maestro. Mitigación: revisión de código y, cuando exista, architecture test que detecte lecturas de cache en rutas de saldo/ledger/auditoría.
- **Riesgo (cerrado por F1-16):** contaminación de cache entre tenants. `HybridCache` no tiene ningún concepto de tenant propio: si dos tenants piden el mismo recurso lógico con la misma clave literal (por ejemplo, `"producto:slug-x"`), comparten la misma entrada cacheada — es el comportamiento esperado de cualquier cache de clave/valor sin tenant en la clave, no un bug de HybridCache, pero es una fuga de información real si un handler cachea datos sensibles a tenant sin componer el `TenantId` en la clave (`docs/threat-model.md`, hallazgo I5). F1-16 agrega `ITenantAwareCache`/`TenantAwareCache` (`Shared.Infrastructure.Caching`, registrado por `AddSharedCaching` vía `TryAddScoped`), que compone `"tenant:{TenantId}:"` en la clave usando `ITenantContext` (F1-15) y falla cerrado (`TenantResolutionException`) si el proyecto opera en modo multi-tenant sin un `TenantId` resuelto para el scope actual — mismo principio de "fallar de forma segura" que `HttpContextTenantProvider` (F1-12). Probado contra dos tenants compartiendo la misma instancia de `HybridCache` (`TenantAwareCacheTests`, Shared.Infrastructure.Caching.Tests). `HybridCache` directamente sigue siendo válido para datos que **no** son sensibles a tenant (metadata/configuración global compartida entre todos los tenants de un despliegue) — en ese caso, componer el `TenantId` sería incorrecto.
- Vinculado al registro de riesgos de F0-12 (pendiente de creación).
