# 0015. mTLS servicio-a-servicio: contrato operativo, mecanismo de emisión propuesto

**Estado:** Proposed
**Fecha:** 2026-09-06
**Responsable:** Pendiente de aprobación humana explícita (Plan Maestro sección 13)

## Contexto

El Plan Maestro (Fase 2, Épica F2-C — Secretos y criptografía, F2-14: "mTLS contracts — Preparar
identidad y certificados entre servicios") pide un entregable explícitamente llamado **"Contratos
operativos"** (no "implementación mTLS" ni "provider", a diferencia de F2-12 "Abstracción y provider" o
F2-13 "Política criptográfica") con criterio de aceptación **"Aplicable al extraer servicios"** — no
"mTLS operando hoy". El estado real del repositorio al iniciar esta tarea (`docs/inventario-tecnico.md`,
`docs/plan-maestro-bitcode-ia.md` sección 2.2 "Regla para microservicios", Fase 9 "Capacidad de
extracción de microservicios") es un monolito modular de un único proceso desplegable: no existen hoy
dos servicios separados en red que necesiten autenticarse mutuamente por TLS, y la Fase 9 (donde eso
empezaría a ser cierto) todavía no se alcanzó ni tiene fecha — depende de que un módulo concreto
demuestre elegibilidad real (matriz de la sección "Criterios de elegibilidad" de la Fase 9), algo que
tampoco ocurrió todavía.

El Plan Maestro (sección 2, "Seguridad entre servicios") ya fija la dirección: "Workload identity, OAuth2
Client Credentials y mTLS **cuando existan servicios separados**". F2-04 (ADR 0004, Épica F2-A) ya
implementó identidad de servicio vía OAuth2 Client Credentials (`IServiceTokenProvider`) y dejó un campo
de contrato explícitamente preparado y sin implementar para esta evolución
(`ServiceIdentityOptions.CertificateThumbprint`, ver `docs/guia-oidc-adapter.md` sección "F2-04", "queda
fuera de alcance"): "autenticación de cliente por certificado (mTLS/`private_key_jwt`) o workload
identity federada". F2-13 (ADR de política criptográfica, `docs/politica-criptografica.md`) también
señaló explícitamente esta tarea como pendiente y separada ("mTLS servicio-a-servicio — F2-14, tarea
separada de la misma épica").

## Decisión

Se separan dos decisiones de distinto nivel, siguiendo el mismo patrón que ADR 0004/0005/0007/0014
(contrato/dirección arquitectónica aceptada ahora, elección de tecnología/proveedor concreto pendiente de
aprobación humana o de la fase que la necesite realmente):

1. **El contrato operativo de mTLS queda `Accepted` como documento de referencia
   (`docs/contrato-mtls-servicios.md`)**: qué debe cumplir cualquier servicio extraído en el futuro
   (Fase 9) antes de poder llamar o ser llamado por otro servicio del framework — provisión de
   identidad/certificado, rotación sin downtime, validación mutua obligatoria en ambos sentidos, y cómo
   convive con la identidad de servicio OAuth2 Client Credentials ya implementada en F2-04 (mTLS
   **complementa**, no reemplaza: autentica el canal de transporte/la identidad de la carga de trabajo a
   nivel de red; el token de Client Credentials sigue siendo la autorización a nivel de aplicación —
   defensa en profundidad de dos capas, ninguna sustituye a la otra, Plan Maestro sección 1 "Zero
   Trust — verificar siempre, en cada capa"). El contrato es independiente del mecanismo de emisión de
   certificados concreto (punto 2): describe requisitos verificables (rotación, revocación, validación),
   no la tecnología que los satisface.
2. **El mecanismo concreto de emisión/rotación de certificados de identidad de servicio (SPIFFE/SPIRE
   propuesto como candidato de referencia, PKI interna propia o `cert-manager` de Kubernetes como
   alternativas) queda `Proposed`, no `Accepted`** — ninguno de los tres se adopta formalmente en esta
   tarea. Elegir uno es, en los mismos términos que ADR 0014 trató "elección de proveedor de
   secretos/KMS", una decisión que compromete infraestructura operativa real (una autoridad de
   certificación viva, con su propia política de confianza/HA/rotación) y que hoy no tiene ningún
   consumidor real que la necesite (no existe ni un segundo servicio separado en este repositorio) —
   corresponde recién cuando la Fase 9 identifique el primer módulo elegible para extracción (F9-01,
   "Selección piloto... Aprobación humana") y ese runtime independiente (F9-05, "Host independiente")
   necesite presentar y validar certificados reales.
3. **No se agrega código nuevo en esta tarea.** A diferencia de F2-12/F2-13 (que sí entregaron una
   abstracción con implementación de referencia, `ISecretProvider`/`IEncryptionProvider`, aun sin un
   consumidor de dominio real en el propio framework), F2-14 no tiene siquiera un segundo proceso HTTP
   separado del framework contra el cual ejercer una prueba de mTLS con sentido: construir hoy un
   `IServiceCertificateProvider` o una configuración de `SocketsHttpHandler`/Kestrel para presentar y
   validar certificados de cliente sería código sin caso de uso verificable end-to-end (ningún test de
   integración podría probar "dos servicios separados se autentican mutuamente" porque esos dos
   servicios no existen), y arriesgaría quedar desactualizado o con una interfaz equivocada para cuando
   la Fase 9 realmente lo necesite, contradiciendo la regla de la sección 3.2 del Plan Maestro de no
   construir infraestructura sin necesidad demostrada ("Crear microservicios antes de demostrar sus
   límites de datos y operación", sección 2.2/3.2). El entregable literal de la fila del backlog es
   "Contratos operativos" (no "provider" ni "abstracción e implementación", a diferencia de F2-12/F2-13),
   lo que refuerza esta lectura.

## Alternativas consideradas (para el mecanismo de emisión, punto 2 — todas `Proposed`, ninguna elegida)

- **SPIFFE/SPIRE:** estándar abierto de identidad de carga de trabajo (SVID de X.509, rotación automática
  de corta vida, atestación de nodo/workload), self-hosteable (consistente con Keycloak/Vault — ADR
  0004/0014 — y con la filosofía del repositorio de no atar el entorno de referencia a un servicio
  gestionado de un solo proveedor cloud), con soporte nativo para el modelo "un certificado por
  workload, de corta vida, rotado automáticamente" que exige la sección 3.4 del contrato operativo.
  Candidato de referencia recomendado en el contrato, no elegido formalmente.
- **PKI interna propia (CA privada operada por el equipo de plataforma, `openssl`/`step-ca`/similar):**
  más simple de operar al inicio (sin agregar un componente de infraestructura nuevo tipo SPIRE), pero
  desplaza a un proceso manual/scripts propios la rotación automática y la atestación de identidad que
  SPIFFE/SPIRE da de fábrica — mayor riesgo operativo a medida que crece el número de servicios
  extraídos.
- **`cert-manager` (Kubernetes):** apropiado si el runtime objetivo de los servicios extraídos (F9-05,
  "Host independiente") termina siendo Kubernetes — no evaluado en profundidad porque ningún ADR previo
  fija Kubernetes como plataforma de despliegue objetivo del framework (a diferencia de Docker/
  Testcontainers, ya en uso transversal para desarrollo/CI).
- **Certificate Manager gestionado de un proveedor cloud (AWS Certificate Manager Private CA, Azure
  Key Vault como CA):** mismo motivo de descarte que Azure Key Vault/AWS Secrets Manager en ADR 0014 —
  ningún ADR previo fija esa nube como plataforma objetivo; ataría el entorno de referencia a un tenant
  cloud de pago.

## Consecuencias

- **F2-14 implementado como documento:** `docs/contrato-mtls-servicios.md` (contrato operativo completo:
  emisión/rotación de certificados, validación mutua bidireccional, convivencia con F2-04, matriz de
  aplicabilidad — "aplica cuando" vs "no aplica hoy"). Este ADR fija la decisión de nivel arquitectónico;
  el documento de contrato es el artefacto operable/consultable que un futuro F9-05 (Host independiente)
  debe satisfacer.
- **Gate de salida de Fase 2** ("mTLS entre servicios extraídos: Obligatorio", sección 8.3 del Plan
  Maestro) queda correctamente interpretado como aplicable recién quando existan servicios extraídos
  (Fase 9) — no se declara satisfecho hoy porque hoy no aplica; el contrato deja documentado qué debe
  cumplirse en ese momento, y este ADR dejará de estar `Proposed` (pasará a `Accepted` sobre el mecanismo
  elegido) cuando F9-01 identifique el primer piloto de extracción y la elección de mecanismo de emisión
  de certificados se apruebe explícitamente (sección 13 del Plan Maestro).
- Ningún código existente (`ServiceIdentityOptions.CertificateThumbprint`, `ServiceTokenProvider`) cambia
  en esta tarea: sigue fallando explícitamente (`ServiceIdentity.CertificateAuthenticationNotSupported`)
  si se configura sin `ClientSecret`, tal como F2-04 ya lo dejó documentado.

## Riesgos y mitigación

- **Riesgo:** que un proyecto consumidor extraiga un servicio (Fase 9) sin haber revisado
  `docs/contrato-mtls-servicios.md` y despliegue tráfico servicio-a-servicio sin mTLS, violando el gate
  de salida de Fase 2 ("mTLS entre servicios extraídos: Obligatorio"). Mitigación: F9-02 (Contract
  boundary) y F9-05 (Host independiente) deben referenciar explícitamente este ADR y el contrato
  operativo como parte de su Definition of Ready.
- **Riesgo:** elegir el mecanismo de emisión (SPIFFE/SPIRE vs PKI interna vs `cert-manager`) tarde,
  bajo presión de una extracción ya en curso. Mitigación: el contrato operativo ya deja la recomendación
  de referencia (SPIFFE/SPIRE) documentada para acelerar esa decisión cuando corresponda, sin obligar a
  adoptarla sin aprobación humana.
- Vinculado al registro de riesgos de F0-12 y a la sección 13 del Plan Maestro.
