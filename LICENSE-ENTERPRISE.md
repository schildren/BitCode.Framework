# BitCode Enterprise License (interim)

**Estado:** Marco interino — pendiente de redacción legal definitiva. Ver
[`docs/adr/0008-licencias-open-core.md`](docs/adr/0008-licencias-open-core.md).

Este documento cubre el código listado bajo "Capacidades empresariales" en
[`NOTICE.md`](NOTICE.md) — los 12 módulos de la Plataforma Funcional Empresarial (Fase 6 del
Plan Maestro) y sus paquetes/UI asociados. El resto del repositorio se distribuye bajo
[Apache License 2.0](LICENSE); ver `NOTICE.md` para el límite exacto entre ambos regímenes.

## Por qué este documento es un marco interino, no un contrato final

Fijar los términos comerciales definitivos de una licencia propietaria (duración de uso,
garantías, límites de responsabilidad, condiciones de reventa/sublicencia, soporte, SLA) es
una decisión de negocio y legal que excede el alcance de una tarea de ingeniería ejecutada por
un agente de IA. Este documento establece el **límite técnico** (qué código está bajo régimen
propietario) y un **default protector** mientras esa redacción legal definitiva no exista,
para no dejar el código empresarial de facto sin ninguna protección.

## Términos interinos

1. **Todos los derechos reservados.** Salvo acuerdo escrito explícito con el titular de los
   derechos, no se otorga ningún permiso de uso, copia, modificación, distribución,
   sublicencia o explotación comercial del código listado como "Capacidades empresariales" en
   `NOTICE.md`.
2. **Uso interno de evaluación.** Se permite clonar, compilar y ejecutar este código con fines
   de evaluación técnica interna (p. ej. dentro de la organización titular del repositorio),
   sin derecho a redistribución a terceros ni a uso en producción por parte de terceros sin
   un acuerdo comercial.
3. **Sin garantía.** El código se provee "tal cual", sin garantías de ningún tipo, expresas o
   implícitas, incluidas (sin limitarse a) garantías de comerciabilidad, idoneidad para un
   propósito particular o no infracción.
4. **Reemplazo obligatorio.** Este marco interino debe ser reemplazado por un texto de licencia
   comercial definitivo, revisado por asesoría legal, antes de:
   - publicar cualquier paquete NuGet/npm que contenga código de esta categoría (ver Gate de
     salida de Fase 8, ítem 3, `docs/plan-maestro-bitcode-ia.md`), o
   - ofrecer este código bajo cualquier forma de acuerdo comercial con un tercero.

## Relación con el núcleo Apache-2.0

El núcleo del framework (`LICENSE`, Apache 2.0) puede usarse, modificarse y redistribuirse
libremente bajo sus propios términos, con independencia de este documento. Un consumidor que
sólo use el núcleo (`Shared.*`, `BitCode.Gateway`, plantillas, paquetes Angular genéricos) no
está sujeto a los términos de este documento.
