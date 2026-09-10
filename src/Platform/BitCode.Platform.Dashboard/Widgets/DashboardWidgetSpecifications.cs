using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.Dashboard.Widgets;

/// <summary>Todos los widgets del PROPIO dashboard del actor -- SIEMPRE filtra por el actor autenticado
/// (mismo criterio que <c>BandejaDeActorSpecification</c> de Task Inbox), nunca recibe el
/// <see cref="DashboardWidget.UserId"/> de otro usuario como parámetro de un cliente.</summary>
internal sealed class WidgetsDeUsuarioSpecification : Specification<DashboardWidget>
{
    public WidgetsDeUsuarioSpecification(Guid userId)
    {
        ApplyCriteria(w => w.UserId == userId);
        ApplyOrderBy(w => w.Orden);
    }
}
