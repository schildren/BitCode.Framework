# Guía del Portal Técnico y Documentación Viva (F8-09)

## 1. Propósito y Alcance

El **Portal Técnico** de BitCode.Framework centraliza y unifica la documentación viva del ecosistema en una aplicación SPA interactiva y moderna. Consolida:
1. **Documentos de Arquitectura y Guías de Desarrollo:** Más de 100 documentos en formato Markdown categorizados temáticamente.
2. **Registro de Decisiones Arquitectónicas (ADRs):** De 0001 a 0019 con filtrado y badges visuales según su estado (`Accepted`, `Proposed`).
3. **Catálogo de APIs (OpenAPI):** Especificaciones OpenAPI 3.1.1 de los servicios de la plataforma (`sample-api`, `sample-documents-api`, `sample-workflow-api`) con desglose de endpoints, métodos HTTP tipados y payloads JSON.
4. **Scaffolding y Patrones:** Ejemplos canónicos de código backend, frontend y testing de arquitectura para adopción inmediata por parte de los equipos de producto.

---

## 2. Arquitectura del Portal

El portal sigue una filosofía de **cero dependencias externas en tiempo de ejecución**, asegurando portabilidad, velocidad instantánea y soporte offline:
- **Compilador (`portal/build-portal.mjs`):** Script en Node.js que escanea recursivamente `docs/`, `docs/adr/` y `docs/openapi/`, extrayendo títulos, categorías, metadatos y esquemas OpenAPI para estructurar el archivo estático `portal/portal-data.json`.
- **Visor SPA (`portal/index.html`):** Interfaz basada en estándares web modernos (CSS Grid/Flexbox, Vanilla JS, Dark Mode nativo estilo developer tool). Cuenta con:
  - Buscador reactivo instantáneo sobre títulos, contenidos, rutas y operaciones.
  - Navegación por pestañas (*Docs*, *ADRs*, *APIs*, *Ejemplos*).
  - Renderer Markdown integrado con protección de bloques de código y soporte para alertas estilo GitHub.
  - Botones de copiado al portapapeles en todos los bloques de código.
- **Servidor Local (`portal/serve.mjs`):** Servidor HTTP liviano sin librerías externas (puerto 4280 por defecto).

---

## 3. Uso y Comandos

Los comandos están integrados en el espacio de trabajo de Node/Frontend (`frontend/package.json`):

### Compilar datos del portal
Lee la documentación y especificaciones actuales para regenerar el catálogo:
```bash
cd frontend
npm run portal:build
```
*Salida esperada:*
```text
Construyendo datos del portal técnico...
Portal data compilado exitosamente en: portal/portal-data.json
- Documentos técnicos: 103
- ADRs procesados: 19
- Especificaciones OpenAPI: 4
- Ejemplos interactivos: 3
```

### Iniciar servidor de visualización local
```bash
cd frontend
npm run portal:serve
```
Abrir navegador en: `http://localhost:4280`

---

## 4. Integración Continua (CI)

En `.github/workflows/ci.yml`, el paso `contracts-verify` incluye la validación automatizada del portal:
```yaml
- name: Verificar compilación del portal técnico (portal:build)
  run: npm run portal:build
  working-directory: frontend
```
Cualquier documento con formato roto o JSON de OpenAPI malformado romperá el gate de CI de forma preventiva.
