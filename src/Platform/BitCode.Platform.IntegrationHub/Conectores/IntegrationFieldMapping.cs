using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.IntegrationHub.Conectores;

/// <summary>
/// Un mapeo campo-a-campo entre el payload interno (JSON de negocio del bounded context que dispara la
/// solicitud) y el payload externo esperado por <see cref="IntegrationConnector"/> (Fase 6, módulo 9:
/// "mapping" del Plan Maestro). <see cref="CampoOrigen"/>/<see cref="CampoDestino"/> son rutas JSON
/// simples separadas por punto (por ejemplo <c>"cliente.nombre"</c>) -- DELIBERADAMENTE no un motor de
/// transformación genérico (JSONata/XSLT/Scriban): sin expresiones, condicionales, funciones ni
/// conversión de tipos, solo "tomá el valor de esta ruta del JSON interno y ponelo en esta otra ruta del
/// JSON externo", mismo criterio ya aplicado a <c>WorkflowRuleEvaluator</c> (Fase 6, módulo 6) y
/// <c>NotificationTemplateRenderer</c> (Fase 6, módulo 8). Ver <see cref="Mapping.IntegrationFieldMapper"/>
/// para la implementación del motor de aplicación y <c>docs/guia-integration-hub.md</c>, sección
/// "Mapping", para las limitaciones honestas de este alcance.
/// </summary>
public sealed class IntegrationFieldMapping : Entity<Guid>, ITenantEntity
{
    public Guid ConnectorId { get; private set; }

    public string CampoOrigen { get; private set; } = string.Empty;

    public string CampoDestino { get; private set; } = string.Empty;

    public Guid TenantId { get; set; }

    public IntegrationFieldMapping(Guid id, Guid connectorId, string campoOrigen, string campoDestino)
        : base(id)
    {
        ConnectorId = connectorId;
        CampoOrigen = campoOrigen;
        CampoDestino = campoDestino;
    }

    private IntegrationFieldMapping()
    {
    }
}
