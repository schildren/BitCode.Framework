# Política criptográfica (F2-13)

**Tarea:** F2-13 (Fase 2, Épica F2-C — Secretos y criptografía) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Criterio de aceptación:** "Rotación y recuperación probadas".
**Depende de:** F2-12 (`ISecretProvider`, `docs/guia-secret-provider.md`) — el cifrado a nivel de
aplicación reutiliza el proveedor de secretos ya existente para resolver material de clave; no reimplementa
gestión de secretos.

## Qué resuelve esta tarea

F2-13 pide "definir qué datos requieren cifrado y su gestión de claves" con una "política criptográfica"
como entregable. Este documento cubre esa política (qué se cifra, con qué algoritmo, quién gestiona las
claves) y `Shared.Infrastructure.Security.Encryption` (`IEncryptionProvider`) es su implementación de
referencia para el cifrado de aplicación (campos sensibles en reposo) — cifrado en tránsito (TLS/HTTPS) ya
es un requisito transversal de Zero Trust (Plan Maestro sección 1) y no depende de esta tarea; mTLS
servicio-a-servicio es F2-14, tarea separada de esta misma épica.

## 1. Qué datos requieren cifrado

| Categoría | Ejemplos | Mecanismo | Tarea |
|---|---|---|---|
| Secretos de infraestructura (cadenas de conexión, `client_secret` de un IdP, credenciales de terceros) | `Secrets:Vault:Token`, `client_secret` OIDC | `ISecretProvider` (nunca en `appsettings.json`) | F2-12 |
| Datos personales (PII) de negocio en reposo | DNI/CUIT, domicilio, teléfono, datos de contacto de un cliente | `IEncryptionProvider` sobre la columna concreta (a nivel de consumidor: value converter de EF Core sobre el campo, no la fila completa) | F2-13 (este documento) |
| Datos financieros sensibles en reposo | número de cuenta/CBU/IBAN, número de tarjeta si el dominio los persiste (no recomendado sin cumplir PCI-DSS: preferir tokenización de un PSP externo) | `IEncryptionProvider`, o directamente no persistir el dato crudo (tokenización) | F2-13 (este documento) |
| Estado de correlación de un flujo OIDC (PKCE verifier, `state`, `nonce`) | cookie de correlación del Authorization Code Flow | `IDataProtectionProvider`/`IDataProtector` nativo de ASP.NET Core (ya en uso, `OidcAuthorizationCodeStateProtector`, F2-04) | F2-04/F2-06 |
| Datos en tránsito (toda comunicación HTTP, interna o externa) | cualquier llamada API, browser↔BFF, servicio↔IdP | TLS/HTTPS obligatorio (Zero Trust) | transversal, todas las fases de F2 |
| Comunicación servicio-a-servicio con identidad mutua | llamadas entre microservicios extraídos | mTLS | F2-14 (fuera de alcance de F2-13) |
| Auditoría (integridad, no confidencialidad) | registro de auditoría inmutable | firma/hash encadenado, no cifrado simétrico | F2-D (Auditoría inmutable), tarea separada |

**Hallazgo de esta tarea:** al día de F2-13, `BitCode.Framework` (el framework en sí, `src/`) no define
ninguna entidad de dominio con campos de PII/datos financieros — esas entidades son responsabilidad de
cada proyecto consumidor (el framework es infraestructura reutilizable, no una aplicación de dominio
concreta). No hay, por lo tanto, ninguna columna existente en este repositorio que hoy debería estar
cifrada y no lo esté. Lo que esta tarea entrega es la **abstracción y la política** que un proyecto
consumidor real debe aplicar sobre sus propias entidades (ver la sección "Cómo aplicarlo", más abajo) —
mismo patrón que F2-12 entregó `ISecretProvider` sin que el framework tuviera hoy un secreto real que
migrar.

## 2. Algoritmos aprobados

- **Cifrado simétrico de campo (dato en reposo):** AES-256 en modo **GCM** (cifrado autenticado — detecta
  alteración del texto cifrado, no solo confidencialidad). Prohibido ECB. Prohibido CBC/CTR sin HMAC
  independiente (cifrado no autenticado). Longitud de clave: 256 bits (32 bytes) exactos —
  `AesGcmEncryptionProvider` rechaza cualquier material de clave de otra longitud
  (`Encryption.InvalidKeyMaterial`).
- **Nonce/IV:** 96 bits (12 bytes), generado con `RandomNumberGenerator` (CSPRNG) en cada operación de
  cifrado — nunca reutilizado con la misma clave (requisito de seguridad de AES-GCM).
- **Firma de tokens JWT empresariales (OIDC):** asimétrica (RS256/ES256 vía JWKS del IdP,
  `AddSharedOidcAuthentication`, F2-01/ADR 0004) — ya resuelto fuera de esta tarea, listado acá solo para
  completitud de la política. El JWT propio (`AddSharedSecurity`, "camino simple" sin IdP externo) sigue
  usando una clave simétrica (`SymmetricSecurityKey`) porque es explícitamente el camino de desarrollo/
  proyectos sin OIDC, no el mecanismo empresarial.
- **Protección de estado de correlación (cookies, verificadores PKCE):** `IDataProtectionProvider` nativo
  de ASP.NET Core — no se reemplaza por `IEncryptionProvider`: ya resuelve rotación de clave y expiración
  con su propio key ring, y es el mismo mecanismo que usa el middleware OpenIdConnect internamente.
- **Hashing de contraseñas:** delegado a ASP.NET Core Identity (`PasswordHasher<TUser>`, PBKDF2) — no
  forma parte de esta tarea, no se toca.

## 3. Gestión de claves

### 3.1 Dónde vive el material de clave

El material de clave de `IEncryptionProvider` **no vive en `Shared.Infrastructure.Security.Encryption`**:
se resuelve en tiempo de uso a través de `ISecretProvider` (F2-12), exactamente igual que cualquier otro
secreto del framework. Dos claves lógicas por defecto (configurables vía `EncryptionOptions`):

- `Encryption:ActiveKeyVersion` → el identificador de versión (`"1"`, `"2"`, ...) usado para **cifrar**
  datos nuevos.
- `Encryption:Keys:{version}` → el material de esa versión (AES-256, 32 bytes, Base64).

En desarrollo (`ConfigurationSecretProvider`) esas claves se pueblan vía `dotnet user-secrets`/variables de
entorno, igual que cualquier secreto de F2-12. En producción, un proyecto real las aprovisiona en el mismo
proveedor de secretos de nivel empresarial que ya haya adoptado (Vault KV v2, `Proposed` — ADR 0014;
Azure Key Vault/AWS KMS si el proyecto los adoptó por su cuenta, ver "Decisiones pendientes" abajo).

### 3.2 Quién gestiona las claves

Generación, aprovisionamiento y rotación del material de clave son responsabilidad del equipo de
plataforma/seguridad del proyecto consumidor (nunca de un desarrollador individual ni de un pipeline sin
control de acceso) — mismo modelo de responsabilidad que F2-12 define para el resto de los secretos
("Secretos externos al repositorio", gate de salida de la Fase 2). `BitCode.Framework` no genera ni
almacena claves por sí mismo: solo define el contrato (`IEncryptionProvider`) y la convención de dónde
resolverlas (`ISecretProvider`).

### 3.3 Formato del texto cifrado (versionado explícito)

`AesGcmEncryptionProvider.EncryptAsync` produce `"v{version}.{Base64(nonce(12) || ciphertext || tag(16))}"`.
La versión de clave usada queda embebida en el propio texto cifrado, en claro (no es secreta — identifica
qué clave usar, no la expone). Esto es la base tanto de la rotación como de la recuperación:
**`DecryptAsync` nunca depende de cuál sea la versión activa en el momento de descifrar**, solo de que la
versión embebida en el dato exista en `ISecretProvider`.

### 3.4 Rotación (sin downtime)

1. Generar una nueva versión de material de clave (AES-256, 32 bytes aleatorios) fuera de banda (el propio
   mecanismo del proveedor de secretos, por ejemplo `vault kv put` contra Vault).
2. Aprovisionar esa nueva versión en `Encryption:Keys:{nueva-version}` sin tocar
   `Encryption:Keys:{version-anterior}` (se conserva archivada).
3. Actualizar `Encryption:ActiveKeyVersion` a la nueva versión.

Ninguno de los tres pasos exige desplegar código nuevo ni reiniciar el proceso: desde el siguiente
`EncryptAsync`, los datos nuevos usan la clave nueva; los datos existentes, cifrados con la versión
anterior, se siguen descifrando sin ningún cambio porque `DecryptAsync` resuelve la clave por la versión
embebida, no por el puntero de "versión activa". Probado explícitamente en
`AesGcmEncryptionProviderTests.RotatesActiveKey_WithoutBreakingDecryptionOfDataEncryptedBeforeRotation`.

Reencriptar datos existentes con la clave nueva (para poder eventualmente retirar una versión antigua) es
un proceso de background del proyecto consumidor (leer con la clave vieja, escribir con la clave nueva),
fuera de alcance de F2-13 — ver "Fuera de alcance" más abajo.

### 3.5 Recuperación

Si el puntero de "versión activa" (`Encryption:ActiveKeyVersion`) se pierde, se corrompe, o queda
apuntando a una versión inexistente (error operativo al rotar, fallo del proveedor de secretos sobre esa
única clave), **los datos ya cifrados siguen siendo recuperables** mientras el material de su propia
versión (archivada, nunca eliminada por la política de rotación de la sección 3.4) siga existiendo en
`ISecretProvider` — la recuperación no depende de la salud del puntero de versión activa, solo de la
versión embebida en cada texto cifrado. Cifrar datos *nuevos* sí requiere que el puntero esté sano (es un
fallo esperado y explícito, `Encryption.ActiveKeyVersionNotConfigured`/`Encryption.KeyNotFound`, nunca una
excepción sin traducir). Probado explícitamente en
`AesGcmEncryptionProviderTests.Recovery_ArchivedKeyStillDecryptsData_EvenIfActiveKeyPointerIsLostOrCorrupted`.

**Recuperación ante pérdida total de una versión de clave (no solo del puntero):** si el material de una
versión concreta se pierde de forma irrecuperable (el propio Vault/KMS la perdió, no solo el puntero), los
datos cifrados con esa versión son **irrecuperables por diseño** — es la misma propiedad que hace posible
el "crypto shredding" deliberado (ver sección 4). No hay, ni debe haber, una puerta trasera de recuperación
sin el material de clave: la política asume que el proveedor de secretos/KMS elegido (Vault con
almacenamiento durable, o un KMS gestionado) es el mecanismo de continuidad de esa clave, con su propio
backup/HA — responsabilidad operativa fuera de alcance de F2-13 (ver ADR 0014, "adopción productiva real
de Vault requiere aprobación humana").

## 4. Retención y eliminación de versiones de clave ("crypto shredding")

La política por defecto es **archivar indefinidamente** cada versión de clave rotada (nunca eliminarla
automáticamente): eliminar una versión antes de reencriptar todos los datos que dependen de ella los vuelve
irrecuperables. Un proyecto puede usar la eliminación deliberada de una versión de clave como mecanismo de
"crypto shredding" (borrado lógico de todos los datos cifrados bajo esa versión, por ejemplo para cumplir
un derecho de supresión/GDPR sobre un conjunto de datos) — pero es una decisión operativa explícita del
proyecto consumidor, no un comportamiento automático de `IEncryptionProvider`.

## 5. Fuera de alcance de F2-13

- **Wrapping de la clave maestra en un HSM/KMS externo (envelope encryption con un KMS gestionado)** — hoy
  el material de clave es un valor plano resuelto vía `ISecretProvider`; envolverlo con un KMS
  (AWS KMS/Azure Key Vault Keys/HashiCorp Transit) es una decisión de proveedor concreto, sujeta al mismo
  tratamiento de la sección 13 del Plan Maestro que ya recibió la elección de proveedor de secretos (ADR
  0014) — **si se adopta, corresponde un ADR nuevo o un addendum a ADR 0014**, no una decisión tomada
  dentro de esta tarea.
- **Reencriptado automático de datos existentes tras una rotación** ("key rewrap" en background) — queda
  como responsabilidad operativa del proyecto consumidor.
- **Integración lista para usar con EF Core** (`ValueConverter` genérico sobre `IEncryptionProvider` para
  anotar una propiedad de entidad como cifrada) — no se agrega en F2-13 para no acoplar
  `Shared.Infrastructure.Security` a `Shared.Infrastructure.Persistence`/EF Core sin una necesidad concreta
  todavía; un proyecto consumidor puede escribir su propio `ValueConverter` fino sobre `IEncryptionProvider`
  hoy mismo sin esperar a una tarea nueva.
- **mTLS servicio-a-servicio** — F2-14, tarea separada de la misma épica. Ver
  [`docs/contrato-mtls-servicios.md`](contrato-mtls-servicios.md) y
  [ADR 0015](adr/0015-mtls-contrato-operativo-servicio-a-servicio.md).
- **Auditoría íntegra/resistente a manipulación** — Épica F2-D, mecanismo distinto (firma/hash encadenado,
  no cifrado de confidencialidad).

## 6. Registro: `AddSharedEncryption`

```csharp
services.AddSharedSecretProvider(configuration); // F2-12, debe registrarse antes
services.AddSharedEncryption(configuration);      // F2-13
```

```csharp
public interface IEncryptionProvider
{
    Task<Result<string>> EncryptAsync(string plaintext, CancellationToken cancellationToken = default);
    Task<Result<string>> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default);
}
```

Un fallo de resolución de clave (versión activa no configurada, versión específica inexistente, material
de clave con formato/longitud inválida) o un texto cifrado alterado/corrupto son `Result.Failure`, nunca
una excepción sin traducir — mismo criterio que el resto de los contratos de infraestructura del framework
(`ISecretProvider`, F2-12).

## Pruebas

- `tests/Shared.Infrastructure.Security.Tests/Encryption/AesGcmEncryptionProviderTests.cs` — cifrado/
  descifrado correcto, no determinismo del nonce, detección de alteración (tag de autenticación), formato
  de texto cifrado inválido, clave activa no configurada, material de clave de longitud inválida, versión
  de clave inexistente, **rotación sin interrumpir el descifrado de datos previos**, **recuperación con
  puntero de versión activa perdido/corrupto** (los dos últimos son el criterio de aceptación explícito de
  F2-13).
- `tests/Shared.Infrastructure.Security.Tests/Encryption/EncryptionServiceCollectionExtensionsTests.cs` —
  registro de `IEncryptionProvider`, defaults y configuración de claves lógicas personalizadas.

## Referencias

- [`docs/convenciones.md`](convenciones.md) — cuándo usar `IEncryptionProvider`.
- [`docs/guia-secret-provider.md`](guia-secret-provider.md) — F2-12, base de la que depende la gestión de
  claves de esta tarea.
- [`docs/adr/0014-secretos-proveedor-vault-propuesto.md`](adr/0014-secretos-proveedor-vault-propuesto.md)
  — decisión de proveedor de secretos, de la que depende dónde vive realmente el material de clave en
  producción.
