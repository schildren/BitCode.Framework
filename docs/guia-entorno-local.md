# Guía de Entorno Local de Desarrollo (F8-11)

## 1. Propósito y Filosofía ("Day 1 Developer Experience")

La meta de la tarea **F8-11** es garantizar que cualquier persona nueva que ingrese al equipo pueda **clonar el repositorio, ejecutar un comando y tener todo el entorno de infraestructura listo para desarrollar en menos de 5 minutos** (Gate de salida de Fase 8: *"Una persona nueva puede levantar el entorno siguiendo la guía"*).

El entorno local automatiza mediante contenedores:
1. **Motor de Base de Datos:** SQL Server 2022 Developer Edition.
2. **Caché Distribuido y Rate Limiting:** Redis 7.2.
3. **Plataforma de Mensajería y Eventos:** Apache Kafka en modo KRaft (sin ZooKeeper).
4. **Telemetría y Observabilidad:** OpenTelemetry Collector con exportador a Jaeger UI.

---

## 2. Pre-requisitos del Sistema

- **Docker Desktop** (Windows / macOS) con WSL2 backend habilitado, o **Docker Engine + Docker Compose v2** (Linux).
- **.NET SDK 10.0+**
- **Node.js 22+** (para desarrollo frontend/Nx)

---

## 3. Inicio Rápido (Quickstart)

Desde la raíz del repositorio:

### En Windows (PowerShell)
```powershell
# Iniciar todos los contenedores en segundo plano
./scripts/dev-env.ps1 up
```

### En Linux / macOS (Bash)
```bash
chmod +x ./scripts/dev-env.sh
./scripts/dev-env.sh up
```

### O directamente con Docker Compose
```bash
docker compose up -d
```

---

## 4. Endpoints y Credenciales Locales

Una vez iniciados los servicios, quedan expuestos en los puertos estándar:

| Servicio | Endpoint / Puerto | Credenciales por Defecto | Propósito en el Framework |
|---|---|---|---|
| **SQL Server** | `localhost:1433` | Usuario: `sa`<br>Password: `Password123!` | Persistencia, migraciones EF Core, Outbox/Inbox y Multi-Tenancy. |
| **Redis** | `localhost:6379` | Sin contraseña | HybridCache L2 y rate limiting distribuido del Gateway. |
| **Kafka** | `localhost:9092` | PLAINTEXT (sin autenticación) | Broker de eventos de integración (`Shared.Infrastructure.Messaging.Kafka`). |
| **OTel Collector (gRPC)** | `localhost:4317` | N/A | Recepción de trazas, métricas y logs OTLP desde las aplicaciones. |
| **OTel Collector (HTTP)** | `localhost:4318` | N/A | Protocolo HTTP/protobuf para clientes OTLP web. |
| **Jaeger UI** | [http://localhost:16686](http://localhost:16686) | N/A | Interfaz visual interactiva para inspeccionar trazas distribuidas y spans. |
| **Collector Health** | [http://localhost:13133](http://localhost:13133) | N/A | Endpoint de salud y diagnóstico del Collector. |

---

## 5. Configuración de Proyectos Consumidores (`appsettings.Development.json`)

Para que un host local (ej. `samples/Sample.Api` o un servicio nuevo creado con `dotnet new bitcode-app`) apunte a este entorno, configure sus cadenas de conexión locales:

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost,1433;Database=MiAppDb;User Id=sa;Password=Password123!;TrustServerCertificate=True;MultipleActiveResultSets=True;"
  },
  "Caching": {
    "RedisConnectionString": "localhost:6379"
  },
  "Kafka": {
    "BootstrapServers": "localhost:9092"
  },
  "OpenTelemetry": {
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

---

## 6. Comandos de Gestión y Mantenimiento

Los scripts `scripts/dev-env.ps1` y `scripts/dev-env.sh` admiten las siguientes acciones:

- **Ver estado:**
  ```bash
  ./scripts/dev-env.ps1 status
  ```
- **Seguir logs en tiempo real:**
  ```bash
  ./scripts/dev-env.ps1 logs
  ```
- **Detener contenedores:**
  ```bash
  ./scripts/dev-env.ps1 down
  ```
- **Limpieza completa y reset de datos (elimina volúmenes persistentes):**
  ```bash
  ./scripts/dev-env.ps1 clean
  ```

---

## 7. Verificación del Entorno

Para verificar que el entorno responde correctamente:
1. Conectar con Azure Data Studio / SSMS a `localhost,1433` con usuario `sa` y password `Password123!`.
2. Abrir en el navegador [http://localhost:16686](http://localhost:16686) para verificar que la interfaz de Jaeger carga con éxito.
3. Ejecutar las pruebas de arquitectura del entorno local:
   ```bash
   dotnet test tests/BitCode.Architecture.Tests/BitCode.Architecture.Tests.csproj --filter "FullyQualifiedName~DevEnvironment"
   ```
