# AppName

Aplicación base generada por `dotnet new bitcode-app` (BitCode.Framework). Incluye el golden path
mínimo de un host: modularidad (`AddModules`/`UseModules`), persistencia multi-tenant, pipeline
CQRS + validación + transacciones, Minimal API versionada (`/api/v1/...`), OpenAPI, health checks
(`/health/live`, `/health/ready`) y observabilidad (OpenTelemetry).

El feature `Elementos/` es un ejemplo de referencia (mismo patrón que
`samples/Sample.Api/Productos`) -- reemplazalo por tu dominio real, o usalo como plantilla copiando
su estructura para el próximo feature (`dotnet new bitcode-feature`/`dotnet new bitcode-entity`
generan piezas sueltas equivalentes dentro de un proyecto ya existente).

## Antes de correr

1. Configurar `ConnectionStrings:Default` (appsettings.Development.json, variable de entorno o
   `dotnet user-secrets`) apuntando a una instancia de SQL Server.
2. `dotnet run` -- crea el esquema con `Database.EnsureCreatedAsync()` (reemplazar por
   `dotnet ef migrations` antes de operar en producción).

Ver `docs/convenciones.md` en la raíz del framework para las reglas duras que este proyecto ya sigue.
