# 0014. Secretos: abstracción intercambiable (`ISecretProvider`) y HashiCorp Vault propuesto como proveedor

**Estado:** Proposed
**Fecha:** 2026-09-06
**Responsable:** Pendiente de aprobación humana explícita (Plan Maestro sección 13)

## Contexto

El Plan Maestro (Fase 2, Épica F2-C — Secretos y criptografía, F2-12: "Secret provider — Integrar
Vault, Key Vault o KMS aprobado") pide una abstracción de proveedor de secretos más al menos un
proveedor concreto, con "Cero secretos en repositorio" como criterio de aceptación. El estado del
repositorio al iniciar esta tarea ya cumple ese criterio de forma estructural: `samples/Sample.Api/appsettings.json`
no trae ninguna cadena de conexión ni credencial (`"ConnectionStrings:Default": ""`), y los únicos
"secretos" que aparecen en el código (`KeycloakContainerFixture.ClientSecret`, contraseñas de
`SqlServerContainerFixture`/`RedisContainerFixture`, el root token de `VaultContainerFixture` agregado
por esta misma tarea) son literales de un contenedor Testcontainers efímero, descartado al finalizar
cada corrida de pruebas — no credenciales de un sistema real. F2-12 es, por lo tanto, trabajo
prospectivo: construir el mecanismo que un proyecto consumidor real necesitará para externalizar sus
propios secretos (cadenas de conexión, `client_secret` de un IdP externo — ver el addendum F2-06 del
ADR 0004, "política operativa de rotación de `client_secret`... mientras no exista un proveedor de
secretos automatizado"—, credenciales de servicios de terceros), no remediar una fuga ya existente.

La Plan Maestro sección 13 lista explícitamente "elección de proveedor de secretos/KMS" como una
decisión que requiere aprobación humana explícita antes de construirse — el mismo tratamiento que ya
recibió la elección de Kafka como broker de mensajería (ADR 0005, todavía `Proposed`) y que recibió
Keycloak como IdP (ADR 0004, `Accepted` porque esa aprobación específica ya se obtuvo explícitamente
antes de iniciar la Épica F2-A). Al iniciar F2-12 no existe ninguna aprobación humana registrada sobre
qué proveedor concreto de secretos/KMS adoptar.

## Decisión

Se separan dos decisiones de distinto nivel, siguiendo el mismo patrón que ADR 0004 (adapter
intercambiable) y ADR 0005 (broker concreto pendiente de aprobación):

1. **La abstracción (`ISecretProvider`, intercambiable por configuración) queda `Accepted` e
   implementada.** `Shared.Infrastructure.Security.Secrets.ISecretProvider` (`GetSecretAsync(key) ->
   Result<string>`) es el único contrato que debe consumir código de aplicación/infraestructura para
   resolver un secreto — nunca un tipo concreto. `SecretProviderServiceCollectionExtensions.AddSharedSecretProvider(configuration)`
   selecciona la implementación exclusivamente a partir de `"Secrets:Provider"`, sin que el código de
   negocio cambie entre proveedores.
2. **HashiCorp Vault (`VaultSecretProvider`, motor KV v2 vía su API HTTP estándar) queda `Proposed`,
   no `Accepted`, como proveedor concreto de nivel empresarial** — implementado y verificado (unitario
   y contra un Vault real vía Testcontainers, `VaultContainerFixture`), disponible para que un
   proyecto lo habilite explícitamente por configuración, pero su adopción productiva real (aprovisionar
   un Vault operativo, definir su política de auth/sellado/HA) requiere la aprobación humana de la
   sección 13 antes de habilitarse contra un entorno productivo real — mismo tratamiento que ADR 0005
   da a Kafka.
3. **`ConfigurationSecretProvider` (default cuando no se configura `"Secrets:Provider"`) queda
   `Accepted` como proveedor de desarrollo/local**, sin necesidad de aprobación adicional: resuelve
   secretos desde `IConfiguration` bajo la sección `"Secrets:Values"`, poblada en la práctica vía
   variables de entorno o `dotnet user-secrets` — nunca desde `appsettings.json` versionado. Mismo rol
   que `NullTenantProvider` (F1-12) o el JWT propio (`Shared.Infrastructure.Security.Jwt`) cumplen
   frente a su contraparte de nivel empresarial: no bloquea el trabajo local ni CI sin infraestructura
   externa disponible, y no implica ninguna decisión de proveedor productivo.

Motivo de proponer Vault (no elegirlo formalmente) como candidato: es open source, self-hosteable
(consistente con Keycloak — ADR 0004 — y con la filosofía general del repositorio de no atar el
entorno de referencia a un tenant cloud de pago), se integra vía una API HTTP estándar sin SDK
propietario (mismo estilo que `ServiceTokenProvider`/`OidcAuthorizationCodeExchanger`, F2-02/F2-04), y
tiene una imagen oficial de contenedor apta para Testcontainers/CI (`hashicorp/vault`, modo dev) —
mismo criterio que ya justificó Keycloak sobre Entra ID para el entorno de referencia.

## Alternativas consideradas

- **Azure Key Vault:** no evaluado en profundidad porque el repositorio no fija Azure como plataforma
  cloud objetivo en ningún ADR previo (a diferencia de Keycloak/Kafka, autohosteables); introducirlo
  ataría el entorno de referencia/CI a un tenant Azure real, mismo motivo que descartó Entra ID como
  IdP de referencia en ADR 0004.
- **AWS Secrets Manager/KMS:** mismo motivo que Azure Key Vault — ningún ADR previo fija AWS como
  plataforma objetivo.
- **Variables de entorno/user-secrets como único mecanismo (sin Vault):** cubre el desarrollo local
  (`ConfigurationSecretProvider`, adoptado sin reservas) pero no un entorno productivo real con
  rotación, auditoría de acceso a secretos y control de acceso granular por secreto — insuficiente
  como único mecanismo para el criterio de aceptación de F2-12 a nivel empresarial.

## Consecuencias

- **F2-12 implementado:** `src/Shared.Infrastructure.Security/Secrets/` (`ISecretProvider`,
  `SecretProviderOptions`/`SecretProviderKind`, `ConfigurationSecretProvider`/`ConfigurationSecretProviderOptions`,
  `VaultSecretProvider`/`VaultSecretProviderOptions`, `SecretProviderServiceCollectionExtensions.AddSharedSecretProvider`).
  Ver `docs/guia-secret-provider.md` para el detalle de diseño y `docs/convenciones.md` para cuándo
  usar cada proveedor.
- Habilitar `VaultSecretProvider` contra un Vault productivo real (no Testcontainers/dev) requiere
  primero la aprobación humana de la sección 13 sobre esta misma ADR — análogo al addendum de riesgo
  de ADR 0005 para Kafka. Mientras esa aprobación no exista, un proyecto real que necesite un
  proveedor de secretos de nivel empresarial hoy usa `VaultSecretProvider` bajo su propio riesgo y
  responsabilidad (el código ya es correcto y probado), o queda en `ConfigurationSecretProvider`
  (variables de entorno/user-secrets) hasta que la decisión de proveedor se apruebe formalmente.
- Autenticación de `VaultSecretProvider` contra Vault: solo el método "token" (`VaultSecretProviderOptions.Token`)
  está implementado. AppRole, Kubernetes auth y la renovación/revocación de leases (Vault dinámico:
  credenciales de base de datos de corta vida, etc.) quedan fuera de alcance de F2-12 — ver
  `docs/guia-secret-provider.md`, "queda fuera de alcance". F2-13 (cifrado, gestión de claves) y F2-14
  (mTLS contracts) tampoco quedan resueltos por esta tarea.
- El "secreto para llegar al secreto" (`Secrets:Vault:Token`) sigue el mismo principio que todo lo
  demás: se resuelve por configuración externa (variable de entorno o mecanismo de arranque del
  proceso), nunca como literal en `appsettings.json` — `AddSharedSecretProvider` falla explícitamente
  (`InvalidOperationException`) si falta, en vez de arrancar con un proveedor mal configurado en
  silencio.

## Riesgos y mitigación

- **Riesgo:** que un proyecto consumidor habilite `VaultSecretProvider` contra un Vault productivo real
  sin que exista todavía la aprobación humana de la sección 13 sobre el proveedor de secretos/KMS.
  Mitigación: este ADR documenta explícitamente el estado `Proposed`; la aprobación productiva se
  registra actualizando este mismo documento a `Accepted`, mismo mecanismo que ADR 0004 usó para
  Keycloak.
- **Riesgo:** confundir `ConfigurationSecretProvider` (deliberadamente simple, pensado para desarrollo/
  CI) con un mecanismo apto para producción. Mitigación: la documentación de la clase y de
  `docs/guia-secret-provider.md` lo dejan explícito, mismo patrón que `NullTenantProvider`/JWT propio.
- Vinculado al registro de riesgos de F0-12 y a la sección 13 del Plan Maestro.
