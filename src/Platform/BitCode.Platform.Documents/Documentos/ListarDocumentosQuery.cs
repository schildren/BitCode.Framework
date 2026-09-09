using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Documents.Documentos;

// F1-21: expone page/pageSize crudos del cliente HTTP -- la validación de límites (nunca "todas las
// filas") ocurre dentro del handler, vía PageRequest.Create, antes de tocar el repositorio.
internal sealed record ListarDocumentosQuery(int Page, int PageSize) : IQuery<PagedResult<DocumentoResponse>>;

internal sealed class ListarDocumentosQueryHandler(IReadRepository<Documento, Guid> repository)
    : IRequestHandler<ListarDocumentosQuery, Result<PagedResult<DocumentoResponse>>>
{
    public async Task<Result<PagedResult<DocumentoResponse>>> Handle(
        ListarDocumentosQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<DocumentoResponse>>(pageRequestResult.Error);
        }

        // La proyección referencia directamente las columnas mapeadas (CreatedAtUtc/RetencionDias) en vez
        // del atajo Documento.DisponibleParaDisposicionDesde -- DateTime.AddDays sobre columnas mapeadas
        // sí traduce a SQL, un getter de propiedad de dominio no mapeada podría no hacerlo (F1-17: nunca
        // materializar la entidad completa solo para leer un cálculo derivado).
        return await repository.ListPagedAsync(
            new TodosLosDocumentosOrdenadosPorTituloSpecification(),
            d => new DocumentoResponse(
                d.Id, d.Titulo, d.Descripcion, d.Clasificacion, d.RetencionDias, d.VersionActualId,
                d.VersionActualNumero, d.CreatedAtUtc.AddDays(d.RetencionDias)),
            pageRequestResult.Value,
            cancellationToken);
    }
}
