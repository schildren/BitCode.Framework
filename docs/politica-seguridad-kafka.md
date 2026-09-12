# Política de seguridad de transporte de Kafka (F3-11)

**Tarea:** F3-11 (Fase 3 — Plataforma de eventos) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Trabajo:** Seguridad — aplicar TLS, ACL, identidad y mínimo privilegio.
**Criterio de aceptación:** "Acceso cruzado denegado".

## Objetivo y alcance real de esta tarea

Hasta F3-02, `KafkaMessagingOptions` (`Shared.Infrastructure.Messaging.Kafka`) dejaba preparadas —
pero sin usar ni validar— las propiedades de seguridad de transporte
(`SecurityProtocol`/`SaslMechanism`/`SaslUsername`/`SaslPassword`/`SslCaLocation`), y
`docs/matriz-soporte.md` documentaba explícitamente que solo se probaba en modo
`SecurityProtocol.Plaintext` contra el broker efímero de `Testcontainers.Kafka` en CI.

F3-11 hace dos cosas concretas:

1. **Completa el mapeo** de `KafkaMessagingOptions` hacia `Confluent.Kafka.ClientConfig`
   (`KafkaClientConfigFactory`) para las 4 combinaciones de `SecurityProtocol` que soporta la
   librería (`Plaintext`, `SaslPlaintext`, `Ssl`, `SaslSsl`), agrega soporte para mTLS
   (`SslCertificateLocation`/`SslKeyLocation`/`SslKeyPassword`) y agrega
   `KafkaMessagingOptionsValidator`, que falla en el arranque (`AddSharedMessagingKafka`) ante una
   combinación inconsistente (por ejemplo, un `SaslMechanism` configurado con `SecurityProtocol.Plaintext`,
   que `Confluent.Kafka` simplemente ignoraría en silencio, dejando el cliente sin autenticar).
2. **Documenta** (este archivo) la configuración productiva recomendada: protocolo, rotación de
   credenciales, y ACL por tópico/bounded context con el criterio de mínimo privilegio.

**Lo que esta tarea NO hace** (y por qué, con honestidad sobre el alcance):

- **No habilita tráfico productivo sobre Kafka.** El addendum F3-02 del ADR
  [0005](adr/0005-mensajeria-kafka.md) es explícito: la aprobación humana ya obtenida cubre
  desarrollo/CI/Testcontainers, no producción — aprovisionar y habilitar Kafka productivo sigue
  requiriendo una aprobación humana explícita adicional en el momento de ese despliegue (Plan
  Maestro, sección 13). Esta tarea deja la *configuración* de transporte lista para ese momento,
  no la ejecuta.
- **No verifica ACL contra un broker real con Testcontainers.** Se evaluó armar un contenedor de
  `Testcontainers.Kafka` con SASL/SCRAM y ACL habilitados (varios usuarios, `kafka-acls.sh`,
  `authorizer.class.name=kafka.security.authorizer.AclAuthorizer` o
  `org.apache.kafka.metadata.authorizer.StandardAuthorizer` según versión de broker). La imagen
  por defecto que usa `Testcontainers.Kafka` (`confluentinc/cp-kafka:6.1.9`, ver
  `docs/matriz-soporte.md`) arranca con un script de entrypoint pensado para `PLAINTEXT` de un
  único listener; reconfigurarlo para SASL/SCRAM con múltiples usuarios y ACL reales requiere un
  `server.properties`/JAAS a medida, bootstrap de credenciales SCRAM vía `kafka-configs.sh`
  *después* de que el broker ya está arriba, y coordinar el orden de arranque — un esfuerzo
  desproporcionado para el alcance de esta tarea concreta del backlog, y con riesgo real de dejar
  un test de integración frágil/no determinista si no se hace con cuidado. En vez de eso:
  - Sí se verificó, con **tests unitarios reales, sin broker** (`tests/Shared.Infrastructure.Messaging.Kafka.Tests/KafkaClientConfigFactoryAndSecurityValidationTests.cs`), que el mapeo de
    `KafkaMessagingOptions` → `ClientConfig` es correcto para las 4 combinaciones de protocolo, y
    que `KafkaMessagingOptionsValidator` rechaza en el arranque las combinaciones inconsistentes.
  - **No** se verificó automáticamente que un cliente con credenciales SASL inválidas o sin permiso
    ACL sobre un tópico sea efectivamente rechazado por un broker real — ver la sección
    "Verificación pendiente" más abajo para el runbook que debe ejecutar quien aprovisione el
    primer entorno con Kafka real (staging o productivo).

## Configuración recomendada por ambiente

| Ambiente | `SecurityProtocol` | Notas |
|---|---|---|
| Local / CI (Testcontainers) | `Plaintext` (default de `KafkaMessagingOptions`) | Sin cambios — sigue siendo el único modo probado contra broker real en este repositorio. |
| Staging / Producción | `SaslSsl` con `SaslMechanism.ScramSha512` (o `ScramSha256` si el broker gestionado no soporta 512) | Recomendado por sobre `SaslPlaintext` (credenciales SASL/PLAIN viajarían en texto claro sin TLS) y por sobre mTLS puro (rotar un usuario SCRAM es más simple operativamente que reemitir certificados de cliente en cada bounded context). |
| Caso especial: broker detrás de un proxy/mesh que ya termina TLS internamente | `SaslPlaintext` | Solo si el operador de infraestructura garantiza que el tráfico SASL_PLAINTEXT nunca sale de una red ya cifrada a nivel de transporte (p. ej. mTLS del service mesh) — decisión de infraestructura, documentar explícitamente en el runbook de ese despliegue. |

No se recomienda `Ssl` (TLS sin SASL) ni mTLS puro como mecanismo *principal* de identidad: la
autorización por ACL de Kafka funciona sobre un "principal" (usuario SASL o "SSL principal" del
certificado de cliente); `KafkaMessagingOptions` soporta ambos caminos
(`SaslUsername`/`SaslPassword` o `SslCertificateLocation`/`SslKeyLocation`) para no cerrar la
puerta a mTLS si un despliegue concreto lo prefiere (por ejemplo, un service mesh que ya emite
certificados por servicio), pero SASL/SCRAM es la recomendación por defecto de este framework por
ser más simple de rotar sin herramienta de PKI adicional.

## Resolución de credenciales: quién llena `SaslUsername`/`SaslPassword`

`Shared.Infrastructure.Messaging.Kafka` **no** referencia `Shared.Infrastructure.Security` — mismo
criterio de bajo acoplamiento aplicado en F3-10 para no acoplar este proyecto a
`Shared.Infrastructure.Observability`. `KafkaMessagingOptions.SaslUsername`/`SaslPassword` siguen
siendo `string`/`string?` planos.

La resolución del secreto real es responsabilidad del **host** (el proyecto consumidor que llama
`AddSharedMessagingKafka`), típicamente de una de estas dos formas:

1. **Recomendado:** el host resuelve el secreto con `ISecretProvider` (F2-12,
   `Shared.Infrastructure.Security/Secrets`, con `ConfigurationSecretProvider` en desarrollo y
   `VaultSecretProvider` en producción) *antes* de construir/enlazar `KafkaMessagingOptions`, y
   asigna el valor ya resuelto (por ejemplo, sobrescribiendo `IConfiguration` con un
   `ConfigurationBuilder` encadenado, o llamando `AddSharedMessagingKafka` después de mutar la
   sección `Messaging:Kafka` en memoria con el valor resuelto).
2. **Alternativa igualmente válida:** si el proveedor de secretos de la plataforma (Vault, un
   Secret Store de Kubernetes, Azure Key Vault) ya se integra como un `IConfigurationProvider` de
   `IConfiguration` (patrón estándar de .NET), `configuration.GetSection("Messaging:Kafka").Get<KafkaMessagingOptions>()`
   dentro de `AddSharedMessagingKafka` ya lo resuelve solo, sin ningún código adicional en este
   proyecto.

En ningún caso `SaslUsername`/`SaslPassword` deben aparecer hardcodeados en `appsettings.json`
commiteado al repositorio (regla dura de `docs/convenciones.md`, ya aplicable a cualquier secreto).

## Identidad y mínimo privilegio: un principal por bounded context

Regla operativa (a aplicar cuando se aprovisione el primer entorno con Kafka real, sección 13 del
Plan Maestro):

- **Un usuario SASL/SCRAM por bounded context o servicio**, no un usuario compartido para todo el
  despliegue. El `ClientId` de `KafkaMessagingOptions` identifica al cliente en logs/métricas, pero
  la identidad real frente al broker (y la base de las ACL) es el usuario SASL.
- **ACL explícitas por tópico**, sin comodines amplios (`*`):
  - Producir: `kafka-acls.sh --add --allow-principal User:<svc> --operation Write --topic <topic-del-bounded-context>`.
  - Consumir: `--operation Read --topic <topic>` + `--operation Read --group <consumer-group-id>` (el
    `ConsumerGroupId` de `KafkaMessagingOptions`/`KafkaEventConsumer<TEvent>`) — Kafka autoriza el
    `Read` de un tópico y el uso del consumer group como permisos separados; ambos hacen falta.
  - Publicar a un DLQ (F3-08, `KafkaDeadLetterPublisher`, sufijo `.dlq`): mismo principal que
    publica al tópico original, `Write` explícito también sobre `<topic>.dlq`.
  - **Denegar todo lo demás por defecto** (`allow.everyone.if.no.acl.found=false` en el broker,
    equivalente al *default deny* que ya aplica el resto del framework — ver
    `docs/architecture-principles.md` sección 3).
- **Nunca reutilizar el usuario administrador del cluster** (el que crea tópicos/ACL) como
  identidad de un publicador o consumidor de aplicación.

## Rotación de credenciales

1. Crear la credencial SCRAM nueva sin borrar la vieja (`kafka-configs.sh --alter --add-config
   'SCRAM-SHA-512=[password=<nueva>]' --entity-type users --entity-name <svc>` crea una nueva
   versión de credencial para el mismo usuario, sin downtime).
2. Actualizar el secreto en el proveedor (`VaultSecretProvider`/equivalente) con la nueva
   contraseña.
3. Reiniciar (o recargar configuración de) el servicio consumidor/productor para que tome la
   credencial nueva — `KafkaMessagingOptions` se resuelve una sola vez al construir el
   `IServiceCollection` (singleton), no hay hot-reload de credenciales SASL en este adapter.
4. Una vez confirmado que el servicio nuevo autentica correctamente, eliminar la credencial vieja
   (`--delete-config 'SCRAM-SHA-512'`).

No hay automatización de este flujo en el framework — es un procedimiento operativo del equipo que
administra el cluster Kafka, documentado acá para que quede trazable junto con el resto de las
decisiones de seguridad de transporte.

## Verificación pendiente (runbook para el primer despliegue con Kafka real)

Antes de habilitar tráfico productivo sobre Kafka (aprobación humana requerida, ADR 0005 addendum
F3-02), quien aprovisione ese entorno debe verificar manualmente, contra el broker real:

1. Un cliente con `SecurityProtocol.SaslSsl` y credenciales SASL **inválidas** falla al conectar
   (no publica ni consume) — evidencia de autenticación real, no solo configuración aceptada.
2. Un cliente con credenciales SASL **válidas** pero **sin ACL** sobre un tópico de otro bounded
   context recibe `TopicAuthorizationFailed`/`GroupAuthorizationFailed` al intentar publicar o
   consumir ese tópico — evidencia del criterio de aceptación de esta tarea, "Acceso cruzado
   denegado", a nivel de broker (no solo de aplicación).
3. El mismo cliente, sobre su propio tópico autorizado, publica/consume sin error.
4. La conexión TLS valida la cadena de certificados del broker contra `SslCaLocation` (rechazo si
   se apunta a una CA incorrecta).

Estos 4 puntos quedan fuera del alcance de esta tarea (no hay entorno con broker SASL/ACL real
disponible en CI) y deben ejecutarse y registrarse como evidencia en el runbook de despliegue del
primer entorno real, no como parte de la suite automatizada de este repositorio.
