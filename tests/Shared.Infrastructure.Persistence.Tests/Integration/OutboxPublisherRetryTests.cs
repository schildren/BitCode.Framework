using System.Text.Json;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Domain.Outbox;
using BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;
using BitCode.Framework.Shared.Kernel;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F3-07 (Retries): clasificación de errores transitorios/permanentes, backoff exponencial con jitter
/// (verificado a nivel de cálculo puro en <c>EventRetryBackoffTests</c>, Shared.Application.Tests) y
/// límite máximo de reintentos aplicado por <see cref="OutboxBatchProcessor"/> contra SQL Server real.
/// No necesita Kafka real (a diferencia de <see cref="OutboxPublisherIntegrationTests"/>): el punto de
/// esta clase es la política de reintentos en sí, no el round-trip contra un broker concreto.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class OutboxPublisherRetryTests(SqlServerContainerFixture sqlFixture)
{
    private string BuildIsolatedConnectionString([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        sqlFixture.BuildIsolatedConnectionString("ObxRetry", testName);

    private static async Task<ServiceProvider> BuildProviderAsync(
        string connectionString,
        IEventPublisher eventPublisher,
        OutboxPublisherOptions options,
        IEventPublishFailureClassifier? classifier = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        services.AddSingleton(eventPublisher);
        services.AddSingleton(options);
        services.AddSingleton(classifier ?? new DefaultEventPublishFailureClassifier());
        services.AddScoped<OutboxBatchProcessor>();

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        await context.Database.EnsureCreatedAsync();

        return provider;
    }

    private static async Task<OutboxMessage> SeedOutboxMessageAsync(IServiceProvider provider, object domainEvent)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Empty,
            EventType = domainEvent.GetType().AssemblyQualifiedName!,
            PayloadJson = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
            OccurredAtUtc = DateTime.UtcNow,
        };

        context.Set<OutboxMessage>().Add(message);
        await context.SaveChangesAsync();

        return message;
    }

    private static async Task<OutboxMessage?> FindOutboxMessageAsync(IServiceProvider provider, Guid id)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        return await context.Set<OutboxMessage>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(message => message.Id == id);
    }

    /// <summary>
    /// Un error PERMANENTE (clasificado explícitamente por un <see cref="IEventPublishFailureClassifier"/>
    /// de prueba) no debe reintentarse jamás, sin importar cuántos <c>MaxAttempts</c> queden disponibles:
    /// se marca <see cref="OutboxMessage.ExhaustedAtUtc"/> ya en el PRIMER fallo.
    /// </summary>
    [Fact]
    public async Task ProcessBatchAsync_PermanentError_ExhaustsOnFirstFailure()
    {
        var options = new OutboxPublisherOptions
        {
            Retry = new EventRetryPolicyOptions { MaxAttempts = 10, BaseDelay = TimeSpan.Zero },
        };
        var alwaysFailingPublisher = new AlwaysFailingEventPublisher();
        await using var provider = await BuildProviderAsync(
            BuildIsolatedConnectionString(),
            alwaysFailingPublisher,
            options,
            new AlwaysPermanentClassifier());

        var integrationEvent = new OutboxRetryTestEvent(Guid.NewGuid(), "Error permanente");
        var message = await SeedOutboxMessageAsync(provider, integrationEvent);

        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var result = await processor.ProcessBatchAsync();
            result.Claimed.Should().Be(1);
            result.Failed.Should().Be(1);
            result.Exhausted.Should().Be(1, "un error permanente se agota en el primer fallo, sin esperar a MaxAttempts");
        }

        var persisted = await FindOutboxMessageAsync(provider, message.Id);
        persisted!.RetryCount.Should().Be(1);
        persisted.ExhaustedAtUtc.Should().NotBeNull();
        persisted.ProcessedAtUtc.Should().BeNull("una fila agotada NUNCA se marca como procesada — no se pierde, queda visible para F3-08/intervención manual");

        // Un ciclo posterior ya no vuelve a reclamarla (ExhaustedAtUtc IS NOT NULL la excluye del claim).
        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var secondCycle = await processor.ProcessBatchAsync();
            secondCycle.Claimed.Should().Be(0, "una fila agotada no debe volver a reclamarse en ningún ciclo futuro");
        }

        alwaysFailingPublisher.CallCount.Should().Be(1, "un error permanente no debe reintentarse ni una sola vez más");
    }

    /// <summary>
    /// Un error TRANSITORIO que nunca deja de fallar sí se reintenta, pero solo hasta
    /// <see cref="EventRetryPolicyOptions.MaxAttempts"/> — no indefinidamente: al superarlo, la fila
    /// queda agotada y deja de reclamarse, aunque nunca se pierde (nunca se marca procesada).
    /// </summary>
    [Fact]
    public async Task ProcessBatchAsync_TransientNeverRecovers_ExhaustsAfterMaxAttempts()
    {
        const int maxAttempts = 3;
        var options = new OutboxPublisherOptions
        {
            Retry = new EventRetryPolicyOptions { MaxAttempts = maxAttempts, BaseDelay = TimeSpan.Zero },
        };
        var alwaysFailingPublisher = new AlwaysFailingEventPublisher();
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString(), alwaysFailingPublisher, options);

        var integrationEvent = new OutboxRetryTestEvent(Guid.NewGuid(), "Transitorio que nunca se recupera");
        var message = await SeedOutboxMessageAsync(provider, integrationEvent);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await using var scope = provider.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var result = await processor.ProcessBatchAsync();

            result.Claimed.Should().Be(1, $"la fila todavía debe poder reclamarse en el intento {attempt} (<= MaxAttempts)");
            result.Failed.Should().Be(1);

            var isLastAllowedAttempt = attempt == maxAttempts;
            result.Exhausted.Should().Be(isLastAllowedAttempt ? 1 : 0);
        }

        var persisted = await FindOutboxMessageAsync(provider, message.Id);
        persisted!.RetryCount.Should().Be(maxAttempts);
        persisted.ExhaustedAtUtc.Should().NotBeNull("tras agotar MaxAttempts intentos fallidos, la fila debe quedar marcada como agotada");
        persisted.ProcessedAtUtc.Should().BeNull("nunca se pierde: sigue existiendo, solo deja de reintentarse");

        // Un ciclo (maxAttempts + 1) adicional ya no debe volver a reclamarla ni a llamar al publisher.
        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var extraCycle = await processor.ProcessBatchAsync();
            extraCycle.Claimed.Should().Be(0);
        }

        alwaysFailingPublisher.CallCount.Should().Be(maxAttempts, "no debe haber ningún intento más allá de MaxAttempts");
    }

    /// <summary>
    /// Con un <see cref="EventRetryPolicyOptions.BaseDelay"/> real (no cero), un fallo transitorio
    /// programa <see cref="OutboxMessage.LockedUntilUtc"/> en el futuro: un ciclo inmediatamente
    /// posterior NO debe poder reclamar la fila todavía (el backoff no transcurrió).
    /// </summary>
    [Fact]
    public async Task ProcessBatchAsync_RealBackoff_NotClaimedBeforeElapsed()
    {
        var options = new OutboxPublisherOptions
        {
            Retry = new EventRetryPolicyOptions
            {
                MaxAttempts = 10,
                BaseDelay = TimeSpan.FromSeconds(30), // suficientemente largo para que el test no dependa de timing ajustado
                MaxDelay = TimeSpan.FromMinutes(5),
            },
        };
        var failingOncePublisher = new AlwaysFailingEventPublisher();
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString(), failingOncePublisher, options);

        var integrationEvent = new OutboxRetryTestEvent(Guid.NewGuid(), "Backoff real");
        var message = await SeedOutboxMessageAsync(provider, integrationEvent);

        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var firstAttempt = await processor.ProcessBatchAsync();
            firstAttempt.Claimed.Should().Be(1);
            firstAttempt.Failed.Should().Be(1);
        }

        var afterFirstAttempt = await FindOutboxMessageAsync(provider, message.Id);
        afterFirstAttempt!.LockedUntilUtc.Should().NotBeNull();
        afterFirstAttempt.LockedUntilUtc!.Value.Should().BeAfter(DateTime.UtcNow, "el backoff (base 30s) todavía no debería haber transcurrido");

        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var immediateSecondCycle = await processor.ProcessBatchAsync();
            immediateSecondCycle.Claimed.Should().Be(0, "el backoff todavía no transcurrió, no debería poder reclamarse de nuevo tan pronto");
        }

        failingOncePublisher.CallCount.Should().Be(1);
    }
}

/// <summary>Evento de integración de prueba dedicado a <see cref="OutboxPublisherRetryTests"/> (F3-07).</summary>
public sealed record OutboxRetryTestEvent(Guid AccountId, string Name) : DomainEvent, IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Tests.OutboxRetryTestEvent";

    public int SchemaVersion => 1;
}

/// <summary><see cref="IEventPublisher"/> de prueba que SIEMPRE falla, contando cuántas veces se lo invocó.</summary>
public sealed class AlwaysFailingEventPublisher : IEventPublisher
{
    private int _callCount;

    public int CallCount => _callCount;

    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        throw new InvalidOperationException("Fallo simulado de publicación (siempre falla, F3-07).");
    }

    public async Task PublishAsync(IEnumerable<IIntegrationEvent> integrationEvents, CancellationToken cancellationToken = default)
    {
        foreach (var integrationEvent in integrationEvents)
        {
            await PublishAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary><see cref="IEventPublishFailureClassifier"/> de prueba: clasifica TODO como permanente.</summary>
public sealed class AlwaysPermanentClassifier : IEventPublishFailureClassifier
{
    public EventPublishFailureKind Classify(Exception exception) => EventPublishFailureKind.Permanent;
}
