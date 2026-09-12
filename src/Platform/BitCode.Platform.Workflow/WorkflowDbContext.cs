using BitCode.Framework.Platform.Workflow.Definiciones;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.Workflow;

/// <summary>
/// Dueño exclusivo del esquema del módulo Workflow (Fase 6, módulo 6 del Plan Maestro): tablas
/// <c>WorkflowDefinitions</c>, <c>WorkflowVersiones</c>, <c>WorkflowStates</c>, <c>WorkflowTransitions</c>,
/// <c>WorkflowInstances</c>, <c>WorkflowTasks</c>, <c>WorkflowHistoriales</c>, más
/// <c>IdempotencyKeys</c>/<c>OutboxMessages</c>/<c>InboxMessages</c> (configuradas automáticamente por
/// <see cref="MultiTenantDbContext"/>). Ningún otro módulo de plataforma debe leer/escribir estas tablas
/// directamente -- la única superficie pública para consultarlas/mutarlas son los contratos de este
/// módulo (comandos/queries mapeados por <c>WorkflowEndpointRouteBuilderExtensions</c>), ver
/// <c>docs/guia-workflow.md</c>.
/// </summary>
public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<WorkflowDefinition> WorkflowDefiniciones => Set<WorkflowDefinition>();

    public DbSet<WorkflowVersion> WorkflowVersiones => Set<WorkflowVersion>();

    public DbSet<WorkflowState> WorkflowStates => Set<WorkflowState>();

    public DbSet<WorkflowTransition> WorkflowTransitions => Set<WorkflowTransition>();

    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();

    public DbSet<WorkflowTask> WorkflowTasks => Set<WorkflowTask>();

    public DbSet<WorkflowHistorial> WorkflowHistoriales => Set<WorkflowHistorial>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WorkflowDefinition>(builder =>
        {
            builder.Property(d => d.Codigo).HasMaxLength(64).IsRequired();
            builder.Property(d => d.Nombre).HasMaxLength(200).IsRequired();
            builder.Property(d => d.Descripcion).HasMaxLength(500);
            builder.HasIndex(d => new { d.TenantId, d.Codigo }).IsUnique();
        });

        modelBuilder.Entity<WorkflowVersion>(builder =>
        {
            builder.HasIndex(v => v.WorkflowDefinitionId);
            builder.HasIndex(v => new { v.WorkflowDefinitionId, v.Numero }).IsUnique();
        });

        modelBuilder.Entity<WorkflowState>(builder =>
        {
            builder.Property(s => s.Codigo).HasMaxLength(64).IsRequired();
            builder.Property(s => s.Nombre).HasMaxLength(200).IsRequired();
            builder.Property(s => s.TituloTarea).HasMaxLength(200);
            builder.HasIndex(s => s.WorkflowVersionId);
            builder.HasIndex(s => new { s.WorkflowVersionId, s.Codigo }).IsUnique();
        });

        modelBuilder.Entity<WorkflowTransition>(builder =>
        {
            builder.Property(t => t.Accion).HasMaxLength(64).IsRequired();
            builder.Property(t => t.ReglaExpresion).HasMaxLength(500);
            builder.HasIndex(t => t.WorkflowVersionId);
            builder.HasIndex(t => t.DesdeEstadoId);
        });

        modelBuilder.Entity<WorkflowInstance>(builder =>
        {
            builder.HasIndex(i => i.WorkflowVersionId);
            builder.HasIndex(i => i.WorkflowDefinitionId);
            builder.HasIndex(i => i.EstadoActualId);
        });

        modelBuilder.Entity<WorkflowTask>(builder =>
        {
            builder.Property(t => t.Titulo).HasMaxLength(200).IsRequired();
            builder.Property(t => t.Comentario).HasMaxLength(1000);
            builder.Property(t => t.AccionResuelta).HasMaxLength(64);
            builder.HasIndex(t => t.WorkflowInstanceId);
            builder.HasIndex(t => t.AsignadoAUserId);
            // Consultada por WorkflowEscalamientoJob (regla dura 5: nunca IQueryable, pero sigue
            // ganando la especificación con un índice que cubra su filtro).
            builder.HasIndex(t => new { t.Estado, t.Escalada, t.SlaVencimientoUtc });
        });

        modelBuilder.Entity<WorkflowHistorial>(builder =>
        {
            builder.Property(h => h.TipoEvento).HasMaxLength(64).IsRequired();
            builder.Property(h => h.Detalle).HasMaxLength(1000).IsRequired();
            builder.HasIndex(h => h.WorkflowInstanceId);
        });
    }
}
