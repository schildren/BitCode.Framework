using System.Text;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
        services.AddScoped<IPermissionService, PermissionService<TUser, TRole>>();

        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
        services.AddAuthorization();

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
