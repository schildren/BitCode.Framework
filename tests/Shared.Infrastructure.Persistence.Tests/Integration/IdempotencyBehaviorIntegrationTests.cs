using BitCode.Framework.Shared.Application;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Idempotency;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F1-22 (Idempotencia API): verifica, contra un SQL Server real (Testcontainers) y a través del
/// pipeline completo de MediatR (<c>AddSharedApplication</c> + <c>IdempotencyBehavior</c> +
/// <c>TransactionBehavior</c> + <c>EfIdempotencyStore</c>), el criterio de aceptación literal "POST
/// repetido no duplica operación": reenviar el mismo comando con la misma Idempotency-Key ejecuta el
/// handler una única vez, y reenviarlo con la misma clave pero un payload distinto se rechaza en vez
/// de tratarse como el mismo reintento.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class IdempotencyBehaviorIntegrationTests(SqlServerContainerFixture fixture)
{
    private string BuildIsolatedConnectionString(
        [System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        fixture.BuildIsolatedConnectionString("Idempotency", testName);

    private static async Task<(ServiceProvider Provider, FakeIdempotencyKeyProvider KeyProvider)> BuildProviderAsync(
        string connectionString)
    {
        var keyProvider = new FakeIdempotencyKeyProvider();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        // Registrado ANTES de AddSharedApplication, mismo patrón que
        // AddHttpContextIdempotencyKeyProvider(): gana sobre el NullIdempotencyKeyProvider por
        // defecto (TryAddScoped).
        services.AddSingleton<IIdempotencyKeyProvider>(keyProvider);
        services.AddSharedApplication(typeof(IdempotencyBehaviorIntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        await context.Database.EnsureCreatedAsync();

        return (provider, keyProvider);
    }

    /// <summary>
    /// Escenario central (a) del criterio de aceptación: mismo comando, misma Idempotency-Key, mismo
    /// payload. El segundo envío no debe volver a insertar la entidad, y ambas respuestas deben ser
    /// idénticas (el mismo Id ya generado en el primer envío).
    /// </summary>
    [Fact]
    public async Task SameKeyAndSamePayload_ExecutesHandlerOnceAndReturnsIdenticalResult()
    {
        var (provider, keyProvider) = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var _ = provider;
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        keyProvider.IdempotencyKey = "same-key-same-payload";
        var command = new CreateTestEntityIdempotentCommand("Idempotente", 1);

        var firstResult = await sender.Send(command);
        var secondResult = await sender.Send(command);

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        secondResult.Value.Should().Be(
            firstResult.Value,
            "el segundo envío con la misma Idempotency-Key debe devolver el mismo resultado ya obtenido");

        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var matchingRows = await context.TestEntities.IgnoreQueryFilters()
            .Where(e => e.Id == firstResult.Value)
            .CountAsync();
        matchingRows.Should().Be(1, "el efecto de negocio debe haber ocurrido UNA sola vez, no dos");
    }

    /// <summary>
    /// Escenario central (b) del criterio de aceptación: misma Idempotency-Key, payload distinto. El
    /// segundo envío debe rechazarse (nunca ejecutarse como si fuera el mismo reintento), y no debe
    /// crear una segunda entidad bajo esa clave.
    /// </summary>
    [Fact]
    public async Task SameKeyWithDifferentPayload_RejectsSecondCommandAndDoesNotCreateSecondEntity()
    {
        var (provider, keyProvider) = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var _ = provider;
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        keyProvider.IdempotencyKey = "same-key-different-payload";

        var firstResult = await sender.Send(new CreateTestEntityIdempotentCommand("Original", 1));
        var secondResult = await sender.Send(new CreateTestEntityIdempotentCommand("Distinto", 2));

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsFailure.Should().BeTrue();
        secondResult.Error.Code.Should().Be("Idempotency.KeyReused");

        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var totalRows = await context.TestEntities.IgnoreQueryFilters().CountAsync();
        totalRows.Should().Be(1, "el comando rechazado nunca debió llegar a ejecutar el handler");
    }

    /// <summary>
    /// Sin ninguna Idempotency-Key, el comando se rechaza antes de tocar el handler — nunca se
    /// ejecuta en silencio como si no fuera idempotente (decisión documentada en
    /// <c>IIdempotentCommand</c>/docs/convenciones.md).
    /// </summary>
    [Fact]
    public async Task WithoutIdempotencyKey_RejectsCommandWithoutExecutingHandler()
    {
        var (provider, keyProvider) = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var _ = provider;
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        keyProvider.IdempotencyKey = null;

        var result = await sender.Send(new CreateTestEntityIdempotentCommand("Sin clave", 1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Idempotency.KeyRequired");

        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        (await context.TestEntities.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    /// <summary>
    /// Atomicidad (F1-22): si el handler falla a mitad de camino (excepción, no <c>Result.Failure</c>),
    /// ni el efecto de negocio ni el registro de idempotencia quedan persistidos — ninguno de los dos
    /// SaveChangesAsync intermedios llega a confirmarse porque ambos comparten el mismo
    /// <c>ChangeTracker</c>/scope, y la excepción se propaga sin que <c>TransactionBehavior</c> llegue
    /// a llamar <c>SaveChangesAsync</c>. Un reintento posterior con la misma clave y un payload válido
    /// se ejecuta con normalidad, como si el intento fallido nunca hubiera ocurrido.
    /// </summary>
    [Fact]
    public async Task WhenHandlerThrows_NothingIsPersisted_AndRetryLaterSucceeds()
    {
        var (provider, keyProvider) = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var _ = provider;
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        keyProvider.IdempotencyKey = "key-con-fallo-primero";

        var act = async () => await sender.Send(
            new CreateTestEntityIdempotentCommand("Va a fallar", 1, FailHandler: true));

        await act.Should().ThrowAsync<InvalidOperationException>();

        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        (await context.TestEntities.IgnoreQueryFilters().AnyAsync()).Should().BeFalse(
            "el fallo del handler no debe dejar ninguna entidad persistida");

        var retryResult = await sender.Send(
            new CreateTestEntityIdempotentCommand("Reintento exitoso", 2, FailHandler: false));

        retryResult.IsSuccess.Should().BeTrue(
            "un intento fallido anterior con la misma clave no debe bloquear un reintento válido " +
            "posterior: no quedó ningún registro de idempotencia guardado bajo esa clave");
        (await context.TestEntities.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }
}

/// <summary>
/// Comando de prueba <see cref="IIdempotentCommand"/> dedicado (F1-22): existe únicamente para
/// ejercitar <c>IdempotencyBehavior</c> de punta a punta contra SQL Server real, sin depender del
/// pipeline HTTP de <c>Sample.Api</c>.
/// </summary>
public record CreateTestEntityIdempotentCommand(string Name, int Amount, bool FailHandler = false)
    : ICommand<Guid>, IIdempotentCommand;

public class CreateTestEntityIdempotentCommandHandler(IRepository<TestEntity, Guid> repository)
    : IRequestHandler<CreateTestEntityIdempotentCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateTestEntityIdempotentCommand request,
        CancellationToken cancellationToken)
    {
        if (request.FailHandler)
        {
            throw new InvalidOperationException("Fallo intencional del handler de prueba.");
        }

        var entity = new TestEntity(Guid.NewGuid(), request.Name, request.Amount);
        await repository.AddAsync(entity, cancellationToken);

        return entity.Id;
    }
}
