using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.TaskInbox.Bandeja;

/// <summary>Estado de un <see cref="TaskInboxItem"/> -- reflejo del estado de la <c>WorkflowTask</c>
/// de origen tal como lo reportan los eventos de integración de Workflow (Fase 6, módulo 6), nunca
/// calculado ni decidido por este módulo.</summary>
public enum TaskInboxEstado
{
    Pendiente = 0,
    Aprobada = 1,
    Rechazada = 2,
}

/// <summary>
/// Read-model propio de Task Inbox (Fase 6, módulo 7 del Plan Maestro) para UNA tarea humana de
/// Workflow (Fase 6, módulo 6): se crea/actualiza exclusivamente al consumir
/// <c>Workflow.TareaAsignada</c>/<c>Workflow.TareaAprobada</c>/<c>Workflow.TareaRechazada</c> (ver
/// <c>Eventos/*IntegrationEventConsumer.cs</c>), nunca por una acción directa de un cliente HTTP --
/// este módulo NO expone ningún comando que mute <see cref="Estado"/>/<see cref="AsignadoAUserId"/>
/// directamente, porque esa mutación sigue siendo responsabilidad exclusiva de los comandos públicos
/// de Workflow (<c>POST /api/v1/workflows/tareas/{id}/resolver</c>, <c>.../delegar</c>).
/// </summary>
/// <remarks>
/// <see cref="Id"/> es el mismo <c>WorkflowTaskId</c> del evento de origen (nunca un identificador
/// propio nuevo): así el consumidor de eventos puede resolver "¿ya tengo una fila para esta tarea?"
/// con un simple <c>GetByIdAsync</c>, sin necesitar un índice adicional -- la clave natural entre
/// bounded contexts es el identificador que Workflow ya expone públicamente en sus eventos.
///
/// <see cref="LeidoAtUtc"/> es la única mutación que SÍ es responsabilidad exclusiva de este módulo
/// (<c>MarcarComoLeidaCommand</c>): Workflow no tiene ningún concepto de "leído", es una capacidad
/// propia de la experiencia de bandeja (Fase 6, módulo 7).
///
/// Implementa <see cref="IHasConcurrencyToken"/> por el mismo motivo que <c>WorkflowTask</c>
/// (<c>docs/guia-workflow.md</c>): dos caminos independientes pueden mutar la misma fila casi al mismo
/// tiempo sin ninguna coordinación entre sí -- el consumidor de eventos (asíncrono, reflejando una
/// resolución/delegación/escalamiento real de Workflow) y <c>MarcarComoLeidaCommand</c> (síncrono, un
/// actor humano marcando la tarea como leída en su propia bandeja). Sin el token, uno de los dos
/// efectos podría perderse en silencio (lost update).
/// </remarks>
public sealed class TaskInboxItem : Entity<Guid>, ITenantEntity, IAuditedEntity, IHasConcurrencyToken
{
    public Guid WorkflowInstanceId { get; private set; }

    /// <summary>Asignación actual -- reflejo de <c>WorkflowTask.AsignadoAUserId</c> al momento del
    /// último evento consumido (creación, delegación o escalamiento). Ver "Límites conocidos" en
    /// <c>docs/guia-taskinbox.md</c> para la ventana de latencia entre que Workflow reasigna la tarea y
    /// que este read-model refleja el nuevo asignado.</summary>
    public Guid AsignadoAUserId { get; private set; }

    public TaskInboxEstado Estado { get; private set; } = TaskInboxEstado.Pendiente;

    /// <summary>Fecha del último evento de asignación consumido (alta, delegación o escalamiento) --
    /// NO es la fecha de creación de la <c>WorkflowTask</c> original si esta fila se actualizó por una
    /// reasignación posterior.</summary>
    public DateTime AsignadaAtUtc { get; private set; }

    public Guid? ResueltaPorUserId { get; private set; }

    public DateTime? ResueltaAtUtc { get; private set; }

    /// <summary>Marca de lectura propia de la bandeja (Fase 6, módulo 7) -- <see langword="null"/> =
    /// no leída todavía. Nunca se resetea a <see langword="null"/> automáticamente por una
    /// reasignación: si Workflow delega/escala la tarea a un asignado distinto, la fila sigue
    /// existiendo pero ahora le pertenece a otro actor, así que <see cref="MarcarNoLeidaPorReasignacion"/>
    /// la limpia explícitamente (ver comentario en el método).</summary>
    public DateTime? LeidoAtUtc { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public TaskInboxItem(Guid workflowTaskId, Guid workflowInstanceId, Guid asignadoAUserId, DateTime asignadaAtUtc)
        : base(workflowTaskId)
    {
        WorkflowInstanceId = workflowInstanceId;
        AsignadoAUserId = asignadoAUserId;
        AsignadaAtUtc = asignadaAtUtc;
    }

    private TaskInboxItem()
    {
    }

    /// <summary>
    /// Construye una fila directamente en estado resuelto -- caso de borde documentado: los tres
    /// eventos de Workflow se publican en tópicos DISTINTOS (uno por <c>EventType</c>, ver
    /// <c>docs/convenciones.md</c> regla dura 22), así que Kafka solo garantiza orden DENTRO de un
    /// mismo tópico/partición, nunca ENTRE tópicos distintos -- nada impide, en teoría, que
    /// <c>Workflow.TareaAprobada</c>/<c>Workflow.TareaRechazada</c> se entregue y procese antes que
    /// <c>Workflow.TareaAsignada</c> de la misma tarea. Este factory evita perder el evento de
    /// resolución en ese escenario (se documenta explícitamente en <c>docs/guia-taskinbox.md</c>, "Límites
    /// conocidos" -- no se descarta el evento, pero la fila resultante nace ya resuelta, sin haber
    /// mostrado nunca el estado "Pendiente" en la bandeja).
    /// <see cref="AsignadoAUserId"/> queda en <see cref="Guid.Empty"/> (marcador "desconocido
    /// todavía") -- quien resolvió la tarea (<paramref name="resueltaPorUserId"/>) NO es
    /// necesariamente quien tenía la tarea asignada (delegación, aprobación por un supervisor), así
    /// que conflacionar ambos campos daría un dueño de bandeja incorrecto (hallazgo de auditoría de
    /// arquitectura, 2026-09-09). <see cref="AplicarAsignacion"/> corrige este valor cuando la
    /// asignación tardía finalmente llega, sin revertir la resolución ya aplicada.
    /// </summary>
    public static TaskInboxItem CrearYaResuelta(
        Guid workflowTaskId, Guid workflowInstanceId, TaskInboxEstado estado, Guid resueltaPorUserId, DateTime resueltaAtUtc)
    {
        var item = new TaskInboxItem(workflowTaskId, workflowInstanceId, Guid.Empty, resueltaAtUtc)
        {
            Estado = estado,
            ResueltaPorUserId = resueltaPorUserId,
            ResueltaAtUtc = resueltaAtUtc,
        };
        return item;
    }

    /// <summary>Aplica una (re)asignación reportada por <c>Workflow.TareaAsignada</c> -- se invoca
    /// tanto para la asignación inicial (creación de la fila) como para delegaciones/escalamientos
    /// posteriores sobre una fila ya existente. Idempotente respecto del mismo asignado: si el
    /// asignado no cambió, no reinicia <see cref="LeidoAtUtc"/> (evita "des-leer" una tarea por un
    /// evento duplicado que el propio Inbox de F1-24 ya debería haber descartado, pero que esta
    /// entidad no asume ciegamente).</summary>
    public void AplicarAsignacion(Guid asignadoAUserId, DateTime asignadaAtUtc)
    {
        if (Estado != TaskInboxEstado.Pendiente)
        {
            // La tarea ya fue resuelta -- probablemente porque Workflow.TareaAprobada/Rechazada llegó
            // antes que esta asignación por el mismo motivo documentado en CrearYaResuelta (tópicos
            // distintos, sin garantía de orden entre ellos). Una asignación tardía NUNCA debe revivir
            // el ítem a Pendiente ni borrar la resolución ya aplicada (hallazgo de auditoría de
            // arquitectura, 2026-09-09) -- como mucho, corrige el AsignadoAUserId si todavía era el
            // marcador "desconocido" (Guid.Empty) que dejó CrearYaResuelta.
            if (AsignadoAUserId == Guid.Empty)
            {
                AsignadoAUserId = asignadoAUserId;
            }

            return;
        }

        if (asignadoAUserId == AsignadoAUserId)
        {
            return;
        }

        AsignadoAUserId = asignadoAUserId;
        AsignadaAtUtc = asignadaAtUtc;
        MarcarNoLeidaPorReasignacion();
    }

    public void AplicarAprobacion(Guid resueltaPorUserId, DateTime resueltaAtUtc)
    {
        Estado = TaskInboxEstado.Aprobada;
        ResueltaPorUserId = resueltaPorUserId;
        ResueltaAtUtc = resueltaAtUtc;
    }

    public void AplicarRechazo(Guid resueltaPorUserId, DateTime resueltaAtUtc)
    {
        Estado = TaskInboxEstado.Rechazada;
        ResueltaPorUserId = resueltaPorUserId;
        ResueltaAtUtc = resueltaAtUtc;
    }

    /// <summary>Una reasignación (delegación/escalamiento) cambia de dueño la tarea -- la marca de
    /// lectura del asignado anterior ya no aplica al nuevo asignado.</summary>
    private void MarcarNoLeidaPorReasignacion() => LeidoAtUtc = null;

    public Result MarcarComoLeida(Guid actorUserId)
    {
        if (AsignadoAUserId != actorUserId)
        {
            return Result.Failure(new Error(
                "TaskInbox.Bandeja.NoAutorizado",
                "Solo el actor actualmente asignado puede marcar esta tarea como leída.", ErrorType.Forbidden));
        }

        LeidoAtUtc ??= DateTime.UtcNow;
        return Result.Success();
    }
}
