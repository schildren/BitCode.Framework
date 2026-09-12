using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BitCode.Framework.Shared.Infrastructure.Security.Jwt;

/// <summary>
/// Registra ÚNICAMENTE el esquema de autenticación JWT Bearer (validación de firma/issuer/audience/
/// vigencia contra <see cref="JwtOptions"/>, sección de configuración "Jwt") — sin ASP.NET Core
/// Identity, sin <c>UserManager</c>/<c>RoleManager</c> ni ningún <c>DbContext</c>. Extraído de
/// <see cref="SecurityServiceCollectionExtensions.AddSharedSecurity{TUser,TRole,TContext}"/> (que
/// delega en este método para no duplicar la construcción de <see cref="TokenValidationParameters"/>)
/// para que un host que solo necesita validar tokens ya emitidos — como <c>BitCode.Gateway</c> (F4-08),
/// que actúa como "auth boundary" antes de reenviar el request a un backend, sin poseer usuarios/roles
/// propios — pueda registrar el mismo esquema de autenticación sin arrastrar Identity/EF Core.
/// </summary>
public static class JwtBearerAuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// <c>services.AddAuthentication().AddJwtBearer(...)</c> con <see cref="TokenValidationParameters"/>
    /// resueltos desde la sección "Jwt" (<see cref="JwtOptions.SectionName"/>) — mismo criterio de
    /// validación (issuer/audience/firma HMAC/vigencia, sin tolerancia de reloj) que
    /// <see cref="SecurityServiceCollectionExtensions.AddSharedSecurity{TUser,TRole,TContext}"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Falta la sección de configuración "Jwt" (SecretKey/Issuer/Audience).
    /// </exception>
    public static IServiceCollection AddSharedJwtBearerAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<JwtBearerOptions>? configureJwtBearer = null)
    {
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        services.Configure<JwtOptions>(jwtSection);
        var jwtOptions = jwtSection.Get<JwtOptions>()
            ?? throw new InvalidOperationException(
                $"Falta la sección de configuración '{JwtOptions.SectionName}' (SecretKey/Issuer/Audience).");

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

                configureJwtBearer?.Invoke(options);
            });

        return services;
    }
}
