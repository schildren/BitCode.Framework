using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.ImportExport.Exportacion;

internal sealed class TodosLosExportJobsSpecification : Specification<ExportJob>
{
    public TodosLosExportJobsSpecification(string? tipoExportacion, ExportJobEstado? estado)
    {
        ApplyCriteria(j =>
            (tipoExportacion == null || j.TipoExportacion == tipoExportacion) && (estado == null || j.Estado == estado));
        ApplyOrderByDescending(j => j.CreatedAtUtc);
    }
}
