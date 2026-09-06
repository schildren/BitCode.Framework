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
