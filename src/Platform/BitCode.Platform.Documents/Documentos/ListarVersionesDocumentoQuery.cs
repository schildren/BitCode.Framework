using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Documents.Documentos;

internal sealed record ListarVersionesDocumentoQuery(Guid DocumentoId) : IQuery<IReadOnlyList<DocumentoVersionResponse>>;

internal sealed class ListarVersionesDocumentoQueryHandler(IReadRepository<DocumentoVersion, Guid> repository)
    : IRequestHandler<ListarVersionesDocumentoQuery, Result<IReadOnlyList<DocumentoVersionResponse>>>
{
    public async Task<Result<IReadOnlyList<DocumentoVersionResponse>>> Handle(
        ListarVersionesDocumentoQuery request, CancellationToken cancellationToken)
    {
        var versiones = await repository.ListAsync(
            new VersionesDeDocumentoSpecification(request.DocumentoId),
            v => new DocumentoVersionResponse(
                v.Id, v.DocumentoId, v.Numero, v.NombreArchivo, v.ContentType, v.TamanioBytes, v.HashSha256, v.EstadoEscaneo),
            cancellationToken);

        return Result.Success(versiones);
    }
}
