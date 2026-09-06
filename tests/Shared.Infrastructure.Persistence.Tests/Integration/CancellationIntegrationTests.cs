using BitCode.Framework.Shared.Application;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F1-10 (timeouts y cancelación): verifica, contra un SQL Server real (Testcontainers) y a través
/// del pipeline completo de MediatR (<c>AddSharedApplication</c> + <c>TransactionBehavior</c>), el
/// criterio de aceptación literal "requests cancelados liberan recursos": cuando un
/// <see cref="ITransactionalCommand"/> es cancelado después de haber enviado una escritura a SQL
/// Server dentro de una transacción todavía abierta (bloqueo de fila adquirido), (a) la excepción que
/// llega al llamador es <see cref="OperationCanceledException"/> sin traducir a un
/// <c>Result.Failure</c>/ProblemDetails, (b) el cambio nunca queda persistido (rollback real, no solo
/// abandono de la conexión) y (c) el bloqueo de fila se libera de inmediato — una escritura posterior
/// sobre la misma fila, desde una conexión nueva con un <c>CommandTimeout</c> corto, tiene éxito sin
/// esperar a que el pool cierre la conexión de la transacción cancelada.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class CancellationIntegrationTests(SqlServerContainerFixture fixture)
{
    private string BuildIsolatedConnectionString([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        fixture.BuildIsolatedConnectionString("Cancellation", testName);

    private static async Task<ServiceProvider> BuildProviderAsync(string connectionString, int? commandTimeoutSeconds = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (commandTimeoutSeconds is { } timeout)
        {
            services.AddSharedPersistence<MultiTenantTestDbContext>(
                connectionString,
                options => options.CommandTimeoutSeconds = timeout);
        }
        else
        {
            services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        }

        services.AddSharedApplication(typeof(CancellationIntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        await context.Database.EnsureCreatedAsync();

        return provider;
    }

    [Fact]
    public async Task Cancelled_AfterRowLockHeld_PropagatesAndReleasesLock()
    {
        var connectionString = BuildIsolatedConnectionString();
        await using var provider = await BuildProviderAsync(connectionString);
        var entityId = Guid.NewGuid();

        // Estado inicial, persistido fuera del pipeline (no interesa su Result, solo la fila base).
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
            seedContext.TestEntities.Add(new TestEntity(entityId, "Original", 1));
            await seedContext.SaveChangesAsync();
        }

        using var cts = new CancellationTokenSource();

        await using (var scope = provider.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            // El handler renombra la fila, hace flush (SaveChangesAsync intermedio dentro de la
            // transacción abierta por TransactionBehavior — la fila queda con un bloqueo exclusivo en
            // SQL Server) y recién ahí simula la cancelación: cancela el mismo token que
            // TransactionBehavior está observando y relanza OperationCanceledException, tal como
            // ocurriría si el cliente HTTP cerrara la conexión mientras un comando SQL está en vuelo.
            var act = async () => await sender.Send(
                new RenameThenCancelCommand(entityId, "Cambiado por comando cancelado", cts.Cancel),
                cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>(
                "una cancelación no debe traducirse a un Result.Failure ni silenciarse: debe " +
                "propagarse para que ASP.NET Core la trate como cancelación del request, no como un " +
                "error 500 genérico");
        }

        // (b) Rollback real: el nombre nunca debió quedar persistido, aunque el SaveChangesAsync
        // intermedio ya lo había enviado a SQL Server antes de la cancelación.
        await using (var verificationScope = provider.CreateAsyncScope())
        {
            var verificationContext = verificationScope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
            var persisted = await verificationContext.TestEntities.IgnoreQueryFilters()
                .FirstAsync(e => e.Id == entityId);

            persisted.Name.Should().Be(
                "Original",
                "TransactionBehavior debe revertir la transacción ante una cancelación igual que ante " +
                "cualquier otro fallo, no solo abandonar la conexión y confiar en que el pool la limpie");
        }

        // (c) Liberación del bloqueo: una escritura posterior sobre la MISMA fila, desde una conexión
        // nueva con un CommandTimeout corto, debe tener éxito de inmediato. Si el rollback no hubiera
        // liberado el bloqueo de fila adquirido antes de la cancelación, este UPDATE se bloquearía
        // hasta agotar el CommandTimeout corto y el test fallaría con un timeout, no con una aserción.
        await using var fastTimeoutProvider = await BuildProviderAsync(connectionString, commandTimeoutSeconds: 5);
        await using var fastTimeoutScope = fastTimeoutProvider.CreateAsyncScope();
        var fastSender = fastTimeoutScope.ServiceProvider.GetRequiredService<ISender>();

        var subsequentResult = await fastSender.Send(
            new RenameConcurrentEntityCommand2(entityId, "Editado después de la cancelación"));

        subsequentResult.IsSuccess.Should().BeTrue(
            "si el bloqueo de fila de la transacción cancelada siguiera activo, esta escritura se " +
            "hubiera colgado hasta el CommandTimeout corto en vez de tener éxito de inmediato");

        var fastTimeoutContext = fastTimeoutScope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var finalState = await fastTimeoutContext.TestEntities.IgnoreQueryFilters()
            .FirstAsync(e => e.Id == entityId);
        finalState.Name.Should().Be("Editado después de la cancelación");
    }
}

/// <summary>
/// Comando de prueba que renombra una fila, la persiste con un <c>SaveChangesAsync</c> intermedio
/// (adquiriendo un bloqueo exclusivo real en SQL Server dentro de la transacción todavía abierta) y
/// recién ahí simula la cancelación del request cancelando el mismo <see cref="CancellationToken"/>
/// que <c>TransactionBehavior</c> observa, para ejercitar el camino de cancelación con un recurso de
/// base de datos realmente abierto (no solo una cancelación "en frío" antes de tocar la base).
/// </summary>
public record RenameThenCancelCommand(Guid Id, string NewName, Action CancelRequestToken)
    : ICommand, ITransactionalCommand;

public class RenameThenCancelCommandHandler(
    IRepository<TestEntity, Guid> repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RenameThenCancelCommand, Result>
{
    public async Task<Result> Handle(RenameThenCancelCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(request.Id, cancellationToken);
        entity!.Name = request.NewName;
        repository.Update(entity);

        // Flush intermedio: envía el UPDATE a SQL Server, que adquiere un bloqueo exclusivo de fila
        // dentro de la transacción todavía abierta (la maneja TransactionBehavior, no este handler).
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Simula, con un bloqueo de fila real ya adquirido, el mismo efecto observable que una
        // cancelación real de I/O: cancela el mismo CancellationTokenSource que el test pasó a
        // ISender.Send (el mismo token que TransactionBehavior está observando en este momento) y
        // relanza OperationCanceledException tal como lo haría el runtime.
        request.CancelRequestToken();
        throw new OperationCanceledException("Cancelación simulada tras el flush intermedio.", cancellationToken);
    }
}

/// <summary>
/// Comando simple (no transaccional) que renombra la fila para verificar, después de la cancelación
/// anterior, que ningún bloqueo quedó huérfano.
/// </summary>
public record RenameConcurrentEntityCommand2(Guid Id, string NewName) : ICommand;

public class RenameConcurrentEntityCommand2Handler(IRepository<TestEntity, Guid> repository)
    : IRequestHandler<RenameConcurrentEntityCommand2, Result>
{
    public async Task<Result> Handle(RenameConcurrentEntityCommand2 request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(request.Id, cancellationToken);

        if (entity is null)
        {
            return Result.Failure(Error.NotFound("TestEntity.NotFound", "No se encontró la entidad."));
        }

        entity.Name = request.NewName;
        repository.Update(entity);

        return Result.Success();
    }
}
