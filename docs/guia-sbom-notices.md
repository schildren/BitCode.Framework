# Guía — Software Bill of Materials (SBOM) y Licencias de Terceros (`F8-14`)

Gobernanza de supply chain, trazabilidad de componentes y cumplimiento legal (**Fase 8, F8-14**). Define la generación automatizada de SBOM en formato estándar internacional CycloneDX 1.5, el reporte de licencias y el documento legal de atribución [`THIRD-PARTY-NOTICES.md`](file:///c:/03_Laboral/Repositorio/BitCode.Framework/THIRD-PARTY-NOTICES.md), cerrando el Gate de salida de la Fase 8.

---

## 1. Contexto y Cumplimiento Normativo

En el desarrollo de software empresarial contemporáneo, la transparencia de la cadena de suministro es un requisito indispensable (alineado con marcos como NIST SSDF, Executive Order 14028 y directivas ISO/IEC 5230 OpenChain). 

El Plan Maestro establece como criterio de cierre del Gate de Fase 8:
> **"El pipeline produce SBOM, license report y vulnerabilidades con cobertura del 100 %."**

Para garantizar este requerimiento, el repositorio incorpora:

1. **Software Bill of Materials (SBOM)**:
   - Formato estándar de la industria: **CycloneDX v1.5 JSON** (`artifacts/sbom/sbom-cyclonedx.json`).
   - Cobertura completa tanto del ecosistema backend (.NET) como del monorepo frontend (Angular/npm).
   - Especifica identificadores universales Package URL (`purl`, e.g. `pkg:nuget/MediatR@12.4.1`, `pkg:npm/%40angular/core@22.1.4`).

2. **Auditoría Continua de Licencias**:
   - Generación de `artifacts/sbom/license-report.json` con el desglose y distribución de licencias.
   - Verificación estricta contra la política de dependencias ([`docs/politica-dependencias.md`](file:///c:/03_Laboral/Repositorio/BitCode.Framework/docs/politica-dependencias.md)):
     - **Permitidas sin excepción**: MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, 0BSD.
     - **Prohibidas categóricamente**: GPL (cualquier versión), AGPL, SSPL o licencias con copyleft recíproco agresivo.

3. **Atribución Legal en Repositorio**:
   - Mantenimiento automatizado de [`THIRD-PARTY-NOTICES.md`](file:///c:/03_Laboral/Repositorio/BitCode.Framework/THIRD-PARTY-NOTICES.md) en la raíz del repositorio, conteniendo la tabla de atribuciones y los textos completos de licencias exigidos por los autores originales para redistribución empresarial.

---

## 2. Generación y Auditoría Local

Para inspeccionar o actualizar los artefactos de compliance localmente:

```bash
# Generar SBOM, License Report y THIRD-PARTY-NOTICES.md
node scripts/generate-sbom-notices.mjs

# Ejecutar únicamente como verificación de cumplimiento (Gate de CI)
node scripts/generate-sbom-notices.mjs --verify
```

### Artefactos Producidos

| Archivo | Formato | Propósito |
|---|---|---|
| `artifacts/sbom/sbom-cyclonedx.json` | CycloneDX 1.5 JSON | Inventario estructurado de componentes para escaneo de seguridad en herramientas como Dependency-Track, Snyk o Trivy. |
| `artifacts/sbom/license-report.json` | JSON Estructurado | Métricas de distribución de licencias y estado de aprobación legal. |
| `THIRD-PARTY-NOTICES.md` | Markdown Legal | Documento de atribución para cumplimiento de licencias permissivas (MIT/Apache) al redistribuir binarios. |

---

## 3. Integración en el Ciclo de Vida de CI/CD

### 3.1. En Cada Pull Request y Commit (`ci.yml`)
En el workflow `.github/workflows/ci.yml`, el job `dependency-scan`:
1. Audita vulnerabilidades conocidas en NuGet con `dotnet list package --vulnerable --include-transitive`.
2. Ejecuta `node scripts/generate-sbom-notices.mjs` para verificar la conformidad de licencias y generar el SBOM.
3. Sube los artefactos generados como `sbom-and-license-reports`.

### 3.2. En Cada Release Oficial (`release.yml`)
En el pipeline de publicación `.github/workflows/release.yml`:
1. Se genera la versión definitiva de `sbom-cyclonedx.json` y `license-report.json` vinculada al tag `v*`.
2. Se adjuntan el SBOM y el reporte de licencias como assets oficiales del GitHub Release, junto a los paquetes `.nupkg` y `.tgz`.

---

## 4. Estructura de un Componente en CycloneDX 1.5

Ejemplo de definición de dependencia en el SBOM generado:

```json
{
  "type": "library",
  "name": "Confluent.Kafka",
  "version": "2.15.0",
  "purl": "pkg:nuget/Confluent.Kafka@2.15.0",
  "author": "Confluent Inc.",
  "licenses": [
    {
      "license": {
        "id": "Apache-2.0",
        "url": "https://github.com/confluentinc/confluent-kafka-dotnet"
      }
    }
  ],
  "externalReferences": [
    {
      "type": "vcs",
      "url": "https://github.com/confluentinc/confluent-kafka-dotnet"
    }
  ]
}
```

---

## 5. Auditoría de Consumidores

Cualquier organización que adopte BitCode.Framework puede incorporar directamente `artifacts/sbom/sbom-cyclonedx.json` en sus plataformas de gestión de riesgos de terceros o auditorías de seguridad corporativa sin necesidad de ingeniería inversa sobre los ensamblados compilados.
