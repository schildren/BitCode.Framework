using BitCode.Framework.Platform.IntegrationHub.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.IntegrationHub.Conectores;

/// <summary>Un elemento del mapping campo-a-campo recibido al crear un conector -- ver el <c>remarks</c>
/// de <see cref="IntegrationFieldMapping"/>.</summary>
internal sealed record FieldMappingDto(string CampoOrigen, string CampoDestino);

/// <summary>
/// Alta de un conector con su mapping inicial (Fase 6, módulo 9). Operación SENSIBLE (asocia una
/// referencia de credencial a un endpoint externo) -- auditada explícitamente vía <see cref="IAuditWriter"/>
/// (F2-15), mismo criterio que <c>CrearDocumentoCommand</c> (Fase 6, módulo 5).
/// </summary>
internal sealed record CrearConectorCommand(
    string Codigo,
    string Nombre,
    string BaseUrl,
    MetodoHttpConector Metodo,
    TipoAutenticacionConector TipoAutenticacion,
    string? SecretKey,
    string? ApiKeyHeaderName,
    IReadOnlyList<FieldMappingDto> Mappings) : ICommand<Guid>;

internal sealed class CrearConectorCommandValidator : AbstractValidator<CrearConectorCommand>
{
    public CrearConectorCommandValidator()
    {
        RuleFor(c => c.Codigo).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(256);
        RuleFor(c => c.BaseUrl).NotEmpty().MaximumLength(2048)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("BaseUrl debe ser una URL absoluta http/https.");
        RuleFor(c => c.ApiKeyHeaderName).MaximumLength(128);
        RuleFor(c => c.SecretKey).MaximumLength(256);

        // TipoAutenticacion distinto de Ninguna EXIGE una referencia de secreto -- un conector configurado
        // con ApiKey/BearerEstatico sin SecretKey fallaría siempre en el momento de enviar (ISecretProvider
        // no tendría qué resolver); mejor rechazarlo en el alta que dejarlo fallar silenciosamente después.
        RuleFor(c => c.SecretKey).NotEmpty()
            .When(c => c.TipoAutenticacion != TipoAutenticacionConector.Ninguna)
            .WithMessage("SecretKey es obligatorio cuando TipoAutenticacion no es Ninguna.");

        RuleForEach(c => c.Mappings).ChildRules(mapping =>
        {
            mapping.RuleFor(m => m.CampoOrigen).NotEmpty().MaximumLength(256);
            mapping.RuleFor(m => m.CampoDestino).NotEmpty().MaximumLength(256);
        });
    }
}

/// <summary>
/// No llama <c>IUnitOfWork.SaveChangesAsync</c> explícitamente (regla dura 1, docs/convenciones.md):
/// <c>TransactionBehavior</c> lo hace al final del pipeline.
/// </summary>
internal sealed class CrearConectorCommandHandler(
    IRepository<IntegrationConnector, Guid> connectorRepository,
    IRepository<IntegrationFieldMapping, Guid> mappingRepository,
    IReadRepository<IntegrationConnector, Guid> connectorReadRepository,
    IAuditWriter auditWriter,
    IIntegrationHubActorContext actorContext)
    : IRequestHandler<CrearConectorCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearConectorCommand request, CancellationToken cancellationToken)
    {
        // Unicidad reforzada también a nivel de base de datos (índice único en IntegrationHubDbContext,
        // regla dura 6, docs/convenciones.md) -- este chequeo solo mejora el mensaje de error, no
        // reemplaza la restricción real.
        var yaExiste = await connectorReadRepository.AnyAsync(
            new ConectorPorCodigoSpecification(request.Codigo), cancellationToken);
        if (yaExiste)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "IntegrationHub.Conectores.CodigoDuplicado", $"Ya existe un conector con código '{request.Codigo}'."));
        }

        var connector = new IntegrationConnector(
            Guid.NewGuid(), request.Codigo, request.Nombre, request.BaseUrl, request.Metodo,
            request.TipoAutenticacion, request.SecretKey, request.ApiKeyHeaderName);

        await connectorRepository.AddAsync(connector, cancellationToken);

        foreach (var mapping in request.Mappings)
        {
            await mappingRepository.AddAsync(
                new IntegrationFieldMapping(Guid.NewGuid(), connector.Id, mapping.CampoOrigen, mapping.CampoDestino),
                cancellationToken);
        }

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "integrationhub.conectores.crear",
            resource: new AuditResource("integrationhub.conectores", connector.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?>
            {
                ["codigo"] = connector.Codigo,
                ["baseUrl"] = connector.BaseUrl,
                ["tipoAutenticacion"] = connector.TipoAutenticacion.ToString(),
                // Nunca el VALOR del secreto -- solo la clave de referencia, que ya es lo único que esta
                // entidad persiste (ver el remarks de IntegrationConnector).
                ["secretKey"] = connector.SecretKey,
                ["cantidadMappings"] = request.Mappings.Count.ToString(),
            });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return connector.Id;
    }
}
