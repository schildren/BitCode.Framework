using BitCode.Framework.Platform.FeatureManagement.Flags;
using BitCode.Framework.Platform.FeatureManagement.Rollouts;
using BitCode.Framework.Platform.FeatureManagement.Segmentos;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.FeatureManagement.Evaluacion;

/// <summary>
/// El servicio de evaluación real exigido por el Plan Maestro para este módulo -- no alcanza con el CRUD
/// de flags/segmentos/rollouts, tiene que existir una consulta que responda "¿está este flag activo PARA
/// ESTE CONTEXTO concreto?" (Fase 6, módulo Feature Management). Es una <see cref="IQuery{TResponse}"/>
/// de solo lectura (regla dura 2, docs/convenciones.md): nunca escribe nada, ni siquiera un contador de
/// evaluaciones (un consumidor real que necesite métricas de evaluación las agrega vía
/// <c>Shared.Infrastructure.Observability</c>, no mutando este flujo).
///
/// Algoritmo de decisión, en orden (el primero que aplica decide, corto-circuito):
/// 1. El flag no existe -&gt; <see cref="Result.Failure{TValue}"/> (404).
/// 2. <see cref="FeatureFlag.Activo"/> es <see langword="false"/> -&gt; inactivo para TODO contexto,
///    sin importar segmentos/rollouts (breaker global).
/// 3. El flag no tiene ningún <see cref="Rollout"/> asociado -&gt; activo para TODO contexto (flag
///    on/off simple, sin targeting -- equivalente al caso más común de <c>IFeatureFlagProvider</c> de
///    F4-12, pero resuelto desde persistencia propia en vez de <c>IConfiguration</c>).
/// 4. El flag tiene rollouts -&gt; activo si el contexto pertenece a AL MENOS UNO de los segmentos
///    asociados (semántica OR entre segmentos del mismo flag), evaluando cada
///    <see cref="SegmentoTipo"/> soportado:
///    - <see cref="SegmentoTipo.PorTenant"/>: <see cref="FeatureFlagEvaluationRequest.TenantId"/> ==
///      <see cref="Segmento.TenantIdCriterio"/>.
///    - <see cref="SegmentoTipo.PorPorcentaje"/>: bucket determinístico de
///      (<see cref="FeatureFlagEvaluationRequest.UserId"/> ?? <see cref="FeatureFlagEvaluationRequest.TenantId"/>)
///      vía <see cref="PorcentajeRolloutHasher"/>.
/// 5. Ninguno de los rollouts aplica -&gt; inactivo para este contexto (a pesar de que el flag está
///    globalmente activo -- solo lo ven los segmentos elegidos).
/// </summary>
internal sealed record EvaluarFeatureFlagQuery(string Nombre, Guid TenantId, string? UserId)
    : IQuery<FeatureFlagEvaluationResponse>;

internal sealed class EvaluarFeatureFlagQueryValidator : AbstractValidator<EvaluarFeatureFlagQuery>
{
    public EvaluarFeatureFlagQueryValidator()
    {
        RuleFor(q => q.Nombre).NotEmpty();
        RuleFor(q => q.TenantId).NotEmpty();
    }
}

internal sealed class EvaluarFeatureFlagQueryHandler(
    IReadRepository<FeatureFlag, Guid> flagRepository,
    IReadRepository<Rollout, Guid> rolloutRepository,
    IReadRepository<Segmento, Guid> segmentoRepository)
    : IRequestHandler<EvaluarFeatureFlagQuery, Result<FeatureFlagEvaluationResponse>>
{
    public async Task<Result<FeatureFlagEvaluationResponse>> Handle(
        EvaluarFeatureFlagQuery request, CancellationToken cancellationToken)
    {
        var flags = await flagRepository.ListAsync(new FeatureFlagPorNombreSpecification(request.Nombre), cancellationToken);
        var flag = flags.FirstOrDefault();
        if (flag is null)
        {
            return Result.Failure<FeatureFlagEvaluationResponse>(Error.NotFound(
                "FeatureManagement.Evaluacion.FlagNoEncontrado", $"No existe el flag '{request.Nombre}'."));
        }

        if (!flag.Activo)
        {
            return new FeatureFlagEvaluationResponse(flag.Nombre, false, "flag-inactivo-globalmente");
        }

        var rollouts = await rolloutRepository.ListAsync(new RolloutsDeFlagSpecification(flag.Id), cancellationToken);
        if (rollouts.Count == 0)
        {
            return new FeatureFlagEvaluationResponse(flag.Nombre, true, "activo-sin-segmentacion");
        }

        var segmentoIds = rollouts.Select(r => r.SegmentoId).Distinct().ToList();
        var segmentos = await segmentoRepository.ListAsync(new SegmentosPorIdsSpecification(segmentoIds), cancellationToken);

        foreach (var segmento in segmentos)
        {
            if (Pertenece(segmento, request, flag.Id))
            {
                return new FeatureFlagEvaluationResponse(flag.Nombre, true, $"segmento:{segmento.Nombre}");
            }
        }

        return new FeatureFlagEvaluationResponse(flag.Nombre, false, "fuera-de-todos-los-segmentos");
    }

    private static bool Pertenece(Segmento segmento, EvaluarFeatureFlagQuery request, Guid featureFlagId) => segmento.Tipo switch
    {
        SegmentoTipo.PorTenant => segmento.TenantIdCriterio == request.TenantId,
        SegmentoTipo.PorPorcentaje => PorcentajeRolloutHasher.PerteneceAlPorcentaje(
            featureFlagId, request.UserId ?? request.TenantId.ToString(), segmento.Porcentaje!.Value),
        _ => false,
    };
}
