using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Documents.Documentos;

internal sealed record ObtenerDocumentoQuery(Guid Id) : IQuery<DocumentoResponse>;

internal sealed class ObtenerDocumentoQueryHandler(IReadRepository<Documento, Guid> repository)
    : IRequestHandler<ObtenerDocumentoQuery, Result<DocumentoResponse>>
{
    public async Task<Result<DocumentoResponse>> Handle(ObtenerDocumentoQuery request, CancellationToken cancellationToken)
    {
        var documento = await repository.GetByIdAsync(request.Id, cancellationToken);

        return documento is null
            ? Result.Failure<DocumentoResponse>(
                Error.NotFound("Documents.Documentos.NoEncontrado", $"No existe el documento {request.Id}."))
            : new DocumentoResponse(
                documento.Id, documento.Titulo, documento.Descripcion, documento.Clasificacion,
                documento.RetencionDias, documento.VersionActualId, documento.VersionActualNumero,
                documento.DisponibleParaDisposicionDesde);
    }
}
