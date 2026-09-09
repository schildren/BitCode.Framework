using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.IntegrationHub.Conectores;

internal sealed class ConectorPorCodigoSpecification : Specification<IntegrationConnector>
{
    public ConectorPorCodigoSpecification(string codigo) => ApplyCriteria(c => c.Codigo == codigo);
}

/// <summary>Conector activo por su código lógico -- consultado por
/// <c>Procesamiento.IntegrationOutboundProcessorJob</c>/<c>EnviarSolicitudIntegracionCommand</c> antes de
/// encolar o procesar una solicitud.</summary>
internal sealed class ConectorActivoPorCodigoSpecification : Specification<IntegrationConnector>
{
    public ConectorActivoPorCodigoSpecification(string codigo) => ApplyCriteria(c => c.Codigo == codigo && c.Activo);
}

internal sealed class TodosLosConectoresSpecification : Specification<IntegrationConnector>
{
    public TodosLosConectoresSpecification() => ApplyOrderBy(c => c.Codigo);
}

internal sealed class MappingsDeConectorSpecification : Specification<IntegrationFieldMapping>
{
    public MappingsDeConectorSpecification(Guid connectorId) => ApplyCriteria(m => m.ConnectorId == connectorId);
}
