using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Catalogs.Catalogos;

/// <summary>
/// La consulta real que un consumidor típico del módulo necesita (ver Plan Maestro, alcance del primer
/// corte de Catalogs): "¿cuáles son los ítems de la versión vigente de este catálogo, a esta fecha (o
/// hoy si no se especifica)?" -- nunca "traer todas las versiones y filtrar en el cliente".
/// </summary>
internal sealed record ListarItemsVersionVigenteQuery(Guid CatalogoId, DateTime? Fecha) : IQuery<IReadOnlyList<CatalogoItemResponse>>;

internal sealed class ListarItemsVersionVigenteQueryHandler(
    IReadRepository<Catalogo, Guid> catalogoRepository,
    IReadRepository<CatalogoVersion, Guid> versionRepository,
    IReadRepository<CatalogoItem, Guid> itemRepository)
    : IRequestHandler<ListarItemsVersionVigenteQuery, Result<IReadOnlyList<CatalogoItemResponse>>>
{
    public async Task<Result<IReadOnlyList<CatalogoItemResponse>>> Handle(
        ListarItemsVersionVigenteQuery request, CancellationToken cancellationToken)
    {
        var catalogo = await catalogoRepository.GetByIdAsync(request.CatalogoId, cancellationToken);
        if (catalogo is null)
        {
            return Result.Failure<IReadOnlyList<CatalogoItemResponse>>(Error.NotFound(
                "Catalogos.Catalogos.NoEncontrado", $"No existe el catálogo {request.CatalogoId}."));
        }

        var fecha = request.Fecha ?? DateTime.UtcNow;

        var versionVigente = (await versionRepository.ListAsync(
            new CatalogoVersionVigenteEnFechaSpecification(request.CatalogoId, fecha), cancellationToken)).FirstOrDefault();

        if (versionVigente is null)
        {
            return Result.Failure<IReadOnlyList<CatalogoItemResponse>>(Error.NotFound(
                "Catalogos.Versiones.SinVersionVigente",
                $"El catálogo {request.CatalogoId} no tiene ninguna versión vigente en la fecha {fecha:O}."));
        }

        var items = await itemRepository.ListAsync(
            new ItemsDeVersionOrdenadosSpecification(versionVigente.Id),
            i => new CatalogoItemResponse(i.Id, i.Codigo, i.Etiqueta, i.Valor, i.Orden),
            cancellationToken);

        return Result.Success<IReadOnlyList<CatalogoItemResponse>>(items);
    }
}
