using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Application.Regions;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BitCode.Framework.Shared.Application.Behaviors;

/// <summary>
/// Da comportamiento real al marcador <see cref="IRegionalCommand"/> (F5-02, Fase 5 — Disaster
/// Recovery y multi-región): un comando marcado solo se ejecuta si esta instancia corre en la región
/// propietaria de escritura (single-writer) del tenant actual; en caso contrario se rechaza antes de
/// tocar cualquier estado, sin ejecutar el handler.
/// </summary>
/// <remarks>
/// Posición en el pipeline (<c>AddSharedApplication</c>): Logging -&gt; Validation -&gt;
/// RegionalOwnership -&gt; Transaction -&gt; Idempotency -&gt; Handler. Deliberadamente ANTES de
/// <see cref="TransactionBehavior{TRequest,TResponse}"/> e <see cref="IdempotencyBehavior{TRequest,TResponse}"/>:
/// un comando ejecutado fuera de su región propietaria no debe abrir ninguna transacción de base de
/// datos ni tocar ningún store de idempotencia (que en un despliegue multi-región real también estaría
/// particionado/replicado por región) — el rechazo debe ser el primer efecto observable, antes de
/// cualquier otro trabajo.
/// <para/>
/// Si la multi-tenancy está deshabilitada, o el <c>TenantId</c> del request actual no se pudo
/// resolver (<see cref="ITenantContext.TenantId"/> es <see langword="null"/>), este behavior no
/// rechaza nada: sin un tenant identificado no hay ownership regional que validar, y un despliegue de
/// un solo tenant/región no debe ver ningún cambio de comportamiento. Igual criterio para
/// <see cref="RegionId.Primary"/>: si <see cref="ICurrentRegionProvider.CurrentRegion"/> es la misma
/// región resuelta como propietaria (el caso de todo despliegue de una sola región, ver
/// <c>docs/mapa-ownership-regional.md</c>), la comparación siempre coincide y ningún comando se
/// rechaza — cero cambio de comportamiento para el caso común hoy real en este repositorio.
/// </remarks>
public class RegionalOwnershipBehavior<TRequest, TResponse>(
    ITenantContext tenantContext,
    IRegionalOwnershipResolver ownershipResolver,
    ICurrentRegionProvider currentRegionProvider,
    ILogger<RegionalOwnershipBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRegionalCommand, IRequest<TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!tenantContext.IsMultiTenancyEnabled || tenantContext.TenantId is not { } tenantId)
        {
            return await next();
        }

        var ownerRegion = await ownershipResolver.ResolveOwnerRegionAsync(tenantId, cancellationToken);
        var currentRegion = currentRegionProvider.CurrentRegion;

        if (ownerRegion != currentRegion)
        {
            logger.LogWarning(
                "{RequestName} para el tenant {TenantId} se rechazó: la región propietaria es " +
                "'{OwnerRegion}' pero esta instancia corre en '{CurrentRegion}'",
                typeof(TRequest).Name,
                tenantId,
                ownerRegion,
                currentRegion);
            return CreateFailureResult<TResponse>(
                RegionalOwnershipErrors.WrongRegion(tenantId, ownerRegion.Value, currentRegion.Value));
        }

        return await next();
    }

    /// <summary>
    /// Mismo patrón de reflexión que <c>IdempotencyBehavior</c>/<c>TransactionBehavior</c>: construye
    /// un <c>Result.Failure</c>/<c>Result&lt;TValue&gt;.Failure</c> del tipo concreto
    /// <typeparamref name="TResult"/>, que puede ser <c>Result</c> o <c>Result&lt;TValue&gt;</c> según
    /// el comando.
    /// </summary>
    private static TResult CreateFailureResult<TResult>(Error error)
        where TResult : Result
    {
        if (typeof(TResult) == typeof(Result))
        {
            return (TResult)(object)Result.Failure(error);
        }

        var valueType = typeof(TResult).GetGenericArguments()[0];

        var failureMethod = typeof(Result)
            .GetMethods()
            .Single(m => m.Name == nameof(Result.Failure) && m.IsGenericMethod)
            .MakeGenericMethod(valueType);

        return (TResult)failureMethod.Invoke(null, [error])!;
    }
}
