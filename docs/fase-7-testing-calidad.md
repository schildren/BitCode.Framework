# Fase 7 — Testing e infraestructura de calidad

**Estado:** Completa — primer run de CI confirmado en verde ([run 33836338365](https://github.com/schildren/BitCode.Framework/actions/runs/33836338365))
**Commits:** `6db0ed4`, `04a0e44`, `3713085` (3 commits)

## Objetivo

Cerrar el hueco que la auditoría inicial de BC-SFE-MID marcó como **completamente ausente** — sin pipeline CI, bloqueante para cualquier framework productivo — y sanear deuda real acumulada en las fases anteriores (fixtures de Testcontainers duplicados, configuración de proyecto repetida en cada `.csproj`).

## Componentes por tarea

### Tarea 7.1 — Pipeline de GitHub Actions

`.github/workflows/ci.yml`, tres jobs en cada push/PR a `master`:

- **`build`**: `dotnet restore` + `dotnet build --configuration Release` — gate rápido de compilación.
- **`test-unit`**: tests sin el filtro `Integration` (rápidos, sin dependencias externas), resultados publicados como artefacto TRX.
- **`test-integration`**: tests con Testcontainers (SQL Server, Redis) — `ubuntu-latest` trae Docker preinstalado, sin configuración adicional necesaria.

`test-unit` y `test-integration` corren en paralelo (ambos dependen solo de `build`, no uno del otro), para no serializar innecesariamente los tests de integración detrás de los unitarios.

**Verificación:** el repo (`schildren/BitCode.Framework`) es privado y no había credenciales de GitHub (ni `gh` CLI ni token) disponibles en la sesión para consultar el estado del run vía API, así que se le pidió al usuario confirmarlo manualmente en la pestaña Actions. El [primer run](https://github.com/schildren/BitCode.Framework/actions/runs/33836338365) concluyó exitosamente (los tres jobs: `build`, `test-unit`, `test-integration`).

### Tarea 7.2 — `Shared.Testing`: fixtures de Testcontainers reutilizables

`SqlServerContainerFixture` y `RedisContainerFixture` estaban duplicados **byte a byte** en tres proyectos de test (`Persistence`, `Security`, `Caching`) creados en fases anteriores, junto con un `BuildIsolatedConnectionString` repetido dos veces. Se extrajeron a un proyecto nuevo, `Shared.Testing`, del que los tres ahora dependen.

De paso, se agregó **Bogus** (generación de datos sintéticos) — pieza que la auditoría inicial de BC-SFE-MID marcó explícitamente como ausente ("Falta Bogus — generación de datos de prueba parece manual/fixtures propias"). Queda disponible en `Shared.Testing` para el proyecto piloto de la Fase 8 y para cualquier test futuro que la necesite.

### Tarea 7.3 — `Directory.Build.props`

Los 23 `.csproj` del repo repetían manualmente `<TargetFramework>net8.0</TargetFramework>`, `<ImplicitUsings>enable</ImplicitUsings>` y `<Nullable>enable</Nullable>`. Un cambio de framework a futuro (p.ej. a `net9.0`) habría exigido editar los 23 archivos uno por uno, con riesgo real de que alguno quedara desincronizado — ya había ocurrido informalmente entre fases con pequeñas diferencias de formato. Un `Directory.Build.props` en la raíz las aplica automáticamente a todo el árbol; cada `.csproj` conserva solo lo que le es propio (`IsPackable`, `IsTestProject`, `PackageReference`, `ProjectReference`).

## Cómo verificar el estado de la Fase 7

```bash
# Ver el último run de CI (requiere gh CLI autenticado, no disponible en esta sesión)
gh run list --repo schildren/BitCode.Framework --limit 5
```

O visitar directamente: `https://github.com/schildren/BitCode.Framework/actions`

## Cobertura de tests

Sin tests nuevos propios de esta fase (es infraestructura de proceso, no funcionalidad) — se verificó que la suite completa existente (93 tests: 65 unitarios/con dependencias reales + 28 de integración contra SQL Server y Redis reales, sumando todas las fases) sigue en verde después de cada refactor (`Shared.Testing`, `Directory.Build.props`).

## Pendiente / fuera de alcance de esta fase

- Badge de estado de CI en el README del repo — trivial de agregar ahora que el workflow ya corrió exitosamente.
- Cobertura de código mínima exigida (gate de PR) — `coverlet.collector` ya está en todos los proyectos de test desde que se crearon, pero no hay un umbral mínimo forzado en CI; candidato para cuando el framework tenga más historial de uso real y se pueda fijar un umbral realista.
- `Directory.Packages.props` (gestión centralizada de versiones de paquetes NuGet) — se evaluó pero se descartó en esta fase por ser un cambio más amplio y riesgoso que el de `Directory.Build.props`; candidato para una pasada de mantenimiento posterior si la deriva de versiones entre proyectos se vuelve un problema real.
- Fase 8 del plan general (documentación y adopción: proyecto piloto end-to-end, guía de convenciones) — siguiente y última fase del plan original.
