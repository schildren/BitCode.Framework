using BitCode.Framework.Shared.Application;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Outbox;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F1-23 (Outbox base): verifica, contra un SQL Server real (Testcontainers), el criterio de
/// aceptación literal "evento no se pierde tras commit" — y su contraparte necesaria para que la
/// garantía sea real: si el commit no ocurre (rollback), tampoco queda el evento persistido de forma
/// huérfana. <see cref="Interceptors.OutboxSaveChangesInterceptor"/> escribe cada
/// <see cref="DomainEvent"/> levantado por un <c>AggregateRoot&lt;TId&gt;</c> como fila
/// <see cref="OutboxMessage"/> dentro del MISMO <c>SaveChangesAsync</c> que persiste el cambio de
/// negocio del agregado, nunca en una escritura separada.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class OutboxIntegrationTests(SqlServerContainerFixture fixture)
{
    private string BuildIsolatedConnectionString(
        [System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        fixture.BuildIsolatedConnectionString("Outbox", testName);

    private static async Task<ServiceProvider> BuildProviderAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        services.AddSharedApplication(typeof(OutboxIntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        await context.Database.EnsureCreatedAsync();

        return provider;
    }

    /// <summary>
    /// Criterio de aceptación literal: un comando simple (sin <see cref="ITransactionalCommand"/>) que
    /// modifica un agregado y levanta un evento de dominio deja, tras un <c>SaveChangesAsync</c>
    /// exitoso, TANTO el cambio de negocio COMO la fila <see cref="OutboxMessage"/> correspondiente —
    /// el evento no se pierde tras el commit porque nunca fue una escritura separada.
    /// </summary>
    [Fact]
    public async Task SuccessfulCommand_PersistsBusinessChangeAndOutboxMessageTogether()
    {
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var accountId = Guid.NewGuid();

        var result = await sender.Send(new OpenTestAccountCommand(accountId, "Cuenta con evento"));

        result.IsSuccess.Should().BeTrue();

        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var accountPersisted = await context.TestAccounts.IgnoreQueryFilters()
            .AnyAsync(account => account.Id == accountId);
        accountPersisted.Should().BeTrue("el cambio de negocio debe haberse confirmado");

        var outboxMessages = await context.Set<OutboxMessage>().IgnoreQueryFilters()
            .Where(message => message.EventType == typeof(TestAccountOpenedEvent).AssemblyQualifiedName)
            .ToListAsync();

        outboxMessages.Should().HaveCount(
            1,
            "el evento levantado por el agregado debe haber quedado persistido en la misma " +
            "transacción que el cambio de negocio, no en una escritura separada");
        outboxMessages[0].ProcessedAtUtc.Should().BeNull(
            "F1-23 solo escribe la fila pendiente; el relay que la marca como procesada es Fase 3");
        outboxMessages[0].PayloadJson.Should().Contain(accountId.ToString());
    }

    /// <summary>
    /// Atomicidad (mitad necesaria del criterio de aceptación "evento no se pierde tras commit": un
    /// evento tampoco puede sobrevivir SIN el commit del cambio de negocio que lo originó). Un
    /// <see cref="ITransactionalCommand"/> que levanta el evento en un primer paso, hace un
    /// <c>SaveChangesAsync</c> intermedio (igual que <c>TwoStepTransactionalCommand</c>,
    /// F1-07/F1-09) y luego falla en un segundo paso: el rollback de la transacción física debe
    /// descartar TANTO el cambio de negocio COMO la fila de Outbox ya enviada a SQL Server en ese
    /// SaveChanges intermedio — ninguno de los dos queda huérfano.
    /// </summary>
    [Fact]
    public async Task TransactionalCommand_WhenLaterStepFails_RollsBackBusinessChangeAndOutboxTogether()
    {
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var accountId = Guid.NewGuid();

        var act = async () => await sender.Send(
            new OpenTestAccountThenFailCommand(accountId, "Cuenta que no debe sobrevivir"));

        await act.Should().ThrowAsync<InvalidOperationException>();

        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var accountPersisted = await context.TestAccounts.IgnoreQueryFilters()
            .AnyAsync(account => account.Id == accountId);
        accountPersisted.Should().BeFalse(
            "el rollback debe descartar el cambio de negocio, aunque ya se había enviado a SQL " +
            "Server con un SaveChangesAsync intermedio antes del fallo posterior");

        var outboxMessages = await context.Set<OutboxMessage>().IgnoreQueryFilters()
            .Where(message => message.EventType == typeof(TestAccountOpenedEvent).AssemblyQualifiedName)
            .ToListAsync();

        outboxMessages.Should().BeEmpty(
            "el evento de dominio no debe sobrevivir un rollback: ambos comparten el mismo " +
            "ChangeTracker/transacción física que el cambio de negocio que lo originó");
    }
}

/// <summary>Evento de dominio de prueba, dedicado a ejercitar el Outbox writer (F1-23) de punta a punta.</summary>
public sealed record TestAccountOpenedEvent(Guid AccountId, string Name) : DomainEvent;

/// <summary>
/// Agregado de prueba mínimo (F1-23): existe únicamente para levantar un <see cref="DomainEvent"/> real
/// vía <c>RaiseDomainEvent</c> y ejercitar <see cref="Interceptors.OutboxSaveChangesInterceptor"/>
/// contra SQL Server real, sin depender de ningún agregado de negocio de otro módulo.
/// </summary>
public class TestAccount : AggregateRoot<Guid>, ITenantEntity
{
    public string Name { get; private set; } = string.Empty;

    public Guid TenantId { get; set; }

    public TestAccount(Guid id, string name) : base(id)
    {
        Name = name;
        RaiseDomainEvent(new TestAccountOpenedEvent(id, name));
    }

    private TestAccount()
    {
    }
}

public record OpenTestAccountCommand(Guid AccountId, string Name) : ICommand;

public class OpenTestAccountCommandHandler(IRepository<TestAccount, Guid> repository)
    : IRequestHandler<OpenTestAccountCommand, Result>
{
    public async Task<Result> Handle(OpenTestAccountCommand request, CancellationToken cancellationToken)
    {
        await repository.AddAsync(new TestAccount(request.AccountId, request.Name), cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Comando de prueba <see cref="ITransactionalCommand"/> dedicado a ejercitar el rollback coordinado
/// del Outbox (F1-23): abre el agregado (levantando el evento), hace un <c>SaveChangesAsync</c>
/// intermedio dentro de la misma transacción física y luego falla, para comprobar que ni el cambio de
/// negocio ni el evento ya enviado a SQL Server sobreviven al rollback.
/// </summary>
public record OpenTestAccountThenFailCommand(Guid AccountId, string Name) : ICommand, ITransactionalCommand;

public class OpenTestAccountThenFailCommandHandler(
    IRepository<TestAccount, Guid> repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<OpenTestAccountThenFailCommand, Result>
{
    public async Task<Result> Handle(OpenTestAccountThenFailCommand request, CancellationToken cancellationToken)
    {
        await repository.AddAsync(new TestAccount(request.AccountId, request.Name), cancellationToken);

        // SaveChangesAsync intermedio (mismo patrón que TwoStepTransactionalCommand, F1-07/F1-09):
        // tanto el TestAccount como su OutboxMessage ya llegaron a SQL Server en este punto, dentro de
        // la transacción física todavía abierta.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        throw new InvalidOperationException("Fallo intencional posterior al SaveChanges intermedio.");
    }
}
