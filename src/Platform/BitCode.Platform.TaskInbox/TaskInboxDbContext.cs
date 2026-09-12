using BitCode.Framework.Platform.TaskInbox.Bandeja;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.TaskInbox;

/// <summary>
/// Dueño exclusivo del esquema del módulo Task Inbox (Fase 6, módulo 7 del Plan Maestro): una única
/// tabla propia, <c>TaskInboxItems</c> (read-model de las tareas de Workflow, ver
/// <see cref="Bandeja.TaskInboxItem"/>), más <c>IdempotencyKeys</c>/<c>OutboxMessages</c>/
/// <c>InboxMessages</c> configuradas automáticamente por <see cref="MultiTenantDbContext"/> --
/// <c>InboxMessages</c> es, acá, el mecanismo real de deduplicación de los tres eventos de integración
/// de Workflow que este módulo consume (F1-24/F3-04, ver <c>docs/guia-inbox-consumer.md</c>). Ningún
/// otro módulo debe leer/escribir esta tabla directamente -- la única superficie pública son los
/// contratos de este módulo (<c>TaskInboxEndpointRouteBuilderExtensions</c>), ver
/// <c>docs/guia-taskinbox.md</c>.
/// </summary>
public sealed class TaskInboxDbContext(DbContextOptions<TaskInboxDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<TaskInboxItem> TaskInboxItems => Set<TaskInboxItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TaskInboxItem>(builder =>
        {
            builder.HasIndex(i => i.WorkflowInstanceId);
            // Consultada por ListarBandejaQuery: "mi bandeja" siempre filtra por asignado actual +
            // estado (regla dura 5: nunca IQueryable expuesto, pero la especificación sigue
            // beneficiándose de un índice que cubra su filtro más común).
            builder.HasIndex(i => new { i.AsignadoAUserId, i.Estado });
        });
    }
}
