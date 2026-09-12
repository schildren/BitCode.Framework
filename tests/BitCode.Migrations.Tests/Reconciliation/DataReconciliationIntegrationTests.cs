using BitCode.Framework.Platform.Workflow;
using BitCode.Framework.Platform.Workflow.Definiciones;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Persistence;
using BitCode.Framework.Shared.Testing;
using BitCode.Framework.Tools.Migrations.Core.DataReconciliation;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BitCode.Framework.Tests.Migrations.Reconciliation;

/// <summary>
/// Fase 9 (F9-08, "Data migration" — <c>docs/plan-maestro-bitcode-ia.md</c>, backlog de Fase 9,
/// entregable "Herramienta", criterio de aceptación "Conteos y hashes conciliados"): evidencia REAL,
/// contra SQL Server real (Testcontainers), de que <see cref="DataReconciler"/> concilia correctamente
/// un origen y un destino de <see cref="WorkflowDbContext"/> -- y, más importante, DETECTA una
/// discrepancia de contenido introducida deliberadamente aunque los conteos de filas coincidan.
/// </summary>
/// <remarks>
/// Escenario simulado de extracción de microservicio: mover el store físico de Workflow de un servidor
/// SQL Server a otro. Origen y destino son dos bases de datos separadas dentro del mismo contenedor de
/// Testcontainers (opción explícitamente permitida por la tarea F9-08 cuando simplifica el test sin dejar
/// de ser una verificación real de datos reales). La copia física de datos usa <see cref="SqlBulkCopy"/>
/// tabla por tabla, en orden de dependencia de claves foráneas -- el mecanismo más simple disponible en
/// este framework, dado que no existe un tooling de backup/restore físico versionado en el repositorio
/// (ver <c>tools/SqlBackupAutomation</c>, orientado a backups completos de un servidor productivo, no a
/// mover un subconjunto de tablas entre bases).
/// </remarks>
public sealed class DataReconciliationIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();

    /// <summary>
    /// Orden de copia que respeta las claves foráneas del modelo de <see cref="WorkflowDbContext"/>
    /// (ver <c>WorkflowDbContext.OnModelCreating</c>): definiciones antes que versiones, versiones antes
    /// que estados/transiciones/instancias, instancias antes que tareas/historiales.
    /// </summary>
    private static readonly string[] TablasDeNegocioEnOrdenDeCopia =
    [
        "WorkflowDefiniciones",
        "WorkflowVersiones",
        "WorkflowStates",
        "WorkflowTransitions",
        "WorkflowInstances",
        "WorkflowTasks",
        "WorkflowHistoriales",
    ];

    public Task InitializeAsync() => _sqlServerFixture.InitializeAsync();

    public Task DisposeAsync() => _sqlServerFixture.DisposeAsync();

    private async Task<string> CreateAndPopulateSourceDatabaseAsync(string testName)
    {
        var connectionString = _sqlServerFixture.BuildIsolatedConnectionString("ReconcSrc", testName);

        var services = new ServiceCollection();
        services.AddSharedPersistence<WorkflowDbContext>(connectionString);
        await using var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        await context.Database.EnsureCreatedAsync();

        var actorUserId = Guid.NewGuid();
        var definiciones = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowDefinition, Guid>>();
        var versiones = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowVersion, Guid>>();
        var estados = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowState, Guid>>();
        var transiciones = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowTransition, Guid>>();
        var instancias = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowInstance, Guid>>();
        var tareas = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowTask, Guid>>();
        var historiales = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowHistorial, Guid>>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var definicion = new WorkflowDefinition(Guid.NewGuid(), "RECONCILIACION_TEST", "Prueba de reconciliación F9-08", null);
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

        return connectionString;
    }

    private async Task<string> CreateEmptyTargetDatabaseAsync(string testName)
    {
        var connectionString = _sqlServerFixture.BuildIsolatedConnectionString("ReconcDst", testName);

        var services = new ServiceCollection();
        services.AddSharedPersistence<WorkflowDbContext>(connectionString);
        await using var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WorkflowDbContext>().Database.EnsureCreatedAsync();

        return connectionString;
    }

    /// <summary>
    /// Copia tabla por tabla, en orden de dependencia de FK, usando <see cref="SqlBulkCopy"/> con mapeo
    /// explícito de columnas por nombre (no por posición ordinal) -- el mecanismo de copia más simple
    /// disponible sin introducir tooling nuevo de backup/restore físico.
    /// </summary>
    private static async Task CopyBusinessDataAsync(string sourceConnectionString, string targetConnectionString)
    {
        foreach (var tableName in TablasDeNegocioEnOrdenDeCopia)
        {
            await using var sourceConnection = new SqlConnection(sourceConnectionString);
            await sourceConnection.OpenAsync();
            await using var selectCommand = new SqlCommand($"SELECT * FROM [{tableName}]", sourceConnection);
            await using var reader = await selectCommand.ExecuteReaderAsync();

            await using var targetConnection = new SqlConnection(targetConnectionString);
            await targetConnection.OpenAsync();
            using var bulkCopy = new SqlBulkCopy(targetConnection)
            {
                DestinationTableName = $"[{tableName}]",
            };

            for (var i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                bulkCopy.ColumnMappings.Add(columnName, columnName);
            }

            await bulkCopy.WriteToServerAsync(reader);
        }
    }

    private static async Task<DataReconciler> CreateReconcilerAsync(string connectionStringForModel)
    {
        var services = new ServiceCollection();
        services.AddSharedPersistence<WorkflowDbContext>(connectionStringForModel);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        // El IModel es independiente de la conexión concreta: se puede reutilizar tras cerrar el scope.
        return new DataReconciler(context.Model);
    }

    [Fact]
    public async Task Reconcile_OrigenYDestinoIdenticos_ReportaConciliadoConConteosYHashesIguales()
    {
        var sourceConnectionString = await CreateAndPopulateSourceDatabaseAsync("Idéntico_Origen");
        var targetConnectionString = await CreateEmptyTargetDatabaseAsync("Idéntico_Destino");

        await CopyBusinessDataAsync(sourceConnectionString, targetConnectionString);

        var reconciler = await CreateReconcilerAsync(sourceConnectionString);

        // Se limita explícitamente la reconciliación a las siete tablas de negocio de Workflow: las
        // tablas de infraestructura compartida (IdempotencyKey/OutboxMessage/InboxMessage) no forman
        // parte de este escenario -- OutboxMessage en particular queda con eventos de dominio pendientes
        // de relay en el origen (comportamiento normal de la Fase 1, F1-23) que este test deliberadamente
        // no copia, porque copiar el Outbox pertenece a la estrategia de relay/replicación de eventos, no
        // a la migración de datos de negocio que valida F9-08.
        var report = await reconciler.ReconcileAsync(sourceConnectionString, targetConnectionString, TablasDeNegocioEnOrdenDeCopia);

        report.IsFullyReconciled.Should().BeTrue(
            "el destino es una copia exacta del origen -- conteos y hashes deben coincidir en todas las tablas (criterio de aceptación de F9-08)");

        foreach (var tableName in TablasDeNegocioEnOrdenDeCopia)
        {
            var tableResult = report.Tables.Should().ContainSingle(t => t.TableName == tableName).Subject;
            tableResult.Error.Should().BeNull();
            tableResult.SourceCount.Should().Be(tableResult.TargetCount);
            tableResult.SourceCount.Should().BeGreaterThan(0, $"la tabla {tableName} debe haberse poblado con datos de prueba");
            tableResult.SourceAggregateHash.Should().Be(tableResult.TargetAggregateHash);
            tableResult.IsReconciled.Should().BeTrue();
            tableResult.FirstMismatch.Should().BeNull();
        }
    }

    [Fact]
    public async Task Reconcile_DiscrepanciaDeContenidoConMismoConteo_EsDetectadaEnLaTablaExacta()
    {
        var sourceConnectionString = await CreateAndPopulateSourceDatabaseAsync("Discrepancia_Origen");
        var targetConnectionString = await CreateEmptyTargetDatabaseAsync("Discrepancia_Destino");

        await CopyBusinessDataAsync(sourceConnectionString, targetConnectionString);

        // Se introduce deliberadamente una discrepancia de CONTENIDO en el destino sin alterar el
        // conteo de filas: esto es precisamente lo que un chequeo basado solo en COUNT(*) no detectaría,
        // y es la razón de ser del hash de contenido por fila (ver TableReconciliationResult).
        await using (var targetConnection = new SqlConnection(targetConnectionString))
        {
            await targetConnection.OpenAsync();
            await using var updateCommand = new SqlCommand(
                "UPDATE WorkflowHistoriales SET Detalle = 'Detalle alterado deliberadamente en destino'",
                targetConnection);
            var rowsAffected = await updateCommand.ExecuteNonQueryAsync();
            rowsAffected.Should().Be(1, "la fixture de prueba inserta exactamente un WorkflowHistorial");
        }

        var reconciler = await CreateReconcilerAsync(sourceConnectionString);

        // Mismo alcance de tablas que el escenario "idéntico" (ver comentario allí): se excluyen las
        // tablas de infraestructura compartida, que no participan de este escenario de negocio.
        var report = await reconciler.ReconcileAsync(sourceConnectionString, targetConnectionString, TablasDeNegocioEnOrdenDeCopia);

        report.IsFullyReconciled.Should().BeFalse(
            "se alteró el contenido de una fila en destino -- la reconciliación debe fallar globalmente");

        var historialesResult = report.Tables.Should().ContainSingle(t => t.TableName == "WorkflowHistoriales").Subject;
        historialesResult.CountsMatch.Should().BeTrue(
            "el conteo de filas sigue siendo idéntico -- la discrepancia es de CONTENIDO, no de cantidad");
        historialesResult.HashesMatch.Should().BeFalse();
        historialesResult.IsReconciled.Should().BeFalse();
        historialesResult.FirstMismatch.Should().NotBeNull();
        historialesResult.SourceAggregateHash.Should().NotBe(historialesResult.TargetAggregateHash);

        // Las demás tablas de negocio, no tocadas, deben seguir reportando conciliación exitosa --
        // evidencia de que la herramienta localiza la discrepancia en la tabla correcta y no produce
        // falsos positivos en el resto del reporte.
        foreach (var tableName in TablasDeNegocioEnOrdenDeCopia.Where(t => t != "WorkflowHistoriales"))
        {
            var tableResult = report.Tables.Should().ContainSingle(t => t.TableName == tableName).Subject;
            tableResult.IsReconciled.Should().BeTrue($"la tabla {tableName} no fue alterada y debe seguir conciliada");
        }
    }
}
