using BitCode.Framework.Platform.Identity.Abac;
using BitCode.Framework.Platform.Identity.Actors;
using BitCode.Framework.Platform.Identity.HealthChecks;
using BitCode.Framework.Platform.Identity.Sessions;
using BitCode.Framework.Shared.Domain.Idempotency;
using BitCode.Framework.Shared.Domain.Inbox;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Domain.Security;
using BitCode.Framework.Shared.Infrastructure.Persistence;
using BitCode.Framework.Shared.Infrastructure.Persistence.Idempotency;
using BitCode.Framework.Shared.Infrastructure.Persistence.Inbox;
using BitCode.Framework.Shared.Infrastructure.Persistence.Interceptors;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.Security;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Platform.Identity;

/// <summary>
/// Envuelve el cableado de infraestructura del módulo Identity Administration (Fase 6, módulo 1) --
/// mismo espíritu que <c>InfrastructureModule</c> de un proyecto consumidor (docs/convenciones.md):
/// agrupa <c>AddSharedX&lt;T&gt;()</c> del framework para que el resto del host no repita el cableado.
/// </summary>
public static class IdentityAdministrationServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="IdentityAdministrationDbContext"/> sobre SQL Server con los mismos
    /// interceptores de auditoría/soft-delete/tenant/outbox que <c>AddSharedPersistence</c> usa para un
    /// <c>MultiTenantDbContext</c> (F1-12/F2-15/F1-23) -- necesario porque
    /// <see cref="IdentityAdministrationDbContext"/> hereda de <c>MultiTenantIdentityDbContext&lt;,&gt;</c>,
    /// no de <c>MultiTenantDbContext</c>, así que <c>AddSharedPersistence&lt;TContext&gt;</c> (acotado por
    /// genéricos a ese último) no aplica. Llamar ANTES de
    /// <c>AddSharedSecurity&lt;ApplicationUser,ApplicationRole,IdentityAdministrationDbContext&gt;</c> --
    /// mismo orden que <c>AddDbContext</c> antes de <c>AddSharedSecurity</c> en
    /// <c>SecurityEndToEndTests</c>. Un proyecto multi-tenant real llama
    /// <c>services.AddHttpContextTenantProvider()</c> (Shared.Infrastructure.Web) ANTES de este método,
    /// igual que con <c>AddSharedPersistence</c> (F1-12).
    /// </summary>
    public static IServiceCollection AddSharedIdentityAdministrationPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.TryAddScoped<ITenantProvider, NullTenantProvider>();
        services.TryAddScoped<ICurrentUserProvider, NullCurrentUserProvider>();
        services.TryAddScoped<ITenantContext, TenantContext>();

        services.AddScoped<AuditableEntitySaveChangesInterceptor>();
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<TenantSaveChangesInterceptor>();
        services.AddScoped<OutboxSaveChangesInterceptor>();

        services.AddDbContext<IdentityAdministrationDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString);
            options.AddInterceptors(
                sp.GetRequiredService<AuditableEntitySaveChangesInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<TenantSaveChangesInterceptor>(),
                sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });

        // TransactionBehavior (Shared.Application) resuelve IUnitOfWork para CUALQUIER ICommand del
        // host, no solo los de este módulo -- se registra acá para que el pipeline de MediatR pueda
        // resolverlo incluso si el host no llama a AddSharedPersistence para ningún otro
        // MultiTenantDbContext propio. El SaveChangesAsync final de un comando simple (regla dura 1,
        // docs/convenciones.md) es, en la práctica, un flush sin cambios pendientes para los comandos
        // de este módulo: UserManager/RoleManager ya persisten sus propias mutaciones internamente.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<IdentityAdministrationDbContext>());
        services.AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<IdentityAdministrationDbContext>()));

        // F1-22: idempotencia de las mutaciones expuestas por este módulo (CrearUsuarioCommand,
        // AsignarRolAUsuarioCommand, RevocarSesionCommand, etc.) -- la tabla IdempotencyKeys ya está
        // configurada por MultiTenantIdentityDbContext.OnModelCreating.
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();

        // AddSharedApplication (Shared.Application) registra incondicionalmente IInboxMessageProcessor
        // (F1-24), que requiere IInboxStore -- aunque este primer corte del módulo no consume ningún
        // IIntegrationEvent todavía (ver el pendiente explícito sobre eventos en
        // docs/guia-identity-administration.md), sin este registro el host ni siquiera arranca.
        services.AddScoped<IInboxStore, EfInboxStore>();

        services.AddHealthChecks()
            .AddCheck<IdentityAdministrationDbContextHealthCheck>("sql-server-identity-administration", tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Registra los servicios propios de aplicación del módulo (resolución de actor para
    /// auditoría/ABAC, la regla ABAC de no-autoasignación de roles y el store de sesiones). No registra
    /// <c>AddSharedSecurity</c>/<c>AddSharedAbacAuthorization</c>/<c>AddSharedAuditing</c> por su cuenta
    /// -- son responsabilidad explícita del host consumidor (mismo criterio que
    /// <c>AddSharedPrivilegedOperationsPolicies</c> exige <c>AddSharedAbacAuthorization</c> ya llamado,
    /// F2-10): este módulo depende de que existan, no decide en qué orden ni con qué opciones un host
    /// las configura. Ver <c>docs/guia-identity-administration.md</c>, sección "Orden de registro".
    /// </summary>
    public static IServiceCollection AddSharedIdentityAdministration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // El módulo es HTTP por diseño en este primer corte (ver el remarks de
        // IIdentityAdministrationActorContext): no hay todavía un consumidor no-HTTP (job, seeder) que
        // justifique una variante Null/System configurable como ITenantProvider/NullTenantProvider.
        services.AddHttpContextAccessor();
        services.TryAddScoped<IIdentityAdministrationActorContext, HttpContextIdentityAdministrationActorContext>();

        services.AddScoped<IUserSessionStore, EfUserSessionStore>();

        // Se suma a la colección de IAbacRule ya registrada por AddSharedAbacAuthorization (F2-08) --
        // TryAddEnumerable es independiente del orden de llamada entre ambos métodos.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAbacRule, SelfRoleAssignmentAbacRule>());

        return services;
    }
}
