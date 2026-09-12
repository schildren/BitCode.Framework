using BitCode.Framework.Platform.Workflow;
using BitCode.Framework.Platform.Workflow.Definiciones;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Domain.Outbox;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Persistence;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Workflow.Api.Tests.Integration;

/// <summary>
/// Fase 9 (F9-11, "Reversión" -- <c>docs/plan-maestro-bitcode-ia.md</c>, backlog de Fase 9): evidencia
/// REAL, contra SQL Server real (Testcontainers), de que la extracción de Workflow como microservicio
/// piloto (F9-02 "Contract boundary" + F9-05 "Host independiente") fue un cambio ADITIVO, no una
/// reescritura que rompió el camino de volver a consumir el módulo tal como se consumía ANTES de esas
/// dos tareas -- es decir, referenciado directamente como librería embebida por un host, sin Gateway, sin
/// proceso separado y SIN Kafka/Outbox publisher cableado.
/// </summary>
/// <remarks>
/// Ver <c>docs/runbook-workflow.md</c>, sección "Runbook D -- Reversión completa de la extracción de
/// Workflow", que documenta el razonamiento completo y enlaza esta clase como la verificación ejecutable
/// de sus dos afirmaciones centrales:
/// <list type="bullet">
/// <item>F9-02: los 6 eventos de integración movidos a <c>BitCode.Platform.Workflow.Contracts</c> se
/// siguen levantando exactamente igual (mismos tipos concretos, mismo mecanismo <c>RaiseDomainEvent</c>)
/// para un consumidor al que no le importa la extracción -- este test, que registra el módulo SIN pasar
/// por <c>Sample.Workflow.Api</c> (el host de referencia con Gateway/Kafka), es justamente ese
/// consumidor.</item>
/// <item>F9-05: <c>AddSharedMessagingKafka</c>/<c>AddSharedOutboxPublisher</c> son adiciones opcionales
/// del HOST (<c>Sample.Workflow.Api/InfrastructureModule.cs</c>), nunca una dependencia dura de
/// <c>AddSharedWorkflow</c> ni de <c>OutboxSaveChangesInterceptor</c> (que ya escribía en
/// <c>OutboxMessage</c> desde F1-23, antes de que Workflow tuviera ningún broker real) -- este test
/// construye el grafo completo y termina una instancia SIN registrar Kafka en absoluto, y confirma que
/// las filas de <c>OutboxMessage</c> con los eventos reales de instancia quedan escritas de todos modos
/// (el "camino de vuelta" a consumir Workflow embebido, sin broker, sigue intacto).</item>
/// </list>
/// A diferencia de <see cref="WorkflowDataOwnershipIntegrationTests"/> (que prueba CRUD crudo sobre las
/// siete entidades sin pasar por <c>AddSharedWorkflow</c>), esta clase SÍ registra
/// <c>AddSharedWorkflow()</c> (el cableado completo del módulo: health check, <c>IWorkflowActorContext</c>,
/// <c>IWorkflowEngine</c>) para demostrar que ni siquiera ese registro más completo necesita Kafka.
/// </remarks>
public class WorkflowReversionCompatibilityTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();

    public Task InitializeAsync() => _sqlServerFixture.InitializeAsync();

    public Task DisposeAsync() => _sqlServerFixture.DisposeAsync();

    [Fact]
    public async Task ModuloWorkflow_RegistradoSinKafka_LevantaYPersisteLosEventosDeIntegracionDeF902()
    {
        // Deliberadamente el "camino de vuelta" a F9-02/F9-05: ni AddSharedMessagingKafka ni
        // AddSharedOutboxPublisher se registran acá -- exactamente como se consumía Workflow ANTES de
        // F9-05 (ningún host de Fase 6 tenía Kafka wireado, ver comentario de
        // WorkflowServiceCollectionExtensions.AddSharedWorkflow).
        var connectionString = _sqlServerFixture.BuildIsolatedConnectionString("WfRev", nameof(ModuloWorkflow_RegistradoSinKafka_LevantaYPersisteLosEventosDeIntegracionDeF902));

        var services = new ServiceCollection();
        services.AddSharedPersistence<WorkflowDbContext>(connectionString);
        services.AddSharedWorkflow();
        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WorkflowDbContext>().Database.EnsureCreatedAsync();

        var actorUserId = Guid.NewGuid();

        var definiciones = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowDefinition, Guid>>();
        var versiones = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowVersion, Guid>>();
        var estados = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowState, Guid>>();
        var instancias = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowInstance, Guid>>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var definicion = new WorkflowDefinition(Guid.NewGuid(), "REVERSION_TEST", "Prueba de reversión F9-11", null);
        var version = new WorkflowVersion(Guid.NewGuid(), definicion.Id, 1);
        var estadoInicial = new WorkflowState(
            Guid.NewGuid(), version.Id, "INICIAL", "Inicial", esInicial: true, esFinal: false,
            requiereTarea: false, tituloTarea: null, asignadoPorDefectoUserId: null,
            slaMinutos: null, escalarAUserId: null);
        var estadoFinal = new WorkflowState(
            Guid.NewGuid(), version.Id, "FINAL", "Final", esInicial: false, esFinal: true,
            requiereTarea: false, tituloTarea: null, asignadoPorDefectoUserId: null, slaMinutos: null,
            escalarAUserId: null);

        await definiciones.AddAsync(definicion);
        await versiones.AddAsync(version);
        await estados.AddAsync(estadoInicial);
        await estados.AddAsync(estadoFinal);
        await unitOfWork.SaveChangesAsync();

        // Construye y termina la instancia directamente (sin pasar por IniciarInstanciaCommandHandler ni
        // por WorkflowEngine, internos al ensamblado) -- el constructor de WorkflowInstance levanta
        // WorkflowInstanciaIniciadaIntegrationEvent y AvanzarA(esFinal: true) levanta
        // WorkflowInstanciaFinalizadaIntegrationEvent, los mismos dos de los 6 contratos reales movidos en
        // F9-02 (docs/catalogo-eventos.md).
        var instancia = new WorkflowInstance(
            Guid.NewGuid(), definicion.Id, version.Id, estadoInicial.Id, actorUserId,
            new Dictionary<string, string>());
        await instancias.AddAsync(instancia);
        await unitOfWork.SaveChangesAsync();

        instancia.AvanzarA(estadoFinal.Id, esFinal: true);
        await unitOfWork.SaveChangesAsync();

        // Verificación real: sin ningún IEventPublisher/broker registrado, las dos filas de OutboxMessage
        // quedan escritas igual -- OutboxSaveChangesInterceptor (F1-23) nunca dependió de que existiera un
        // publisher, y ninguna de las dos SaveChangesAsync de arriba lanzó una excepción por falta de
        // Kafka. Esto es la evidencia de que "dejar de operar como servicio extraído y volver a consumir
        // Workflow embebido, sin broker" no perdió capacidad en el camino.
        List<OutboxMessage> outboxRows;
        await using (var readScope = provider.CreateAsyncScope())
        {
            var readContext = readScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            outboxRows = await readContext.Set<OutboxMessage>()
                .Where(m => m.TenantId == instancia.TenantId)
                .ToListAsync();
        }

        outboxRows.Should().Contain(m => m.EventType.Contains(nameof(WorkflowInstanciaIniciadaIntegrationEvent)),
            "el constructor de WorkflowInstance debe seguir levantando este evento de integración real de " +
            "F9-02 aun sin ningún broker/publisher registrado en el proceso");
        outboxRows.Should().Contain(m => m.EventType.Contains(nameof(WorkflowInstanciaFinalizadaIntegrationEvent)),
            "AvanzarA(esFinal: true) debe seguir levantando este evento de integración real de F9-02 aun " +
            "sin ningún broker/publisher registrado en el proceso");
    }
}
