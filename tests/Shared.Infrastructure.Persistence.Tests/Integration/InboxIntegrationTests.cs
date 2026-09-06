using BitCode.Framework.Shared.Application;
using BitCode.Framework.Shared.Application.Inbox;
using BitCode.Framework.Shared.Domain.Inbox;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F1-24 (Inbox base): verifica, contra un SQL Server real (Testcontainers), el criterio de aceptación
/// literal "duplicados descartados" — y su contraparte necesaria para que la deduplicación sea
/// correcta: un mensaje cuyo handler falló la primera vez NO queda tratado como duplicado, un
/// reintento posterior con el mismo <c>messageId</c> vuelve a ejecutar el handler.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class InboxIntegrationTests(SqlServerContainerFixture fixture)
{
    private string BuildIsolatedConnectionString(
        [System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        fixture.BuildIsolatedConnectionString("Inbox", testName);

    private static async Task<ServiceProvider> BuildProviderAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        services.AddSharedApplication(typeof(InboxIntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        await context.Database.EnsureCreatedAsync();

        return provider;
    }

    /// <summary>
    /// Criterio de aceptación literal: procesar el mismo <c>messageId</c> dos veces ejecuta el handler
    /// UNA sola vez — la segunda llamada se descarta sin duplicar el efecto de negocio, y devuelve
    /// <see cref="InboxProcessOutcome.Discarded"/> en vez de volver a ejecutar el handler.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_CalledTwiceWithSameMessageId_ExecutesHandlerOnlyOnce()
    {
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IInboxMessageProcessor>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<InboxTestAccount, Guid>>();

        var messageId = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid();
        var handlerExecutions = 0;

        Task Handler(CancellationToken ct)
        {
            handlerExecutions++;
            return repository.AddAsync(new InboxTestAccount(accountId, "Cuenta desde Inbox"), ct);
        }

        var firstOutcome = await processor.ProcessAsync(messageId, "TestAccountOpened", "{}", Handler);
        var secondOutcome = await processor.ProcessAsync(messageId, "TestAccountOpened", "{}", Handler);

        firstOutcome.Should().Be(InboxProcessOutcome.Processed);
        secondOutcome.Should().Be(
            InboxProcessOutcome.Discarded,
            "el mismo messageId ya fue procesado con éxito en la primera llamada");
        handlerExecutions.Should().Be(
            1,
            "el handler no debe volver a ejecutarse para un mensaje ya procesado (duplicados descartados)");

        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var accountsPersisted = await context.Set<InboxTestAccount>().IgnoreQueryFilters()
            .CountAsync(account => account.Id == accountId);
        accountsPersisted.Should().Be(1, "el efecto de negocio no debe duplicarse");

        var inboxMessages = await context.Set<InboxMessage>().IgnoreQueryFilters()
            .Where(message => message.MessageId == messageId)
            .ToListAsync();
        inboxMessages.Should().HaveCount(1);
        inboxMessages[0].ProcessedAtUtc.Should().NotBeNull();
    }

    /// <summary>
    /// Contraparte necesaria del criterio de aceptación: un mensaje cuyo handler lanza una excepción la
    /// primera vez NO debe quedar registrado como procesado (ninguna fila de Inbox sobrevive), así que
    /// un reintento posterior con el MISMO <c>messageId</c> ejecuta el handler de nuevo — no se trata
    /// como un duplicado real, porque nunca se completó con éxito.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_WhenHandlerFailsFirstAttempt_RetryWithSameMessageIdExecutesHandlerAgain()
    {
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IInboxMessageProcessor>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<InboxTestAccount, Guid>>();

        var messageId = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid();
        var handlerExecutions = 0;

        Task FailingHandler(CancellationToken ct)
        {
            handlerExecutions++;
            throw new InvalidOperationException("Fallo intencional en el primer intento.");
        }

        var act = async () => await processor.ProcessAsync(messageId, "TestAccountOpened", "{}", FailingHandler);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var inboxMessagesAfterFailure = await context.Set<InboxMessage>().IgnoreQueryFilters()
            .Where(message => message.MessageId == messageId)
            .ToListAsync();
        inboxMessagesAfterFailure.Should().BeEmpty(
            "un intento fallido no debe dejar ninguna fila de Inbox: no se llamó a SaveChangesAsync");

        Task SucceedingHandler(CancellationToken ct) =>
            repository.AddAsync(new InboxTestAccount(accountId, "Cuenta tras reintento"), ct);

        var retryOutcome = await processor.ProcessAsync(messageId, "TestAccountOpened", "{}", SucceedingHandler);

        retryOutcome.Should().Be(
            InboxProcessOutcome.Processed,
            "el reintento con el mismo messageId no debe tratarse como duplicado, porque el primer " +
            "intento nunca se completó con éxito");
        handlerExecutions.Should().Be(1, "el handler que falló, no el que tuvo éxito, se cuenta en esta variable");

        var accountPersisted = await context.Set<InboxTestAccount>().IgnoreQueryFilters()
            .AnyAsync(account => account.Id == accountId);
        accountPersisted.Should().BeTrue("el reintento exitoso debe haber persistido el efecto de negocio");

        var inboxMessagesAfterRetry = await context.Set<InboxMessage>().IgnoreQueryFilters()
            .Where(message => message.MessageId == messageId)
            .ToListAsync();
        inboxMessagesAfterRetry.Should().HaveCount(1);
        inboxMessagesAfterRetry[0].ProcessedAtUtc.Should().NotBeNull();
    }
}

/// <summary>
/// Agregado de prueba mínimo (F1-24): existe únicamente para ejercitar el efecto de negocio de un
/// handler invocado por <see cref="IInboxMessageProcessor"/>, sin depender de ningún agregado de otro
/// módulo. No necesita levantar eventos de dominio (a diferencia de <c>TestAccount</c> de F1-23): esta
/// tarea ejercita el lado receptor, no el emisor.
/// </summary>
public class InboxTestAccount : Entity<Guid>, ITenantEntity
{
    public string Name { get; private set; } = string.Empty;

    public Guid TenantId { get; set; }

    public InboxTestAccount(Guid id, string name) : base(id)
    {
        Name = name;
    }

    private InboxTestAccount()
    {
    }
}
