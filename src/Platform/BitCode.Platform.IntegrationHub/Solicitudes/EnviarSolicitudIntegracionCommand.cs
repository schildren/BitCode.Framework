using System.Text.Json;
using BitCode.Framework.Platform.IntegrationHub.Actors;
using BitCode.Framework.Platform.IntegrationHub.Conectores;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.IntegrationHub.Solicitudes;

/// <summary>
/// Encola una solicitud saliente hacia un conector (Fase 6, módulo 9: "colas" del Plan Maestro) --
/// solo inserta la fila en <see cref="IntegrationRequestEstado.PendienteDeEnvio"/>, nunca intenta la
/// llamada HTTP en este mismo request (ver el <c>remarks</c> de <see cref="IntegrationRequest"/>).
/// Implementa <see cref="IIdempotentCommand"/> (F1-22): un POST repetido con la misma Idempotency-Key y
/// el mismo payload no encola una segunda solicitud duplicada -- mismo criterio que
/// <c>CrearDocumentoCommand</c> (Fase 6, módulo 5).
/// </summary>
internal sealed record EnviarSolicitudIntegracionCommand(string ConectorCodigo, string PayloadJson)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class EnviarSolicitudIntegracionCommandValidator : AbstractValidator<EnviarSolicitudIntegracionCommand>
{
    public EnviarSolicitudIntegracionCommandValidator()
    {
        RuleFor(c => c.ConectorCodigo).NotEmpty().MaximumLength(128);
        RuleFor(c => c.PayloadJson).NotEmpty()
            .Must(EsJsonValido).WithMessage("PayloadJson debe ser un documento JSON válido.");
    }

    private static bool EsJsonValido(string payload)
    {
        try
        {
            using var _ = JsonDocument.Parse(payload);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

internal sealed class EnviarSolicitudIntegracionCommandHandler(
    IReadRepository<IntegrationConnector, Guid> connectorRepository,
    IRepository<IntegrationRequest, Guid> requestRepository,
    IIntegrationHubActorContext actorContext)
    : IRequestHandler<EnviarSolicitudIntegracionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(EnviarSolicitudIntegracionCommand request, CancellationToken cancellationToken)
    {
        var conectores = await connectorRepository.ListAsync(
            new ConectorActivoPorCodigoSpecification(request.ConectorCodigo), cancellationToken);
        var conector = conectores.FirstOrDefault();
        if (conector is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "IntegrationHub.Solicitudes.ConectorNoEncontrado",
                $"No existe un conector activo con código '{request.ConectorCodigo}'."));
        }

        var integrationRequest = new IntegrationRequest(
            Guid.NewGuid(), conector.Id, request.PayloadJson, actorContext.GetCurrentUserId());

        await requestRepository.AddAsync(integrationRequest, cancellationToken);

        return integrationRequest.Id;
    }
}
