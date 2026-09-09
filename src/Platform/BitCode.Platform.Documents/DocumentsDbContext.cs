using BitCode.Framework.Platform.Documents.Documentos;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.Documents;

/// <summary>
/// Dueño exclusivo del esquema del módulo Documents (Fase 6, módulo 5 del Plan Maestro): tablas
/// <c>Documentos</c>, <c>DocumentoVersiones</c>, más
/// <c>IdempotencyKeys</c>/<c>OutboxMessages</c>/<c>InboxMessages</c> (configuradas automáticamente por
/// <see cref="MultiTenantDbContext"/>). Ningún otro módulo de plataforma debe leer/escribir estas tablas
/// directamente -- la única superficie pública para consultarlas/mutarlas son los contratos de este
/// módulo (comandos/queries mapeados por <c>DocumentsEndpointRouteBuilderExtensions</c>), ver
/// <c>docs/guia-documents.md</c>.
/// </summary>
public sealed class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<Documento> Documentos => Set<Documento>();

    public DbSet<DocumentoVersion> DocumentoVersiones => Set<DocumentoVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Documento>(builder =>
        {
            builder.Property(d => d.Titulo).HasMaxLength(256).IsRequired();
            builder.Property(d => d.Descripcion).HasMaxLength(1000);
            builder.Property(d => d.Clasificacion).HasMaxLength(128).IsRequired();
            builder.HasIndex(d => new { d.TenantId, d.Clasificacion });
        });

        modelBuilder.Entity<DocumentoVersion>(builder =>
        {
            builder.Property(v => v.NombreArchivo).HasMaxLength(256).IsRequired();
            builder.Property(v => v.ContentType).HasMaxLength(128).IsRequired();
            builder.Property(v => v.HashSha256).HasMaxLength(64).IsRequired();
            builder.Property(v => v.BlobKey).HasMaxLength(512).IsRequired();
            builder.HasIndex(v => v.DocumentoId);
            // Guardrail de datos contra la condición de carrera de SubirVersionDocumentoCommandHandler
            // (ver ese archivo, mismo criterio documentado en Feature Management/Catalogs para el mismo
            // tipo de check-then-act sobre un número incremental).
            builder.HasIndex(v => new { v.DocumentoId, v.Numero }).IsUnique();
        });
    }
}
