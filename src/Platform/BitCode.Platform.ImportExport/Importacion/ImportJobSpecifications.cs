using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.ImportExport.Importacion;

internal sealed class TodosLosImportJobsSpecification : Specification<ImportJob>
{
    public TodosLosImportJobsSpecification(string? tipoImportacion, ImportJobEstado? estado)
    {
        ApplyCriteria(j =>
            (tipoImportacion == null || j.TipoImportacion == tipoImportacion) && (estado == null || j.Estado == estado));
        ApplyOrderByDescending(j => j.CreatedAtUtc);
    }
}

internal sealed class ErroresDeImportJobSpecification : Specification<ImportJobError>
{
    public ErroresDeImportJobSpecification(Guid importJobId)
    {
        ApplyCriteria(e => e.ImportJobId == importJobId);
        ApplyOrderBy(e => e.NumeroFila);
    }
}
