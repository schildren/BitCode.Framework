# Guía — CLI de Diagnóstico Accionable (`BitCode.Diagnostics`)

Herramienta de diagnóstico de entorno y pre-flight check (**Fase 8, F8-12**) diseñada para verificar de manera automatizada y accionable la salud de la estación de trabajo antes de compilar o desplegar aplicaciones basadas en BitCode.Framework.

---

## 1. Propósito y Alcance

Garantizar la experiencia de onboarding reproducible ("De 0 a ejecutando en 5 minutos") y agilizar el soporte técnico ante inconsistencias locales. El CLI verifica tres dimensiones fundamentales:

1. **Herramientas y SDKs (`tools`)**: Presencia y compatibilidad de .NET 10 SDK, Node.js (v18+/v20+ LTS), Git, Docker CLI, Docker Compose v2 y estado del Docker Daemon.
2. **Configuración y Manifiestos (`config`)**: Presencia de manifiestos orquestadores (`docker-compose.yml`, `scripts/dev-env.ps1`), variables de entorno estándar (`ConnectionStrings__DefaultConnection`, `Redis__Configuration`, `Kafka__BootstrapServers`, `OpenTelemetry__Endpoint`) y validación de sintaxis de archivos `appsettings.Development.json`.
3. **Conectividad de Infraestructura (`connectivity`)**: Verificación en paralelo de disponibilidad y latencia (TCP/HTTP) hacia SQL Server (1433), Redis (6379), Apache Kafka KRaft (9092), OpenTelemetry Collector (13133) y Jaeger UI (16686).

---

## 2. Formas de Ejecución

### 2.1. Mediante Scripts de Conveniencia (Recomendado)

En Windows (PowerShell):
```powershell
# Diagnóstico completo
.\scripts\doctor.ps1

# Solo herramientas y SDKs
.\scripts\doctor.ps1 tools

# Solo conectividad con dependencias
.\scripts\doctor.ps1 connectivity

# Solo configuración y variables
.\scripts\doctor.ps1 config

# Salida estructurada JSON (para CI o scripts)
.\scripts\doctor.ps1 --format json

# Modo estricto (falla si hay advertencias)
.\scripts\doctor.ps1 --strict
```

En Linux / macOS (Bash):
```bash
chmod +x ./scripts/doctor.sh
./scripts/doctor.sh
./scripts/doctor.sh tools
./scripts/doctor.sh --format json
```

### 2.2. Mediante .NET CLI Directo

```bash
dotnet run --project tools/BitCode.Diagnostics -- [comando] [opciones]
```

---

## 3. Comandos y Modos

| Comando | Alias | Descripción |
|---|---|---|
| `doctor` | `check`, `all` | Ejecuta las tres suites de verificación de forma exhaustiva (por defecto). |
| `tools` | — | Valida herramientas instaladas en el PATH y estado del demonio Docker. |
| `config` | — | Valida archivos de manifiesto del repositorio y variables de entorno requeridas. |
| `connectivity` | — | Sondea disponibilidad y mide latencia de red contra los contenedores locales. |

### Opciones y Flags

| Flag | Argumento | Descripción |
|---|---|---|
| `--format` | `json` | Emite el reporte consolidado en JSON formateado sin encabezados ANSI. |
| `--strict` | — | Devuelve código de salida `2` si se detectan advertencias (`[WARN]`). |
| `--timeout` | `<segundos>` | Define el tiempo límite por sondeo de red (por defecto: `2` segundos). |
| `-h`, `--help` | — | Despliega la ayuda y sintaxis de comandos. |

---

## 4. Códigos de Salida del Proceso

El CLI utiliza códigos de retorno estandarizados para integración fluida con pipelines CI/CD:

| Código | Significado | Criterio |
|:---:|---|---|
| **0** | **Éxito (Healthy)** | Todas las verificaciones requeridas pasaron con éxito (o solo hay advertencias en modo estándar). |
| **1** | **Fallo Crítico (Error)** | Uno o más chequeos esenciales fallaron (e.g. .NET SDK ausente, archivo requerido inexistente). |
| **2** | **Advertencia (Strict)** | Se ejecutó con `--strict` y se detectó al menos una advertencia no bloqueante. |

---

## 5. Salida en Consola y Diagnóstico Accionable

El CLI formatea los resultados con símbolos de severidad y genera un bloque final de **remediaciones sugeridas** con los comandos exactos a ejecutar:

```text
========================================================================
      BitCode Diagnostics Doctor — Verificación de Entorno (F8-12)      
========================================================================
Fecha de ejecución: 2026-09-11 01:23:06 UTC
Duración: 1240 ms

--- 🛠️  Herramientas y SDKs de Desarrollo ---
  [OK]   .NET SDK                  .NET SDK instalado y compatible (10.0.302).
  [OK]   Node.js                   Node.js instalado y compatible (v25.9.0).
  [OK]   Git                       Git disponible (git version 2.55.0).
  [OK]   Docker CLI                Docker CLI disponible (Docker version 29.4.1).
  [OK]   Docker Compose            Docker Compose v2 disponible (Docker Compose version v5.1.3).
  [OK]   Docker Daemon             Demonio de Docker en ejecución y respondiendo.

--- ⚙️  Configuración y Manifiestos ---
  [OK]   docker-compose.yml        Archivo orquestador de dependencias locales (F8-11) encontrado.
  [OK]   scripts/dev-env.ps1       Script de administración PowerShell del entorno local encontrado.
  [WARN] ConnectionStrings__DefaultConnection Variable no configurada. Fallback: localhost,1433

--- 🌐 Conectividad de Infraestructura y Servicios ---
  [WARN] SQL Server 2022           No se pudo conectar a SQL Server 2022 en 127.0.0.1:1433.
  [WARN] Redis 7.2                 No se pudo conectar a Redis 7.2 en 127.0.0.1:6379.
  [WARN] Apache Kafka (KRaft)      No se pudo conectar a Apache Kafka (KRaft) en 127.0.0.1:9092.

------------------------------------------------------------------------
Resumen de comprobaciones: 10 superadas, 4 advertencias, 0 errores (Total: 14)

💡 Acciones de Remediación Sugeridas:
  1. [SQL Server 2022]: Inicie el contenedor con: .\scripts\dev-env.ps1 up (o docker compose up -d sqlserver).
  2. [Redis 7.2]: Inicie el contenedor con: .\scripts\dev-env.ps1 up (o docker compose up -d redis).
  3. [Apache Kafka (KRaft)]: Inicie el contenedor con: .\scripts\dev-env.ps1 up (o docker compose up -d kafka).
```

---

## 6. Integración en CI/CD

Para validar que un agente de compilación o runner cumple con los requisitos del repositorio, invoque:

```bash
dotnet run --project tools/BitCode.Diagnostics -- tools --strict
```
Si alguna herramienta (.NET SDK, Git, Docker) no cumple los requisitos, el proceso devolverá código `1` o `2` abortando el paso tempranamente con un mensaje de diagnóstico claro.
