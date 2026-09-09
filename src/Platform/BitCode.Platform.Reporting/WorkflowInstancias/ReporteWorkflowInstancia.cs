using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Reporting.WorkflowInstancias;

/// <summary>Estado de una <see cref="ReporteWorkflowInstancia"/> -- reflejo del ciclo de vida de la
/// <c>WorkflowInstance</c> de origen tal como lo reportan los eventos de integración de Workflow (Fase 6,
/// módulo 6), nunca calculado ni decidido por este módulo.</summary>
public enum ReporteWorkflowInstanciaEstado
{
    EnCurso = 0,
    Finalizada = 1,
}

/// <summary>
/// Read-model propio de Reporting (Fase 6, módulo 11 del Plan Maestro) para UNA instancia de Workflow
/// (Fase 6, módulo 6) -- ejemplo de integración de referencia de este primer corte: se crea/actualiza
/// exclusivamente al consumir <c>Workflow.WorkflowInstanciaIniciada</c>/
/// <c>Workflow.WorkflowInstanciaFinalizada</c> (ver <c>WorkflowInstanciaIniciadaIntegrationEventConsumer</c>/
/// <c>WorkflowInstanciaFinalizadaIntegrationEventConsumer</c>), nunca por una acción directa de un cliente
/// HTTP -- este módulo no expone ningún comando que mute estos datos: solo lectura/exportación sobre lo que
/// los dos consumidores de eventos ya escribieron. Suficiente para un reporte real de "tiempo promedio de
/// resolución de instancias de workflow por definición" (<c>ListarPromedioDuracionPorDefinicionQuery</c>).
/// </summary>
/// <remarks>
/// <see cref="Id"/> es el mismo <c>WorkflowInstanceId</c> del evento de origen (nunca un identificador
/// propio nuevo) -- misma razón que <c>TaskInboxItem.Id</c> (Fase 6, módulo 7): el consumidor de eventos
/// resuelve "¿ya tengo una fila para esta instancia?" con un simple <c>GetByIdAsync</c>.
///
/// <b>Caso de borde de orden entre tópicos</b> (regla dura 22, <c>docs/convenciones.md</c>):
/// <c>Workflow.WorkflowInstanciaIniciada</c> y <c>Workflow.WorkflowInstanciaFinalizada</c> se publican en
/// tópicos DISTINTOS, así que Kafka solo garantiza orden DENTRO de un mismo tópico/partición, nunca ENTRE
/// tópicos distintos -- nada impide, en teoría, que la finalización se entregue y procese antes que el
/// inicio de la misma instancia. A diferencia de <c>TaskInboxItem.CrearYaResuelta</c> (que necesitaba un
/// marcador "asignado desconocido" porque <c>AsignadoAUserId</c> solo viaja en el evento de asignación),
/// acá <b>ambos eventos llevan <see cref="WorkflowDefinitionId"/></b> -- así que una finalización que llega
/// primero puede crear la fila con toda la información que necesita para el reporte agregado
/// (`WorkflowDefinitionId`/`Estado`/`FinalizadaAtUtc`/`EstadoFinalId`) sin ningún marcador "desconocido":
/// solo <see cref="IniciadaAtUtc"/> (y, por lo tanto, <see cref="DuracionSegundos"/>) queda sin calcular
/// hasta que el evento de inicio, tardío, finalmente llegue (<see cref="AplicarInicio"/> lo completa sin
/// revertir el estado ya finalizado). Documentado explícitamente en <c>docs/guia-reporting.md</c>, "Casos
/// de borde".
///
/// Implementa <see cref="IHasConcurrencyToken"/> por el mismo motivo que <c>TaskInboxItem</c> -- a
/// diferencia de <c>ImportJob</c>/<c>ExportJob</c> (Fase 6, módulo 10, un único job en background como
/// escritor exclusivo de cada fila), acá DOS consumidores de eventos INDEPENDIENTES (uno por cada tópico)
/// pueden leer-y-escribir la MISMA fila sin ninguna coordinación entre sí; sin el token, una entrega
/// simultánea de ambos eventos para la misma instancia podría perder en silencio uno de los dos efectos
/// (lost update) en vez de fallar con <see cref="ErrorType.Conflict"/> (F1-08) y dejar que
/// <c>IInboxMessageProcessor</c> reintente sobre el estado ya actualizado.
/// </remarks>
public sealed class ReporteWorkflowInstancia : Entity<Guid>, ITenantEntity, IAuditedEntity, IHasConcurrencyToken
{
    public Guid WorkflowDefinitionId { get; private set; }

    public ReporteWorkflowInstanciaEstado Estado { get; private set; } = ReporteWorkflowInstanciaEstado.EnCurso;

    /// <summary><see cref="BitCode.Framework.Shared.Kernel.DomainEvent.OccurredOnUtc"/> del evento
    /// <c>Workflow.WorkflowInstanciaIniciada</c> -- <see langword="null"/> si ese evento todavía no llegó
    /// (ver "Caso de borde de orden entre tópicos" arriba). No es necesariamente el instante exacto en que
    /// Workflow inició la instancia en su propio write-model, sino el instante en que se construyó el
    /// evento de integración (limitación real, documentada en <c>docs/guia-reporting.md</c>).</summary>
    public DateTime? IniciadaAtUtc { get; private set; }

    /// <summary>Igual que <see cref="IniciadaAtUtc"/> pero para <c>Workflow.WorkflowInstanciaFinalizada</c>.</summary>
    public DateTime? FinalizadaAtUtc { get; private set; }

    /// <summary>Calculado como <c>FinalizadaAtUtc - IniciadaAtUtc</c> en el momento en que AMBAS fechas ya
    /// son conocidas -- <see langword="null"/> mientras falte cualquiera de las dos (instancia todavía en
    /// curso, o evento de inicio/finalización aún no procesado). Persistido (no recalculado en cada
    /// lectura) para que <c>ListarPromedioDuracionPorDefinicionQuery</c> pueda promediar/agregar sin traer
    /// ambas fechas a memoria fila por fila.</summary>
    public int? DuracionSegundos { get; private set; }

    /// <summary>Identificador del estado final de la <c>WorkflowInstance</c> de origen (una fila de
    /// <c>WorkflowState</c>, Fase 6 módulo 6) tal como lo reporta el evento -- este módulo no resuelve su
    /// nombre lógico ("Aprobado"/"Rechazado"/etc.): eso exigiría, o bien duplicar el catálogo de estados de
    /// Workflow, o bien un acoplamiento síncrono entre bounded contexts que esta tarea decidió NO introducir
    /// sin una necesidad de negocio concreta (mismo criterio que <c>BandejaDeActorSpecification</c> de Task
    /// Inbox). Ver "Qué quedó completo y qué no" en <c>docs/guia-reporting.md</c>.</summary>
    public Guid? EstadoFinalId { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public ReporteWorkflowInstancia(Guid workflowInstanceId, Guid workflowDefinitionId)
        : base(workflowInstanceId)
    {
        WorkflowDefinitionId = workflowDefinitionId;
    }

    private ReporteWorkflowInstancia()
    {
    }

    /// <summary>Construye una fila directamente en estado finalizado -- caso de borde documentado en el
    /// <c>remarks</c> de la clase: la finalización puede llegar antes que el inicio de la misma
    /// instancia.</summary>
    public static ReporteWorkflowInstancia CrearYaFinalizada(
        Guid workflowInstanceId, Guid workflowDefinitionId, Guid estadoFinalId, DateTime finalizadaAtUtc)
    {
        var reporte = new ReporteWorkflowInstancia(workflowInstanceId, workflowDefinitionId)
        {
            Estado = ReporteWorkflowInstanciaEstado.Finalizada,
            EstadoFinalId = estadoFinalId,
            FinalizadaAtUtc = finalizadaAtUtc,
        };
        return reporte;
    }

    /// <summary>Aplica <c>Workflow.WorkflowInstanciaIniciada</c> -- tanto en la creación normal (fila
    /// todavía no existía) como en la corrección tardía de una fila que ya nació finalizada (ver
    /// <see cref="CrearYaFinalizada"/>). Idempotente: si <see cref="IniciadaAtUtc"/> ya estaba seteado
    /// (reentrega del mismo evento, o un segundo evento de inicio inesperado para la misma instancia), no
    /// lo pisa -- se queda con el primer valor conocido.</summary>
    public void AplicarInicio(DateTime iniciadaAtUtc)
    {
        if (IniciadaAtUtc is not null)
        {
            return;
        }

        IniciadaAtUtc = iniciadaAtUtc;
        RecalcularDuracion();
    }

    /// <summary>Aplica <c>Workflow.WorkflowInstanciaFinalizada</c> -- idempotente por el mismo motivo que
    /// <see cref="AplicarInicio"/>: una segunda finalización para la misma instancia (reentrega, o un caso
    /// de negocio inesperado) no pisa la primera fecha/estado final ya registrados.</summary>
    public void AplicarFinalizacion(Guid estadoFinalId, DateTime finalizadaAtUtc)
    {
        if (Estado == ReporteWorkflowInstanciaEstado.Finalizada)
        {
            return;
        }

        Estado = ReporteWorkflowInstanciaEstado.Finalizada;
        EstadoFinalId = estadoFinalId;
        FinalizadaAtUtc = finalizadaAtUtc;
        RecalcularDuracion();
    }

    private void RecalcularDuracion()
    {
        if (IniciadaAtUtc is null || FinalizadaAtUtc is null)
        {
            DuracionSegundos = null;
            return;
        }

        try
        {
            var duracion = FinalizadaAtUtc.Value - IniciadaAtUtc.Value;

            // Reloj de eventos potencialmente inconsistente entre productores/reintentos (regla dura de
            // esta tarea: nunca asumir ciegamente que los datos externos son coherentes) -- una
            // finalización "antes" del inicio (por ejemplo, por dos relojes de instancia de Workflow
            // ligeramente desincronizados en un despliegue distribuido real) no debe producir una duración
            // negativa sin sentido en el reporte agregado.
            DuracionSegundos = duracion.TotalSeconds >= 0
                ? (int)Math.Round(duracion.TotalSeconds)
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Defensivo: cualquier excepción no clasificada calculando la duración (por ejemplo, un
            // overflow de DateTime en un escenario patológico) no debe tumbar el procesamiento del evento
            // completo -- el reporte simplemente queda sin duración calculada para esta instancia.
            DuracionSegundos = null;
        }
    }
}
