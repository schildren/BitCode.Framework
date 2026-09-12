using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.Documents.Documentos;

/// <summary>Especificaciones reutilizadas por más de un handler de <c>Documentos</c> -- agrupadas en un
/// único archivo para evitar duplicar el mismo criterio de filtro entre comandos/queries (regla dura 5,
/// nunca <c>IQueryable</c> expuesto: toda consulta pasa por una <see cref="Specification{T}"/>).</summary>
internal sealed class TodosLosDocumentosOrdenadosPorTituloSpecification : Specification<Documento>
{
    public TodosLosDocumentosOrdenadosPorTituloSpecification() => ApplyOrderBy(d => d.Titulo);
}

internal sealed class VersionesDeDocumentoSpecification : Specification<DocumentoVersion>
{
    public VersionesDeDocumentoSpecification(Guid documentoId) => ApplyCriteria(v => v.DocumentoId == documentoId);
}

internal sealed class VersionDeDocumentoPorNumeroSpecification : Specification<DocumentoVersion>
{
    public VersionDeDocumentoPorNumeroSpecification(Guid documentoId, int numero) =>
        ApplyCriteria(v => v.DocumentoId == documentoId && v.Numero == numero);
}
