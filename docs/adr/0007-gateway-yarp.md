# 0007. Gateway: YARP como API Gateway / punto de entrada

**Estado:** Proposed
**Fecha:** 2026-09-05
**Responsable:** Pendiente de asignación

## Contexto

El Plan Maestro (sección 2) fija YARP como decisión rectora de gateway, y el Plan Maestro (sección 4.1) clasifica "API Gateway: Ausente — Integrar YARP" como brecha a resolver. El repositorio actual (`docs/inventario-tecnico.md`) no tiene ningún proyecto de gateway; `Sample.Api` expone sus endpoints directamente sin capa de gateway/BFF intermedia. La introducción de un gateway está relacionada con Fase 2 (BFF, F2-03) y con la Fase 4 (runtime de alta disponibilidad) y la Fase 9 (extracción de microservicios, donde un gateway es el punto natural de enrutamiento entre monolito y servicios extraídos).

## Decisión

Se adopta YARP como tecnología de API Gateway / reverse proxy para BitCode, a introducir cuando la Fase correspondiente (runtime de alta disponibilidad, Fase 4, y/o BFF de Fase 2) lo requiera. Se marca `Proposed` porque, aunque ya está fijado como decisión rectora en la sección 2 del Plan Maestro, su incorporación real (nuevo proyecto de runtime, configuración de enrutamiento, exposición pública) todavía no existe en el código y su despliegue en un entorno productivo cae bajo "habilitación de tráfico productivo" (sección 13 del Plan Maestro), que requiere aprobación humana antes de operar en producción. El diseño y la construcción del gateway en sí (no su habilitación productiva) pueden proceder según el backlog de Fase 2/4.

## Alternativas consideradas

- **Nginx o un API Management gestionado (p. ej. Azure API Management):** no evaluados en profundidad; el Plan Maestro ya fija YARP como decisión rectora del stack objetivo (sección 2), coherente con ser una librería .NET embebible que facilita el BFF (F2-03) sin agregar una tecnología ajena al stack .NET.
- **Sin gateway, exposición directa de cada módulo/servicio:** descartado para Fase 4 en adelante, porque el modelo de extracción selectiva de microservicios (sección 2.2) y el BFF de aplicaciones críticas (F2-03) requieren un punto de enrutamiento y agregación común.

## Consecuencias

- No implica código nuevo en esta tarea; formaliza la dirección para F2-03 (BFF) y para la Fase 4 (runtime de alta disponibilidad).
- El diseño del gateway debe considerar desde el inicio autenticación (tokens no expuestos al navegador en BFF, ADR 0004) y observabilidad (trazas propagadas end-to-end, `docs/architecture-principles.md` sección 4).

## Riesgos y mitigación

- **Riesgo:** habilitar el gateway en producción sin la aprobación humana exigida por la sección 13 del Plan Maestro ("habilitación de tráfico productivo"). Mitigación: tratar el despliegue productivo del gateway como un punto de decisión separado de su construcción/diseño.
- Vinculado al registro de riesgos de F0-12 (pendiente de creación).
