using BitCode.Framework.Platform.Workflow;
using BitCode.Framework.Platform.Workflow.Definiciones;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Persistence;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Workflow.Api.Tests.Integration;

/// <summary>
/// Fase 9 (F9-03, "Data ownership" -- <c>docs/plan-maestro-bitcode-ia.md</c>, backlog de Fase 9): evidencia
/// REAL, contra SQL Server real (Testcontainers), de que <see cref="WorkflowDbContext"/> es un store
/// propio que se crea, se puebla y se consulta de punta a punta sin necesitar ningún otro módulo de
/// plataforma cableado en el mismo proceso -- ni <c>Sample.Workflow.Api</c> (el host de referencia
/// completo, con RBAC/auditoría/idempotencia/Quartz), ni ningún otro <c>ProjectReference</c> más allá de
/// <c>BitCode.Platform.Workflow</c> y la infraestructura compartida de persistencia (ver
/// <c>Sample.Workflow.Api.Tests.csproj</c>: este proyecto de test YA NO referencia ningún otro módulo de
/// plataforma, ni siquiera transitivamente, porque <c>Sample.Workflow.Api</c> tampoco lo hace).
/// </summary>
/// <remarks>
/// A diferencia de <see cref="WorkflowEndpointsIntegrationTests"/> (que verifica el camino feliz completo
/// vía HTTP contra el host de referencia), esta clase prueba la capa de persistencia sola, exactamente
/// como la usaría un consumidor real que llama <c>AddSharedPersistence&lt;WorkflowDbContext&gt;</c>
/// directamente en su propio <c>InfrastructureModule</c> (ver <c>docs/guia-workflow.md</c>, sección "Cómo
/// consumirlo desde un host") -- sin ningún <c>WebApplicationFactory</c>, sin JWT, sin RBAC.
/// <para>
/// Este framework no versiona migraciones de EF Core por módulo (ningún <c>Migrations/</c> commiteado
/// bajo <c>src/Platform/*</c> ni bajo ningún host <c>Sample.*.Api</c> productivo de referencia -- todos
/// usan <c>Database.EnsureCreatedAsync()</c>, ver comentario en <c>Sample.Workflow.Api/Program.cs</c>).
/// "Ejecutar las migraciones de Workflow de forma aislada" se traduce, en este framework, a construir el
/// modelo completo de <see cref="WorkflowDbContext"/> (que sí incluiría cualquier migración real que se
/// agregara en el futuro, porque <c>Database.EnsureCreatedAsync()</c>/<c>Database.MigrateAsync()</c>
/// aplican el mismo <c>IModel</c>) contra una base de datos vacía, sin que ningún otro <c>DbContext</c> de
/// plataforma participe del proceso.
/// </para>
/// </remarks>
public class WorkflowDataOwnershipIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();

    public Task InitializeAsync() => _sqlServerFixture.InitializeAsync();

    public Task DisposeAsync() => _sqlServerFixture.DisposeAsync();

    private async Task<ServiceProvider> BuildIsolatedWorkflowProviderAsync(string testName)
    {
        // Prefijo corto deliberado: SQL Server limita "Initial Catalog" a 128 caracteres, y los nombres
        // descriptivos de estos Fact (para que quede claro qué verifica cada uno) ya son largos por sí
        // solos -- BuildIsolatedConnectionString concatena prefijo + testName + Guid completo (32
        // caracteres), así que un prefijo largo puede superar el límite (visto en la práctica al ejecutar
        // este test).
        var connectionString = _sqlServerFixture.BuildIsolatedConnectionString("WfOwn", testName);

        // Deliberadamente el ÚNICO AddSharedPersistence<T> de todo este proceso de test: ningún otro
        // DbContext de plataforma se registra acá, a diferencia de un host que combinara Workflow con
        // otro módulo (patrón explícitamente prohibido, ver comentario de
        // Sample.TaskInbox.Api/InfrastructureModule.cs sobre por qué nunca se registran dos
        // AddSharedPersistence<T> de bounded contexts distintos en el mismo contenedor de DI).
        var services = new ServiceCollection();
        services.AddSharedPersistence<WorkflowDbContext>(connectionString);
        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WorkflowDbContext>().Database.EnsureCreatedAsync();

        return provider;
    }

    /// <summary>
    /// Tablas que <see cref="WorkflowDbContext"/> debe poseer -- y SOLO estas -- en una base de datos
    /// creada exclusivamente a partir de su propio modelo: las siete tablas de negocio documentadas en el
    /// XML doc de <see cref="WorkflowDbContext"/> más las tres tablas de infraestructura compartida que
    /// <c>MultiTenantDbContext</c> registra automáticamente (Idempotencia/Outbox/Inbox, F1-22/F1-23/F1-24).
    /// Si algún cambio futuro agrega, por accidente, una entidad de otro módulo al modelo de
    /// <see cref="WorkflowDbContext"/> (por ejemplo, una navegación o un <c>DbSet</c> agregado sin darse
    /// cuenta), esta lista deja de coincidir con las tablas reales y el test siguiente falla -- evidencia
    /// estructural de "store propio", no solo una afirmación en la documentación.
    /// </summary>
    private static readonly HashSet<string> TablasEsperadasDeWorkflow = new(StringComparer.OrdinalIgnoreCase)
    {
        "WorkflowDefiniciones",
        "WorkflowVersiones",
        "WorkflowStates",
        "WorkflowTransitions",
        "WorkflowInstances",
        "WorkflowTasks",
        "WorkflowHistoriales",
        "IdempotencyKey",
        "OutboxMessage",
        "InboxMessage",
    };

    [Fact]
    public async Task WorkflowDbContext_CreadoEnAislamiento_ExponeExactamenteSusPropiasTablas()
    {
        await using var provider = await BuildIsolatedWorkflowProviderAsync("Tablas");
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";
            await using var reader = await command.ExecuteReaderAsync();

            var tablasReales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync())
            {
                tablasReales.Add(reader.GetString(0));
            }

            // Ninguna tabla de otro módulo de plataforma (Organization/Catalogs/Documents/etc.) puede
            // aparecer acá: esta base de datos se creó EXCLUSIVAMENTE a partir del modelo de
            // WorkflowDbContext, ningún otro DbContext participó del proceso.
            tablasReales.Should().BeEquivalentTo(TablasEsperadasDeWorkflow,
                "el store de Workflow (F9-03, 'Data ownership') debe exponer exactamente sus propias " +
                "tablas -- ni una tabla de menos (esquema roto) ni una de más (fuga de ownership hacia " +
                "otro módulo).");
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    /// <summary>
    /// CRUD de punta a punta sobre las siete entidades de negocio del módulo, atravesando
    /// <c>IRepository&lt;,&gt;</c>/<c>IUnitOfWork</c> (regla dura 1, <c>docs/convenciones.md</c>) igual
    /// que cualquier handler de comando real -- confirma que el store no solo "existe" (test anterior)
    /// sino que puede poblarse y volver a leerse íntegro, sin ningún otro módulo de plataforma presente en
    /// el proceso.
    /// </summary>
    [Fact]
    public async Task WorkflowDbContext_CreadoEnAislamiento_PermiteCrearYConsultarElGrafoCompleto()
    {
        await using var provider = await BuildIsolatedWorkflowProviderAsync("Crud");
        var actorUserId = Guid.NewGuid();

        Guid definicionId, versionId, estadoInicialId, estadoFinalId, transicionId, instanciaId, tareaId, historialId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var definiciones = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowDefinition, Guid>>();
            var versiones = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowVersion, Guid>>();
            var estados = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowState, Guid>>();
            var transiciones = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowTransition, Guid>>();
            var instancias = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowInstance, Guid>>();
            var tareas = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowTask, Guid>>();
            var historiales = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowHistorial, Guid>>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var definicion = new WorkflowDefinition(Guid.NewGuid(), "OWNERSHIP_TEST", "Prueba de ownership", null);
            var version = new WorkflowVersion(Guid.NewGuid(), definicion.Id, 1);
            var estadoInicial = new WorkflowState(
                Guid.NewGuid(), version.Id, "INICIAL", "Inicial", esInicial: true, esFinal: false,
                requiereTarea: true, tituloTarea: "Revisar", asignadoPorDefectoUserId: actorUserId,
                slaMinutos: null, escalarAUserId: null);
            var estadoFinal = new WorkflowState(
                Guid.NewGuid(), version.Id, "FINAL", "Final", esInicial: false, esFinal: true,
                requiereTarea: false, tituloTarea: null, asignadoPorDefectoUserId: null, slaMinutos: null,
                escalarAUserId: null);
            var transicion = new WorkflowTransition(
                Guid.NewGuid(), version.Id, estadoInicial.Id, estadoFinal.Id, "Aprobar", null, orden: 1);
            var instancia = new WorkflowInstance(
                Guid.NewGuid(), definicion.Id, version.Id, estadoInicial.Id, actorUserId,
                new Dictionary<string, string>());
            var tarea = new WorkflowTask(Guid.NewGuid(), instancia.Id, estadoInicial.Id, "Revisar", actorUserId, null);
            var historial = new WorkflowHistorial(
                Guid.NewGuid(), instancia.Id, "InstanciaIniciada", "Se inició la instancia de prueba", actorUserId);

            await definiciones.AddAsync(definicion);
            await versiones.AddAsync(version);
            await estados.AddAsync(estadoInicial);
            await estados.AddAsync(estadoFinal);
            await transiciones.AddAsync(transicion);
            await instancias.AddAsync(instancia);
            await tareas.AddAsync(tarea);
            await historiales.AddAsync(historial);
            await unitOfWork.SaveChangesAsync();

            definicionId = definicion.Id;
            versionId = version.Id;
            estadoInicialId = estadoInicial.Id;
            estadoFinalId = estadoFinal.Id;
            transicionId = transicion.Id;
            instanciaId = instancia.Id;
            tareaId = tarea.Id;
            historialId = historial.Id;
        }

        // Segundo scope (nueva instancia de WorkflowDbContext) para forzar una lectura real desde SQL
        // Server, no del change tracker de la instancia que escribió.
        await using (var scope = provider.CreateAsyncScope())
        {
            var definiciones = scope.ServiceProvider.GetRequiredService<IReadRepository<WorkflowDefinition, Guid>>();
            var versiones = scope.ServiceProvider.GetRequiredService<IReadRepository<WorkflowVersion, Guid>>();
            var estados = scope.ServiceProvider.GetRequiredService<IReadRepository<WorkflowState, Guid>>();
            var transiciones = scope.ServiceProvider.GetRequiredService<IReadRepository<WorkflowTransition, Guid>>();
            var instancias = scope.ServiceProvider.GetRequiredService<IReadRepository<WorkflowInstance, Guid>>();
            var tareas = scope.ServiceProvider.GetRequiredService<IReadRepository<WorkflowTask, Guid>>();
            var historiales = scope.ServiceProvider.GetRequiredService<IReadRepository<WorkflowHistorial, Guid>>();

            (await definiciones.GetByIdAsync(definicionId)).Should().NotBeNull();
            (await versiones.GetByIdAsync(versionId)).Should().NotBeNull();
            (await estados.GetByIdAsync(estadoInicialId)).Should().NotBeNull();
            (await estados.GetByIdAsync(estadoFinalId)).Should().NotBeNull();
            (await transiciones.GetByIdAsync(transicionId)).Should().NotBeNull();

            var instanciaLeida = await instancias.GetByIdAsync(instanciaId);
            instanciaLeida.Should().NotBeNull();
            instanciaLeida!.WorkflowDefinitionId.Should().Be(definicionId);
            instanciaLeida.WorkflowVersionId.Should().Be(versionId);

            var tareaLeida = await tareas.GetByIdAsync(tareaId);
            tareaLeida.Should().NotBeNull();
            tareaLeida!.WorkflowInstanceId.Should().Be(instanciaId);

            var historialLeido = await historiales.GetByIdAsync(historialId);
            historialLeido.Should().NotBeNull();
            historialLeido!.WorkflowInstanceId.Should().Be(instanciaId);
        }
    }
}
