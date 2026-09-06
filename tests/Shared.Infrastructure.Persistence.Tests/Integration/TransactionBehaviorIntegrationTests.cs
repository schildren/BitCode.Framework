using BitCode.Framework.Shared.Application;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F1-07 (endurecimiento del pipeline transaccional): verifica, contra un SQL Server real
/// (Testcontainers) y a través del pipeline completo de MediatR (<c>AddSharedApplication</c> +
/// <c>TransactionBehavior</c>), el criterio de aceptación literal "Rollback verificado" para un
/// comando <see cref="ITransactionalCommand"/> que coordina dos escrituras dependientes: si la
/// segunda falla, NINGÚN dato queda persistido, ni siquiera el de la primera escritura (que ya se
/// había enviado a SQL Server con un <c>SaveChangesAsync</c> intermedio dentro de la misma
/// transacción todavía abierta).
/// </summary>
[Collection(SqlServerCollection.Name)]
public class TransactionBehaviorIntegrationTests(SqlServerContainerFixture fixture)
{
    private string BuildIsolatedConnectionString([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        fixture.BuildIsolatedConnectionString("TxBehavior", testName);

    private static async Task<ServiceProvider> BuildProviderAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        services.AddSharedApplication(typeof(TransactionBehaviorIntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        await context.Database.EnsureCreatedAsync();

        return provider;
    }

    [Fact]
    public async Task TransactionalCommand_WhenSecondStepFails_RollsBackEvenAlreadyFlushedFirstStep()
    {
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var act = async () => await sender.Send(
            new TwoStepTransactionalCommand(firstId, secondId, FailOnSecondStep: true));

        await act.Should().ThrowAsync<InvalidOperationException>();

        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var firstPersisted = await context.TestEntities.IgnoreQueryFilters()
            .AnyAsync(e => e.Id == firstId);
        var secondPersisted = await context.TestEntities.IgnoreQueryFilters()
            .AnyAsync(e => e.Id == secondId);

        firstPersisted.Should().BeFalse(
            "el rollback de la transacción debe descartar incluso la primera escritura, ya enviada " +
            "a SQL Server con un SaveChangesAsync intermedio antes de que la segunda fallara");
        secondPersisted.Should().BeFalse();
    }

    [Fact]
    public async Task TransactionalCommand_WhenBothStepsSucceed_CommitsBothWrites()
    {
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var result = await sender.Send(
            new TwoStepTransactionalCommand(firstId, secondId, FailOnSecondStep: false));

        result.IsSuccess.Should().BeTrue();

        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var firstPersisted = await context.TestEntities.IgnoreQueryFilters().AnyAsync(e => e.Id == firstId);
        var secondPersisted = await context.TestEntities.IgnoreQueryFilters().AnyAsync(e => e.Id == secondId);

        firstPersisted.Should().BeTrue();
        secondPersisted.Should().BeTrue();
    }

    /// <summary>
    /// F1-09 (Unit of Work: nesting): un handler de <see cref="ITransactionalCommand"/> puede
    /// despachar otro <see cref="ITransactionalCommand"/> a través de <c>ISender</c> dentro del mismo
    /// scope (mismo <see cref="IUnitOfWork"/>/<c>DbContext</c>). Antes del ajuste de F1-09, el
    /// <c>CommitAsync</c> del comando interno confirmaba y disponía la transacción física completa de
    /// forma prematura; cuando el comando externo intentaba confirmar su propio trabajo posterior, el
    /// <c>CommitAsync</c> externo fallaba con <c>InvalidOperationException</c> ("No hay una
    /// transacción activa") y la escritura posterior al comando interno quedaba sin persistir sin que
    /// el comando externo se enterara del motivo real. Este test verifica, contra SQL Server real, que
    /// las tres escrituras (externa-antes, interna, externa-después) terminan en una única transacción
    /// física y se confirman todas juntas cuando el comando externo (el nivel más externo) hace commit.
    /// </summary>
    [Fact]
    public async Task NestedTransactionalCommand_WhenBothSucceed_CommitsAllWritesInOuterCommit()
    {
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var beforeId = Guid.NewGuid();
        var innerId = Guid.NewGuid();
        var afterId = Guid.NewGuid();

        var result = await sender.Send(
            new OuterTransactionalCommand(beforeId, innerId, afterId, FailInnerCommand: false));

        result.IsSuccess.Should().BeTrue();

        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        (await context.TestEntities.IgnoreQueryFilters().AnyAsync(e => e.Id == beforeId)).Should().BeTrue();
        (await context.TestEntities.IgnoreQueryFilters().AnyAsync(e => e.Id == innerId)).Should().BeTrue();
        (await context.TestEntities.IgnoreQueryFilters().AnyAsync(e => e.Id == afterId)).Should().BeTrue();
    }

    /// <summary>
    /// F1-09 (Unit of Work: límites transaccionales): si el comando anidado falla, todo el trabajo del
    /// comando contenedor —incluida la escritura "antes" del comando interno, ya enviada a SQL Server
    /// con un <c>SaveChangesAsync</c> intermedio— se revierte. No existe "rollback parcial" de un nivel
    /// anidado: ambos comandos comparten el mismo <c>DbContext</c>/<c>ChangeTracker</c>.
    /// </summary>
    [Fact]
    public async Task NestedTransactionalCommand_WhenInnerFails_RollsBackOuterWriteBeforeToo()
    {
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var beforeId = Guid.NewGuid();
        var innerId = Guid.NewGuid();
        var afterId = Guid.NewGuid();

        var act = async () => await sender.Send(
            new OuterTransactionalCommand(beforeId, innerId, afterId, FailInnerCommand: true));

        await act.Should().ThrowAsync<InvalidOperationException>();

        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        (await context.TestEntities.IgnoreQueryFilters().AnyAsync(e => e.Id == beforeId)).Should().BeFalse(
            "el fallo del comando anidado debe revertir también la escritura previa del comando " +
            "contenedor, ya que ambos comparten la misma transacción física");
        (await context.TestEntities.IgnoreQueryFilters().AnyAsync(e => e.Id == innerId)).Should().BeFalse();
        (await context.TestEntities.IgnoreQueryFilters().AnyAsync(e => e.Id == afterId)).Should().BeFalse();
    }
}

/// <summary>
/// Comando de prueba que coordina dos escrituras dependientes bajo la misma transacción explícita:
/// existe únicamente para demostrar, contra SQL Server real, el rollback coordinado de
/// <see cref="ITransactionalCommand"/> exigido por el criterio de aceptación de F1-07.
/// </summary>
public record TwoStepTransactionalCommand(Guid FirstId, Guid SecondId, bool FailOnSecondStep)
    : ICommand, ITransactionalCommand;

public class TwoStepTransactionalCommandHandler(
    IRepository<TestEntity, Guid> repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TwoStepTransactionalCommand, Result>
{
    public async Task<Result> Handle(TwoStepTransactionalCommand request, CancellationToken cancellationToken)
    {
        await repository.AddAsync(new TestEntity(request.FirstId, "Primer paso", 1), cancellationToken);

        // SaveChangesAsync intermedio: necesario porque el segundo paso depende de que el primero ya
        // esté persistido (caso real de uso de ITransactionalCommand). Ambos quedan dentro de la
        // misma transacción de base de datos abierta por TransactionBehavior antes de invocar este
        // handler, y esa transacción es la que garantiza el rollback coordinado si algo falla después.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (request.FailOnSecondStep)
        {
            throw new InvalidOperationException("Fallo intencional en el segundo paso del comando de prueba.");
        }

        await repository.AddAsync(new TestEntity(request.SecondId, "Segundo paso", 2), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Comando de prueba que despacha un <see cref="InnerTransactionalCommand"/> (otro
/// <see cref="ITransactionalCommand"/>) a través de <c>ISender</c> dentro del mismo scope, para
/// ejercitar el escenario de anidamiento de F1-09 sobre el mismo <see cref="IUnitOfWork"/>/
/// <c>DbContext</c>.
/// </summary>
public record OuterTransactionalCommand(Guid BeforeId, Guid InnerId, Guid AfterId, bool FailInnerCommand)
    : ICommand, ITransactionalCommand;

public class OuterTransactionalCommandHandler(
    IRepository<TestEntity, Guid> repository,
    IUnitOfWork unitOfWork,
    ISender sender)
    : IRequestHandler<OuterTransactionalCommand, Result>
{
    public async Task<Result> Handle(OuterTransactionalCommand request, CancellationToken cancellationToken)
    {
        await repository.AddAsync(new TestEntity(request.BeforeId, "Externo - antes", 1), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await sender.Send(
            new InnerTransactionalCommand(request.InnerId, request.FailInnerCommand),
            cancellationToken);

        await repository.AddAsync(new TestEntity(request.AfterId, "Externo - después", 3), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public record InnerTransactionalCommand(Guid Id, bool Fail) : ICommand, ITransactionalCommand;

public class InnerTransactionalCommandHandler(
    IRepository<TestEntity, Guid> repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<InnerTransactionalCommand, Result>
{
    public async Task<Result> Handle(InnerTransactionalCommand request, CancellationToken cancellationToken)
    {
        if (request.Fail)
        {
            throw new InvalidOperationException("Fallo intencional en el comando anidado de prueba.");
        }

        await repository.AddAsync(new TestEntity(request.Id, "Interno", 2), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
