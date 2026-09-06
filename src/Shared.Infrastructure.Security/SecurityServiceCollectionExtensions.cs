using System.Text;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BitCode.Framework.Shared.Infrastructure.Security;

public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Registra ASP.NET Core Identity (UserManager/RoleManager sobre TContext), autenticación JWT
    /// Bearer, el sistema de permisos (Tarea 3.3) y policy-based authorization dinámica por
    /// permiso (Tarea 3.4). La sección de configuración "Jwt" debe existir (JwtOptions.SectionName)
    /// con SecretKey/Issuer/Audience.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Falta la sección de configuración "Jwt", o (F2-09) <c>AddSharedPermissionCache</c> ya fue llamado
    /// antes que este método -- ver <c>docs/guia-rbac-2.md</c>, sección de orden de registro, y el mensaje
    /// de la excepción para el detalle de por qué ese orden dejaría el cache de permisos sin efecto.
    /// </exception>
    public static IServiceCollection AddSharedSecurity<TUser, TRole, TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TUser : ApplicationUser
        where TRole : ApplicationRole
        where TContext : MultiTenantIdentityDbContext<TUser, TRole>
    {
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        services.Configure<JwtOptions>(jwtSection);
        var jwtOptions = jwtSection.Get<JwtOptions>()
            ?? throw new InvalidOperationException(
                $"Falta la sección de configuración '{JwtOptions.SectionName}' (SecretKey/Issuer/Audience).");

        services
            .AddIdentityCore<TUser>()
            .AddRoles<TRole>()
            .AddEntityFrameworkStores<TContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        // F2-07 (RBAC 2.0): AddSharedPermissionEvaluation registra primero el fallback
        // NullPermissionService (TryAddScoped); el AddScoped explícito de abajo, con Identity real,
        // se agrega después y gana la resolución (último registro para un mismo tipo de servicio).
        services.AddSharedPermissionEvaluation();

        // Fix post-revisión de arquitectura de F2-09: si AddSharedPermissionCache ya decoró
        // IPermissionService (Replace sobre NullPermissionService) ANTES de esta llamada, el AddScoped de
        // abajo agregaría un nuevo descriptor de IPermissionService que gana la resolución sobre el
        // Replace anterior -- el decorador de cache queda huérfano, sin ninguna excepción en el arranque,
        // silenciosamente sin efecto (IPermissionService resolvería PermissionService<TUser,TRole> sin
        // cache). Se detecta y se rechaza acá, no en AddSharedPermissionCache, porque ahí (llamado antes
        // de este método) es indistinguible del caso legítimo de un proyecto solo-OIDC cacheando un
        // NullPermissionService que nunca se reemplaza -- ver PermissionCacheServiceCollectionExtensions.
        if (services.IsPermissionCacheAlreadyApplied())
        {
            throw new InvalidOperationException(
                "AddSharedSecurity debe llamarse ANTES de AddSharedPermissionCache, no después. Se " +
                "detectó que AddSharedPermissionCache ya decoró IPermissionService (probablemente sobre " +
                "NullPermissionService, registrado por una llamada previa a AddSharedPermissionEvaluation " +
                "o AddSharedAbacAuthorization) antes de que AddSharedSecurity registrara la implementación " +
                "real de RBAC (PermissionService<TUser, TRole>). Si continuara, el decorador de cache " +
                "quedaría huérfano sin ningún error visible: IPermissionService resolvería " +
                "PermissionService<TUser, TRole> sin cache. Reordená las llamadas: AddSharedSecurity(...) " +
                "antes de AddSharedPermissionCache(...).");
        }

        services.AddScoped<IPermissionService, PermissionService<TUser, TRole>>();

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                };
            });

        return services;
    }
}
