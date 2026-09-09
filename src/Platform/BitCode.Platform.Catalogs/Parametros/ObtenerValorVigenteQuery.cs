using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Catalogs.Parametros;

/// <summary>
/// La consulta real que un consumidor típico del módulo necesita (Plan Maestro, alcance del primer
/// corte de Parámetros): "¿cuál es el valor vigente de este parámetro hoy (o en una fecha dada)?" --
/// nunca "traer todas las vigencias y resolver en el cliente".
/// </summary>
internal sealed record ObtenerValorVigenteQuery(Guid ParametroId, DateTime? Fecha) : IQuery<ParametroValorVigenteResponse>;

internal sealed class ObtenerValorVigenteQueryHandler(
    IReadRepository<Parametro, Guid> parametroRepository,
    IReadRepository<ParametroVigencia, Guid> vigenciaRepository)
    : IRequestHandler<ObtenerValorVigenteQuery, Result<ParametroValorVigenteResponse>>
{
    public async Task<Result<ParametroValorVigenteResponse>> Handle(
        ObtenerValorVigenteQuery request, CancellationToken cancellationToken)
    {
        var parametro = await parametroRepository.GetByIdAsync(request.ParametroId, cancellationToken);
        if (parametro is null)
        {
            return Result.Failure<ParametroValorVigenteResponse>(Error.NotFound(
                "Catalogos.Parametros.NoEncontrado", $"No existe el parámetro {request.ParametroId}."));
        }

        var fecha = request.Fecha ?? DateTime.UtcNow;

        var vigente = (await vigenciaRepository.ListAsync(
            new VigenciaEnFechaSpecification(request.ParametroId, fecha), cancellationToken)).FirstOrDefault();

        if (vigente is null)
        {
            return Result.Failure<ParametroValorVigenteResponse>(Error.NotFound(
                "Catalogos.Parametros.SinVigenciaVigente",
                $"El parámetro {request.ParametroId} no tiene ninguna vigencia vigente en la fecha {fecha:O}."));
        }

        return new ParametroValorVigenteResponse(parametro.Id, vigente.Valor, fecha, vigente.VigenteDesde, vigente.VigenteHasta);
    }
}
