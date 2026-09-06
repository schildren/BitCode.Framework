# Guía — Proveedor de secretos: `ISecretProvider` (F2-12)

**Tarea:** F2-12 (Fase 2, Épica F2-C — Secretos y criptografía) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Criterio de aceptación:** "Cero secretos en repositorio".
**ADR:** [`docs/adr/0014-secretos-proveedor-vault-propuesto.md`](adr/0014-secretos-proveedor-vault-propuesto.md) — la
abstracción está `Accepted`; HashiCorp Vault como proveedor concreto de nivel empresarial queda `Proposed`,
pendiente de aprobación humana explícita (Plan Maestro sección 13) antes de habilitarse en un entorno
productivo real.

## Qué resuelve esta tarea

Un proyecto consumidor necesita cadenas de conexión, `client_secret` de un IdP externo (ver el addendum
F2-06 del ADR 0004), o credenciales de un servicio de terceros — ninguno de esos valores puede vivir en
`appsettings.json` versionado en el repositorio. F2-12 agrega `ISecretProvider`
(`Shared.Infrastructure.Security.Secrets`), una abstracción intercambiable por configuración (mismo
principio que el adapter OIDC/OAuth2, F2-01/ADR 0004): el código de aplicación resuelve un secreto por
clave lógica sin saber si detrás hay un Vault real o la configuración local de un desarrollador.

```csharp
public interface ISecretProvider
{
    Task<Result<string>> GetSecretAsync(string key, CancellationToken cancellationToken = default);
}
```

Un secreto inexistente (`Secrets.NotFound`) o un fallo de comunicación con el proveedor
(`Secrets.ProviderUnavailable`/`Secrets.AccessDenied`) son `Result.Failure`, nunca una excepción sin
traducir — mismo criterio que el resto de los contratos de infraestructura del framework
(`IServiceTokenProvider`, F2-04).

## Registro: `AddSharedSecretProvider`

```csharp
services.AddSharedSecretProvider(configuration);
```

Lee `"Secrets:Provider"` (`SecretProviderKind.Configuration` por default si no se configura) y registra
la implementación correspondiente — el código de negocio inyecta `ISecretProvider`, nunca
`ConfigurationSecretProvider`/`VaultSecretProvider` directamente.

## `ConfigurationSecretProvider` — desarrollo/local (default)

Resuelve cada secreto desde `IConfiguration` bajo `"Secrets:Values"` (configurable con
`ConfigurationSecretProviderOptions.ValuesSectionPath`). En una máquina de desarrollo, esa sección se
puebla exclusivamente vía:

```
dotnet user-secrets set Secrets:Values:Oidc__ServiceIdentity__ClientSecret "valor-real"
```

o la variable de entorno equivalente (`Secrets__Values__Oidc__ServiceIdentity__ClientSecret`) — **nunca**
como literal en `appsettings.json`. No sustituye a un proveedor de nivel empresarial en producción: existe
para no bloquear el trabajo local ni CI sin un Vault disponible, mismo rol que `NullTenantProvider` (F1-12)
o el JWT propio (`Shared.Infrastructure.Security.Jwt`) cumplen frente a su contraparte más completa.

## `VaultSecretProvider` — HashiCorp Vault (KV v2, `Proposed`)

```json
{
  "Secrets": {
    "Provider": "Vault",
    "Vault": {
      "Address": "https://vault.miempresa.com:8200",
      "Token": "",
      "MountPath": "secret",
      "PathPrefix": "bitcode",
      "ValueFieldName": "value"
    }
  }
}
```

`Secrets:Vault:Token` es, en sí mismo, un secreto — se resuelve desde una variable de entorno o el
mecanismo de arranque del proceso, nunca desde `appsettings.json` (el "secreto para llegar al secreto" no
puede vivir en el repositorio tampoco). `AddSharedSecretProvider` falla explícitamente
(`InvalidOperationException`) si falta `Address`/`Token`, o si `Address` no es HTTPS y
`AllowInsecureHttp` no se habilita explícitamente (Zero Trust, Plan Maestro sección 1;
`AllowInsecureHttp` solo tiene sentido contra un Vault de desarrollo local o Testcontainers, nunca en
producción).

`VaultSecretProvider` lee la API HTTP estándar de Vault (`GET /v1/{MountPath}/data/{PathPrefix}/{key}`,
header `X-Vault-Token`) sin un SDK cliente de terceros — mismo estilo minimalista que
`ServiceTokenProvider` (F2-04). Se registra como cliente HTTP tipado vía `AddResilientHttpClient` (F1-26):
timeout/retry/circuit breaker de la pipeline estándar del framework, no un mecanismo propio.

Un secreto simple se guarda en Vault como `{"value": "..."}` (campo configurable con `ValueFieldName`
si un consumidor necesita leer un campo distinto de un secreto con múltiples campos).

### Queda fuera de alcance de F2-12

- **Autenticación AppRole/Kubernetes auth** contra Vault — solo el método "token" está implementado.
- **Secretos dinámicos de Vault** (credenciales de base de datos de corta vida, renovación/revocación de
  leases) — `ISecretProvider.GetSecretAsync` asume un secreto estático versionado (KV v2), no un lease.
- **Adopción productiva real de Vault** — requiere la aprobación humana de la sección 13 sobre el ADR
  0014 antes de aprovisionar un Vault operativo (política de auth, sellado, HA).
- **F2-13 (cifrado, gestión de claves)** y **F2-14 (mTLS contracts)** — tareas separadas de la misma
  Épica F2-C.

## Pruebas

- `tests/Shared.Infrastructure.Security.Tests/Secrets/ConfigurationSecretProviderTests.cs` — proveedor de
  desarrollo/local, sin infraestructura externa.
- `tests/Shared.Infrastructure.Security.Tests/Secrets/VaultSecretProviderTests.cs` — mapeo de respuestas
  HTTP (200/404/401/403/500, campo faltante) a `Result<string>`, contra un `HttpMessageHandler` falso.
- `tests/Shared.Infrastructure.Security.Tests/Secrets/SecretProviderServiceCollectionExtensionsTests.cs` —
  contrato de registro/intercambiabilidad por configuración y validación de configuración obligatoria.
- `tests/Shared.Infrastructure.Security.Tests/Integration/VaultSecretProviderIntegrationTests.cs` — contra
  un HashiCorp Vault real (`VaultContainerFixture`, Testcontainers, modo dev): escritura vía la API KV v2,
  lectura vía `VaultSecretProvider`, token inválido, clave inexistente.

## Referencias

- [`docs/convenciones.md`](convenciones.md) — cuándo usar cada proveedor.
- [`docs/adr/0014-secretos-proveedor-vault-propuesto.md`](adr/0014-secretos-proveedor-vault-propuesto.md) — decisión y estado `Proposed`.
- [`docs/adr/0004-identidad-idp-oidc-oauth2.md`](adr/0004-identidad-idp-oidc-oauth2.md) — mismo patrón de abstracción intercambiable por configuración.
