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
/// F1-08 (concurrencia optimista): verifica, contra un SQL Server real (Testcontainers) y a través
/// del pipeline completo de MediatR (<c>AddSharedApplication</c> + <c>TransactionBehavior</c>), el
/// criterio de aceptación literal "conflictos devuelven respuesta definida" para una entidad marcada
/// con <see cref="IHasConcurrencyToken"/>: dos ediciones concurrentes del mismo registro (dos
/// DbContext/sesiones separadas que cargan la misma fila antes de que ninguna la guarde) resultan en
/// que la segunda en persistir reciba un <c>Result.Failure</c> uniforme con
/// <see cref="ConcurrencyError.Conflict"/> (Error.Type = Conflict, HTTP 409 vía
/// ResultExtensions.ToProblemDetails), nunca una excepción sin controlar propagada hasta el llamador.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ConcurrencyIntegrationTests(SqlServerContainerFixture fixture)
{
    private string BuildIsolatedConnectionString([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        fixture.BuildIsolatedConnectionString("Concurrency", testName);

    private static async Task<ServiceProvider> BuildProviderAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        services.AddSharedApplication(typeof(ConcurrencyIntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        await context.Database.EnsureCreatedAsync();

        return provider;
    }

    [Fact]
    public async Task ConcurrentEdits_SecondSaveGetsUniformConflictResult_NotUnhandledException()
    {
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString());
        var entityId = Guid.NewGuid();

        // Estado inicial, persistido fuera del pipeline (no interesa su Result, solo la fila base).
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
            seedContext.ConcurrentTestEntities.Add(new ConcurrentTestEntity(entityId, "Original"));
            await seedContext.SaveChangesAsync();
        }

        // Dos "sesiones" concurrentes: cada scope tiene su propio DbContext/ChangeTracker, como dos
        // requests HTTP distintos que cargaron la misma fila antes de que ninguna la guardara.
        await using var scopeA = provider.CreateAsyncScope();
        await using var scopeB = provider.CreateAsyncScope();

        var repositoryA = scopeA.ServiceProvider.GetRequiredService<IRepository<ConcurrentTestEntity, Guid>>();
        var repositoryB = scopeB.ServiceProvider.GetRequiredService<IRepository<ConcurrentTestEntity, Guid>>();

        // La sesión B carga la fila (RowVersion original) ANTES de que A la modifique y guarde. EF
        // Core no refresca una entidad ya trackeada en una consulta posterior dentro del mismo
        // DbContext, así que el RowVersion en memoria de B sigue siendo el original incluso después
        // de que A haya actualizado la fila en la base de datos.
        var entityLoadedByB = await repositoryB.GetByIdAsync(entityId);
        entityLoadedByB.Should().NotBeNull();

        var senderA = scopeA.ServiceProvider.GetRequiredService<ISender>();
        var resultA = await senderA.Send(new RenameConcurrentEntityCommand(entityId, "Editado por A"));

        resultA.IsSuccess.Should().BeTrue("la primera edición no tiene ningún conflicto de concurrencia");

        entityLoadedByB!.Name = "Editado por B";
        repositoryB.Update(entityLoadedByB);

        var senderB = scopeB.ServiceProvider.GetRequiredService<ISender>();

        // "Manejo uniforme": el conflicto de concurrencia debe llegar como Result.Failure, nunca como
        // una excepción sin controlar propagada al llamador del pipeline — si TransactionBehavior no
        // capturara ConcurrencyConflictException, este await propagaría la excepción y el test
        // fallaría aquí en vez de en las aserciones de abajo.
        var resultB = await senderB.Send(new NoOpCommand());

        resultB.IsFailure.Should().BeTrue();
        resultB.Error.Code.Should().Be(ConcurrencyError.Code);
        resultB.Error.Type.Should().Be(ErrorType.Conflict, "un conflicto de concurrencia optimista debe mapear a HTTP 409, no a 400/500");

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        var persisted = await verificationContext.ConcurrentTestEntities
            .IgnoreQueryFilters()
            .FirstAsync(e => e.Id == entityId);

        persisted.Name.Should().Be(
            "Editado por A",
            "el cambio de la sesión B nunca debió persistirse: su RowVersion quedó obsoleto en cuanto A guardó primero");
    }
}

/// <summary>
/// Comando de prueba que carga la entidad por Id y renombra: existe únicamente para forzar, a través
/// del pipeline completo, un SaveChangesAsync real sobre la fila que otra sesión ya modificó.
/// </summary>
public record RenameConcurrentEntityCommand(Guid Id, string NewName) : ICommand;

public class RenameConcurrentEntityCommandHandler(IRepository<ConcurrentTestEntity, Guid> repository)
    : IRequestHandler<RenameConcurrentEntityCommand, Result>
{
    public async Task<Result> Handle(RenameConcurrentEntityCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(request.Id, cancellationToken);

        if (entity is null)
        {
            return Result.Failure(Error.NotFound("ConcurrentTestEntity.NotFound", "No se encontró la entidad."));
        }

        entity.Name = request.NewName;
        repository.Update(entity);

        return Result.Success();
    }
}

/// <summary>
/// Comando sin efecto propio: existe para disparar el SaveChangesAsync de TransactionBehavior sobre
/// los cambios que el test ya dejó trackeados manualmente en el DbContext de la sesión B, simulando
/// el "guardar" de una edición concurrente cuya carga ocurrió antes que la de la sesión A.
/// </summary>
public record NoOpCommand : ICommand;

public class NoOpCommandHandler : IRequestHandler<NoOpCommand, Result>
{
    public Task<Result> Handle(NoOpCommand request, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success());
}
