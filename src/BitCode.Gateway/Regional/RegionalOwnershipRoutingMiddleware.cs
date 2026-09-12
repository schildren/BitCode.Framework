using BitCode.Framework.Shared.Domain.MultiTenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BitCode.Gateway.Regional;

/// <summary>
/// "Política global" de routing regional del Gateway (F5-03, Fase 5 — Disaster Recovery y
/// multi-región): antes de proxyar, compara la región propietaria de escritura del tenant del request
/// (<see cref="IRegionalOwnershipResolver"/>, F5-02) contra la región en la que corre ESTA instancia
/// del Gateway (<see cref="ICurrentRegionProvider"/>) y rechaza el request si no coinciden -- en vez de
/// dejarlo llegar al backend, que lo procesaría (o lo rechazaría recién en
/// <c>RegionalOwnershipBehavior</c>, Shared.Application) en la región equivocada.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rechazo, no redirect.</b> Un redirect HTTP real (307) hacia la región propietaria requeriría
/// conocer la URL pública de esa región (p. ej. un Gateway regional detrás de un DNS/anycast por
/// región), infraestructura que no existe en este repositorio (un solo host de desarrollo, ver
/// <c>docs/bia-fase5.md</c>/<c>docs/mapa-ownership-regional.md</c>). Se responde <c>421 Misdirected
/// Request</c> (semántica HTTP estándar para "este servidor no puede producir una respuesta para la
/// combinación de esquema/autoridad del request" -- aplicable acá como "esta instancia no es la
/// propietaria del tenant solicitado") con un <see cref="ProblemDetails"/> que indica la región
/// correcta en el cuerpo y en el header <c>X-BitCode-Owner-Region</c>, para que un cliente/orquestador
/// que sí conozca el mapa de URLs por región (fuera del alcance de este framework) pueda reintentar
/// contra el Gateway correcto -- eso es lo que este documento llama "failover controlado": la
/// redirección es explícita y basada en el ownership vigente, nunca un procesamiento silencioso en la
/// región incorrecta.
/// </para>
/// <para>
/// <b>Cero cambio de comportamiento para el caso hoy real</b> (un solo host/región): si el request es
/// anónimo, no tiene el claim <c>tenant_id</c>, o la región propietaria coincide con la región de esta
/// instancia (el caso de <see cref="RegionId.Primary"/> en todo despliegue de una sola región), este
/// middleware nunca rechaza nada -- mismo criterio que <c>RegionalOwnershipBehavior</c>
/// (Shared.Application, F5-02).
/// </para>
/// <para>
/// Debe registrarse DESPUÉS de <c>UseAuthentication</c>/<c>UseAuthorization</c> (necesita el claim
/// <c>tenant_id</c> del usuario ya autenticado, nunca un header/query string que el cliente controle
/// directamente sin pasar por autenticación -- <c>docs/threat-model.md</c>, hallazgo S2) y ANTES de
/// <c>MapReverseProxy</c> (para no proxyar un request que se va a rechazar).
/// </para>
/// </remarks>
public sealed class RegionalOwnershipRoutingMiddleware(
    RequestDelegate next,
    IRegionalOwnershipResolver ownershipResolver,
    ICurrentRegionProvider currentRegionProvider,
    ILogger<RegionalOwnershipRoutingMiddleware> logger)
{
    public const string OwnerRegionHeaderName = "X-BitCode-Owner-Region";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            // Request anónimo: no hay tenant que resolver -- mismo criterio que HttpContextTenantProvider
            // (Shared.Infrastructure.Web). Si el endpoint requiere autenticación, auth ya rechazó antes.
            await next(context);
            return;
        }

        var claim = context.User.FindFirst(TenantClaimTypes.TenantId);
        if (claim is null || !Guid.TryParse(claim.Value, out var tenantId))
        {
            // Sin tenant identificado no hay ownership regional que validar (usuario sin tenant
            // asignado, o proyecto de un solo tenant) -- se deja pasar, igual que
            // RegionalOwnershipBehavior cuando ITenantContext.TenantId es null.
            await next(context);
            return;
        }

        var ownerRegion = await ownershipResolver.ResolveOwnerRegionAsync(tenantId, context.RequestAborted);
        var currentRegion = currentRegionProvider.CurrentRegion;

        if (ownerRegion != currentRegion)
        {
            logger.LogWarning(
                "Request {Method} {Path} para el tenant {TenantId} rechazado: la región propietaria " +
                "es '{OwnerRegion}' pero esta instancia del Gateway corre en '{CurrentRegion}'",
                context.Request.Method,
                context.Request.Path,
                tenantId,
                ownerRegion,
                currentRegion);

            context.Response.Headers[OwnerRegionHeaderName] = ownerRegion.Value;
            context.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status421MisdirectedRequest,
                Title = "Región incorrecta para el tenant solicitado",
                Detail = $"El tenant '{tenantId}' tiene como región propietaria de escritura " +
                    $"'{ownerRegion.Value}', pero esta instancia del Gateway corre en " +
                    $"'{currentRegion.Value}'. Reintentar contra el Gateway de la región propietaria " +
                    $"(ver header '{OwnerRegionHeaderName}').",
                Type = "https://bitcode.dev/problems/region-misdirected-request",
            });
            return;
        }

        await next(context);
    }
}
