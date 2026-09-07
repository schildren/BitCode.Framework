using BitCode.Framework.Shared.Domain.Outbox;
using BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.DeadLetter;
using BitCode.Framework.Shared.Kernel;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.DeadLetter;

/// <summary>
/// F3-08 (DLQ): <see cref="OutboxDeadLetterReprocessor"/> contra SQL Server real -- el criterio de
/// "auditar primero, mutar después" y los casos de rechazo (fila inexistente / no agotada) no dependen
/// de Kafka, así que esta clase no necesita <c>KafkaContainerFixture</c> (ver
/// <c>OutboxDeadLetterIntegrationTests</c>, Shared.Infrastructure.Persistence.Tests, para el flujo
/// completo con broker real).
/// </summary>
[Collection(BitCode.Framework.Shared.Infrastructure.Security.Tests.Integration.SqlServerCollection.Name)]
public class OutboxDeadLetterReprocessorTests(SqlServerContainerFixture sqlFixture)
{
    private string BuildIsolatedConnectionString([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        sqlFixture.BuildIsolatedConnectionString("SecDlq", testName);

    private static async Task<DeadLetterTestDbContext> CreateContextAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DeadLetterTestDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        var context = new DeadLetterTestDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static async Task<OutboxMessage> SeedExhaustedMessageAsync(DeadLetterTestDbContext context)
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            EventType = "Tests.SomeEvent",
            PayloadJson = "{}",
            OccurredAtUtc = DateTime.UtcNow,
            RetryCount = 5,
            Error = "Fallo simulado previo al reprocesamiento.",
            ExhaustedAtUtc = DateTime.UtcNow,
        };

        context.Set<OutboxMessage>().Add(message);
        await context.SaveChangesAsync();
        return message;
    }

    [Fact]
    public async Task ReprocessAsync_ExhaustedMessage_ReopensRowAndAudits()
    {
        var connectionString = BuildIsolatedConnectionString();
        await using var context = await CreateContextAsync(connectionString);
        var message = await SeedExhaustedMessageAsync(context);

        var auditWriter = new InMemoryAuditWriter();
        var reprocessor = new OutboxDeadLetterReprocessor(context, auditWriter, NullLogger<OutboxDeadLetterReprocessor>.Instance);

        var result = await reprocessor.ReprocessAsync(new DeadLetterReprocessRequest(
            message.Id,
            new AuditActor("operador-1", AuditActorType.User),
            "Causa raíz corregida (prueba unitaria)."));

        result.IsSuccess.Should().BeTrue();
        result.Value.OutboxMessageId.Should().Be(message.Id);
        result.Value.NewRetryCount.Should().Be(0);

        var persisted = await context.Set<OutboxMessage>().IgnoreQueryFilters()
            .FirstAsync(m => m.Id == message.Id);
        persisted.ExhaustedAtUtc.Should().BeNull();
        persisted.Error.Should().BeNull();
        persisted.RetryCount.Should().Be(0);

        var auditEntry = auditWriter.Entries.Should().ContainSingle().Subject;
        auditEntry.Action.Should().Be("OutboxMessage.DeadLetterReprocess");
        auditEntry.Resource.Type.Should().Be("OutboxMessage");
        auditEntry.Resource.Id.Should().Be(message.Id.ToString());
        auditEntry.Outcome.Should().Be(AuditOutcome.Success);
        auditEntry.Actor.Id.Should().Be("operador-1");
        auditEntry.Reason.Should().Be("Causa raíz corregida (prueba unitaria).");
        auditEntry.Id.Should().Be(result.Value.AuditEntryId);
    }

    [Fact]
    public async Task ReprocessAsync_MessageNotFound_ReturnsNotFoundAndDoesNotAudit()
    {
        var connectionString = BuildIsolatedConnectionString();
        await using var context = await CreateContextAsync(connectionString);

        var auditWriter = new InMemoryAuditWriter();
        var reprocessor = new OutboxDeadLetterReprocessor(context, auditWriter, NullLogger<OutboxDeadLetterReprocessor>.Instance);

        var result = await reprocessor.ReprocessAsync(new DeadLetterReprocessRequest(
            Guid.NewGuid(),
            new AuditActor("operador-1", AuditActorType.User),
            "No debería aplicar."));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DeadLetter.OutboxMessageNotFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);
        auditWriter.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReprocessAsync_MessageNotExhausted_ReturnsConflictAndDoesNotMutate()
    {
        var connectionString = BuildIsolatedConnectionString();
        await using var context = await CreateContextAsync(connectionString);

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            EventType = "Tests.SomeEvent",
            PayloadJson = "{}",
            OccurredAtUtc = DateTime.UtcNow,
        };
        context.Set<OutboxMessage>().Add(message);
        await context.SaveChangesAsync();

        var auditWriter = new InMemoryAuditWriter();
        var reprocessor = new OutboxDeadLetterReprocessor(context, auditWriter, NullLogger<OutboxDeadLetterReprocessor>.Instance);

        var result = await reprocessor.ReprocessAsync(new DeadLetterReprocessRequest(
            message.Id,
            new AuditActor("operador-1", AuditActorType.User),
            "No debería aplicar -- la fila nunca se agotó."));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DeadLetter.NotExhausted");
        auditWriter.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReprocessAsync_AuditWriteFails_NeverReopensRow()
    {
        var connectionString = BuildIsolatedConnectionString();
        await using var context = await CreateContextAsync(connectionString);
        var message = await SeedExhaustedMessageAsync(context);

        var reprocessor = new OutboxDeadLetterReprocessor(context, new AlwaysFailingAuditWriter(), NullLogger<OutboxDeadLetterReprocessor>.Instance);

        var result = await reprocessor.ReprocessAsync(new DeadLetterReprocessRequest(
            message.Id,
            new AuditActor("operador-1", AuditActorType.User),
            "No debería aplicar -- la auditoría falla."));

        result.IsFailure.Should().BeTrue();

        var persisted = await context.Set<OutboxMessage>().IgnoreQueryFilters()
            .FirstAsync(m => m.Id == message.Id);
        persisted.ExhaustedAtUtc.Should().NotBeNull("un fallo al auditar nunca debe reabrir la fila -- no puede existir un reprocesamiento sin su auditoría (F3-08)");
        persisted.RetryCount.Should().Be(5, "el estado original de la fila queda intacto si la auditoría falla");
    }
}

/// <summary>DbContext mínimo con <see cref="OutboxMessage"/> registrado, para probar <see cref="OutboxDeadLetterReprocessor"/> sin necesitar el contexto de Identity completo.</summary>
public sealed class DeadLetterTestDbContext(DbContextOptions<DeadLetterTestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        OutboxModelConfigurator.Configure(modelBuilder);
    }
}

/// <summary><see cref="IAuditWriter"/> de prueba que siempre falla -- verifica el orden "auditar primero, mutar después".</summary>
public sealed class AlwaysFailingAuditWriter : IAuditWriter
{
    public Task<Result<AuditEntry>> WriteAsync(AuditEntryRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<AuditEntry>(Error.Failure("Audit.SimulatedFailure", "Fallo simulado de auditoría (prueba).")));
}
