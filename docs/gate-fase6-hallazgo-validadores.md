# Hallazgo crítico transversal: los validadores FluentValidation de Fase 6 nunca se ejecutaban

**Fecha del hallazgo:** 2026-09-09, durante la implementación de Fase 6, módulo 9 (Integration Hub).
**Severidad:** Crítica — afecta a los 8 módulos de Fase 6 completados hasta ese momento (Identity
Administration, Organization, Catalogs and Parameters, Feature Management, Documents, Workflow, Task
Inbox, Notifications), no solo a Integration Hub.
**Estado:** Corregido en el mismo corte, verificado contra la suite completa de tests de los 9 módulos +
`Shared.Application.Tests` + `Sample.Api.Tests` (Fase 1-3). Ningún test existente dependía del
comportamiento roto.

## Qué pasaba

`Shared.Application.ApplicationServiceCollectionExtensions.AddSharedApplication` registraba los
validadores de FluentValidation con:

```csharp
services.AddValidatorsFromAssemblies(assemblies);
```

El parámetro `includeInternalTypes` de `AddValidatorsFromAssemblies` (FluentValidation 11.10.0) tiene
**`false` como valor por defecto**. Todos los validadores de este framework se declaran
`internal sealed class XxxCommandValidator : AbstractValidator<XxxCommand>` — es la convención
deliberada del proyecto (`docs/convenciones.md`: solo `DbContext`, extensiones DI, clases de permisos y
entidades/eventos públicos son `public`; comandos/queries/validators/handlers son `internal sealed`).

Consecuencia: **ningún validador `internal` de ningún módulo se registraba en el contenedor de DI**.
`ValidationBehavior<TRequest,TResponse>` (`Shared.Application/Behaviors/ValidationBehavior.cs`) resuelve
`IEnumerable<IValidator<TRequest>>` por constructor — con la lista vacía, la primera línea del método
(`if (!validators.Any()) return await next();`) saltaba la validación completa, en silencio, para
absolutamente todo comando de todo módulo desde que el pipeline de MediatR existe (Fase 1). Ninguna
excepción, ningún log de advertencia: el comando simplemente se ejecutaba con datos que su propio
`AbstractValidator` debía haber rechazado.

## Cómo se descubrió

Un test de integración nuevo de Integration Hub (módulo 9)
(`CrearConector_ConApiKeySinSecretKey_Retorna400`) esperaba `400 Bad Request` al crear un conector con
`TipoAutenticacion = ApiKey` y `SecretKey = null` — una regla explícita de
`CrearConectorCommandValidator`. La API devolvía `201 Created`. Se aisló el problema instrumentando
temporalmente el predicado `.When(...)` de la regla con un `Console.WriteLine`: el mensaje de debug
**nunca se imprimía**, confirmando que el validador nunca se instanciaba ni se invocaba — no un bug de
lógica de la regla en sí, sino de registro en el contenedor de DI.

## Por qué ningún módulo anterior lo detectó

De los 8 módulos ya completados de Fase 6, solo dos tests de integración en toda la plataforma
verificaban un `400 Bad Request` en algún endpoint (`Sample.Organization.Api.Tests`,
`Sample.Api.Tests`) — y en ambos casos el `400` provenía de una regla de negocio devuelta directamente
por el HANDLER vía `Result.Failure(Error.Validation(...))`, no de un `AbstractValidator` de
FluentValidation. Ninguna suite de los 8 módulos anteriores tenía un test que ejercitara específicamente
una regla declarada en un `AbstractValidator` (`NotEmpty`, `MaximumLength`, `Must`, etc.) esperando el
rechazo — todas las reglas de validación de esos módulos existían en el código pero jamás se habían
probado de punta a punta contra la API real. El "camino feliz" de cada módulo nunca se vio afectado
porque los datos de esos tests ya eran válidos por construcción.

## Corrección aplicada

```csharp
services.AddValidatorsFromAssemblies(assemblies, includeInternalTypes: true);
```

Un único cambio de una línea en `src/Shared.Application/ApplicationServiceCollectionExtensions.cs`,
fuera del alcance de cualquier módulo de Fase 6 individual — es infraestructura compartida usada por
todos.

## Verificación exhaustiva tras el fix

Se re-ejecutó la suite completa de tests de integración de los 9 módulos de Fase 6 (contra SQL Server
real, Testcontainers) más `Shared.Application.Tests` y `Sample.Api.Tests` (Fase 1-3, que también usa
`AddSharedApplication`), para confirmar que activar los validadores reales no rompía ningún "camino
feliz" existente que hubiera estado dependiendo (sin saberlo) de que la validación estuviera
deshabilitada:

| Suite | Resultado tras el fix |
|---|---|
| `Sample.IdentityAdmin.Api.Tests` | 8/8 |
| `Sample.Organization.Api.Tests` | 8/8 |
| `Sample.Catalogs.Api.Tests` | 9/9 |
| `Sample.FeatureManagement.Api.Tests` | 10/10 |
| `Sample.Documents.Api.Tests` | 10/10 |
| `Sample.Workflow.Api.Tests` | 7/7 |
| `Sample.TaskInbox.Api.Tests` | 11/11 |
| `Sample.Notifications.Api.Tests` | 9/9 |
| `Sample.Api.Tests` (Fase 1-3) | 20/20 |
| `Shared.Application.Tests` | 56/56 |

Ningún test regresionó. Esto confirma que ningún test anterior enviaba datos que ahora empiezan a ser
rechazados — el fix es puramente aditivo en términos de comportamiento observable por los tests
existentes, y corrige un hueco de seguridad/integridad de datos real y silencioso en los 8 módulos
anteriores (cualquier `RuleFor` de esos módulos que nunca se ejerció con datos inválidos en un test
específico también estaba, hasta este fix, completamente inactiva en producción).

## Impacto retroactivo sobre los módulos 1-8

Este hallazgo no requiere reabrir ni re-auditar el código de los módulos 1-8 — sus validadores están
correctamente escritos (las reglas en sí son válidas), el defecto vivía enteramente en el registro de DI
compartido. Con este fix, todas las reglas de `AbstractValidator` ya declaradas en Identity
Administration, Organization, Catalogs, Feature Management, Documents, Workflow, Task Inbox y
Notifications empiezan a aplicarse genuinamente por primera vez desde que se implementaron. No se
identificó ninguna regla mal escrita que ahora rechace datos que debería aceptar (la suite completa lo
confirma), pero se recomienda que cualquier trabajo futuro sobre esos módulos tenga en cuenta que, antes
de este commit, sus validadores nunca habían sido ejercitados end-to-end contra la API real.

## Referencias

- `src/Shared.Application/ApplicationServiceCollectionExtensions.cs` — la corrección.
- `src/Shared.Application/Behaviors/ValidationBehavior.cs` — el pipeline behavior afectado.
- `docs/convenciones.md` — convención de accesibilidad `internal sealed` para comandos/queries/validators.
- `docs/guia-integration-hub.md` — módulo durante cuya implementación se descubrió el hallazgo.
