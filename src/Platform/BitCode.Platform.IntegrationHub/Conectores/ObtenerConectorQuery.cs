using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.IntegrationHub.Conectores;

internal sealed record ObtenerConectorQuery(Guid Id) : IQuery<ConectorResponse>;

internal sealed class ObtenerConectorQueryHandler(
    IReadRepository<IntegrationConnector, Guid> connectorRepository,
    IReadRepository<IntegrationFieldMapping, Guid> mappingRepository)
    : IRequestHandler<ObtenerConectorQuery, Result<ConectorResponse>>
{
    public async Task<Result<ConectorResponse>> Handle(ObtenerConectorQuery request, CancellationToken cancellationToken)
    {
        var connector = await connectorRepository.GetByIdAsync(request.Id, cancellationToken);
        if (connector is null)
        {
            return Result.Failure<ConectorResponse>(Error.NotFound(
                "IntegrationHub.Conectores.NoEncontrado", $"No existe el conector {request.Id}."));
        }

        var mappings = await mappingRepository.ListAsync(new MappingsDeConectorSpecification(connector.Id), cancellationToken);

        return new ConectorResponse(
            connector.Id, connector.Codigo, connector.Nombre, connector.BaseUrl, connector.Metodo,
            connector.TipoAutenticacion, connector.ApiKeyHeaderName, connector.Activo,
            mappings.Select(m => new FieldMappingResponse(m.Id, m.CampoOrigen, m.CampoDestino)).ToList());
    }
}
