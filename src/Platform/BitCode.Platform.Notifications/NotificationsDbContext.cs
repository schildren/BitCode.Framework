using BitCode.Framework.Platform.Notifications.Envio;
using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Platform.Notifications.Preferencias;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.Notifications;

/// <summary>
/// Dueño exclusivo del esquema del módulo Notifications (Fase 6, módulo 8 del Plan Maestro): cuatro
/// tablas propias (<c>NotificationTemplates</c>, <c>UserNotificationPreferences</c>, <c>Notifications</c>,
/// <c>NotificationDeliveries</c>), más <c>IdempotencyKeys</c>/<c>OutboxMessages</c>/<c>InboxMessages</c>
/// configuradas automáticamente por <see cref="MultiTenantDbContext"/> -- <c>InboxMessages</c> es, acá,
/// el mecanismo real de deduplicación del consumidor de ejemplo (<c>TareaAsignadaNotificationEventConsumer</c>),
/// mismo mecanismo que ya usa Task Inbox (F1-24/F3-04, ver <c>docs/guia-inbox-consumer.md</c>);
/// <c>OutboxMessages</c> es el mecanismo real de publicación de los dos eventos de integración propios de
/// este módulo (<see cref="NotificacionEnviadaIntegrationEvent"/>/<see cref="NotificacionFallidaIntegrationEvent"/>).
/// Ningún otro módulo debe leer/escribir esta tabla directamente -- la única superficie pública son los
/// contratos de este módulo, ver <c>docs/guia-notifications.md</c>.
/// </summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();

    public DbSet<UserNotificationPreference> UserNotificationPreferences => Set<UserNotificationPreference>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<NotificationTemplate>(builder =>
        {
            // Clave lógica de una plantilla -- consultada por NotificationSender en cada disparo (ver
            // NotificationTemplateActivaSpecification), y por CrearNotificationTemplateCommand para
            // rechazar duplicados (regla dura 6, docs/convenciones.md: unicidad reforzada también a
            // nivel de base de datos, no solo en el handler).
            builder.HasIndex(t => new { t.Codigo, t.Canal, t.Locale }).IsUnique();
        });

        modelBuilder.Entity<UserNotificationPreference>(builder =>
        {
            builder.HasIndex(p => new { p.UserId, p.CodigoPlantilla, p.Canal }).IsUnique();
        });

        modelBuilder.Entity<Notification>(builder =>
        {
            // Consultada por ListarMisNotificacionesQuery: "mis notificaciones" siempre filtra por
            // destinatario + estado (mismo criterio que TaskInboxItem).
            builder.HasIndex(n => new { n.DestinatarioUserId, n.Estado });

            // Consultada por NotificationRetryJob en cada disparo (IgnoreQueryFilters, cross-tenant) --
            // sin este índice, cada ciclo del job sería un table scan completo de la tabla.
            builder.HasIndex(n => new { n.Estado, n.ProximoReintentoUtc });
        });

        modelBuilder.Entity<NotificationDelivery>(builder =>
        {
            builder.HasIndex(d => d.NotificationId);
        });
    }
}
