using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Dashboard.Widgets;

/// <summary>
/// Un widget que UN usuario agregó a SU PROPIO dashboard (Fase 6, módulo 12 del Plan Maestro:
/// "preferencias") -- a diferencia de la mayoría de los módulos anteriores de Fase 6 (que en su mayoría
/// decidieron "sin ownership, datos operacionales", ver <c>docs/guia-integration-hub.md</c>), acá SÍ hay
/// un caso genuino de dato personal por usuario: qué widgets tiene cada usuario en su dashboard y en qué
/// orden. Mismo criterio de ownership estricto que <c>TaskInboxItem</c> (Fase 6, módulo 7) --
/// <see cref="UserId"/> nunca lo elige el llamador de un comando, siempre se resuelve del actor
/// autenticado (ver <c>Actors.IDashboardActorContext</c>).
/// </summary>
/// <remarks>
/// <see cref="WorkflowDefinitionId"/> es el único parámetro de widget de este primer corte -- solo aplica
/// (y es obligatorio, ver <c>AgregarWidgetCommandValidator</c>) cuando
/// <see cref="Tipo"/> es <see cref="TipoWidgetDashboard.PromedioDuracionWorkflowPorDefinicion"/>. Un tipo
/// de widget futuro con otros parámetros necesitaría agregar sus propias columnas (nulas para los tipos
/// que no las usan) -- mismo criterio pragmático que <c>NotificationTemplate</c> aplica a sus campos
/// específicos por canal.
///
/// Implementa <see cref="IHasConcurrencyToken"/> (F1-08, evaluado proactivamente): un mismo usuario puede
/// tener el dashboard abierto en dos pestañas/dispositivos y reordenar/agregar/quitar widgets casi
/// simultáneamente sin ninguna coordinación entre sí -- sin el token, una de las dos escrituras
/// concurrentes podría perderse en silencio (lost update) en vez de fallar con
/// <c>ErrorType.Conflict</c> (HTTP 409, F1-08).
/// </remarks>
public sealed class DashboardWidget : Entity<Guid>, ITenantEntity, IAuditedEntity, IHasConcurrencyToken
{
    public Guid UserId { get; private set; }

    public TipoWidgetDashboard Tipo { get; private set; }

    public string Titulo { get; private set; } = string.Empty;

    /// <summary><see langword="null"/> salvo cuando <see cref="Tipo"/> es
    /// <see cref="TipoWidgetDashboard.PromedioDuracionWorkflowPorDefinicion"/> (ver <c>remarks</c> de la
    /// clase).</summary>
    public Guid? WorkflowDefinitionId { get; private set; }

    /// <summary>Posición del widget en el dashboard del usuario (0-based) -- mutada exclusivamente por
    /// <see cref="CambiarOrden"/> (<c>ReordenarWidgetsCommand</c>).</summary>
    public int Orden { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public DashboardWidget(
        Guid id, Guid userId, TipoWidgetDashboard tipo, string titulo, Guid? workflowDefinitionId, int orden)
        : base(id)
    {
        UserId = userId;
        Tipo = tipo;
        Titulo = titulo;
        WorkflowDefinitionId = workflowDefinitionId;
        Orden = orden;
    }

    private DashboardWidget()
    {
    }

    public void CambiarOrden(int nuevoOrden) => Orden = nuevoOrden;
}
