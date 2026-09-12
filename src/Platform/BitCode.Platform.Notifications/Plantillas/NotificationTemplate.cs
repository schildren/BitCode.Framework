using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Notifications.Plantillas;

/// <summary>
/// Plantilla de notificación (Fase 6, módulo 8: "plantillas" del Plan Maestro) -- identificada por un
/// código lógico estable (por ejemplo <c>"tarea-asignada"</c>, no un GUID) más el <see cref="Canal"/> y
/// el <see cref="Locale"/> con los que se renderiza: la misma <see cref="Codigo"/> puede tener una
/// plantilla distinta por canal (el cuerpo de un email admite HTML/firma; el de una notificación InApp
/// es texto corto) y por locale (mismo criterio de vigencia por código que ya usa
/// <c>Catalogs and Parameters</c>, Fase 6, módulo 3, aunque sin versionado histórico -- ver "Pendientes"
/// en <c>docs/guia-notifications.md</c>).
/// </summary>
/// <remarks>
/// <see cref="Cuerpo"/>/<see cref="Asunto"/> usan un reemplazo de placeholders <c>{variable}</c> simple
/// (<see cref="NotificationTemplateRenderer"/>) -- DELIBERADAMENTE no un motor de templating genérico
/// (Scriban/Handlebars/Razor) -- mismo criterio ya aplicado a <c>WorkflowRuleEvaluator</c> (Fase 6,
/// módulo 6, Workflow): sin condicionales, loops ni funciones. Suficiente para el 100% de los casos de
/// uso de notificación transaccional simple ("Hola {nombre}, tenés una tarea nueva: {titulo}"); un
/// consumidor que necesite lógica de presentación más rica (plurales, condicionales por idioma, tablas)
/// necesita un motor real -- fuera de alcance de este corte.
/// </remarks>
public sealed class NotificationTemplate : Entity<Guid>, ITenantEntity, IAuditedEntity
{
    public string Codigo { get; private set; } = string.Empty;

    public NotificationChannel Canal { get; private set; }

    public string Locale { get; private set; } = string.Empty;

    /// <summary><see langword="null"/> para canales que no tienen concepto de asunto (InApp). Admite
    /// los mismos placeholders <c>{variable}</c> que <see cref="Cuerpo"/>.</summary>
    public string? Asunto { get; private set; }

    public string Cuerpo { get; private set; } = string.Empty;

    /// <summary>Una plantilla inactiva no se resuelve (<c>NotificationSender</c> falla con
    /// <see cref="ErrorType.NotFound"/> como si no existiera) -- permite "dar de baja" una plantilla sin
    /// borrarla (una <see cref="Envio.NotificationDelivery"/> ya persistida sigue teniendo trazabilidad
    /// completa aunque la plantilla que la originó ya no esté activa).</summary>
    public bool Activa { get; private set; } = true;

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public NotificationTemplate(
        Guid id, string codigo, NotificationChannel canal, string locale, string? asunto, string cuerpo)
        : base(id)
    {
        Codigo = codigo;
        Canal = canal;
        Locale = locale;
        Asunto = asunto;
        Cuerpo = cuerpo;
    }

    private NotificationTemplate()
    {
    }

    public void Desactivar() => Activa = false;

    public void Activar() => Activa = true;
}
