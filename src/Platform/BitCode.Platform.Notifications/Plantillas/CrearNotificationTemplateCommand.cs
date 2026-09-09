using BitCode.Framework.Platform.Notifications.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Notifications.Plantillas;

/// <summary>
/// Alta de una plantilla (Fase 6, módulo 8: "plantillas" del Plan Maestro). No hay un comando de
/// actualización en este primer corte -- ver "Pendientes" en <c>docs/guia-notifications.md</c>: una
/// plantilla mal redactada se corrige dando de baja la actual (<c>DesactivarNotificationTemplateCommand</c>
/// no existe todavía tampoco) y creando una nueva, no hay versionado histórico como
/// <c>Catalogs and Parameters</c> (Fase 6, módulo 3).
/// </summary>
internal sealed record CrearNotificationTemplateCommand(
    string Codigo, NotificationChannel Canal, string Locale, string? Asunto, string Cuerpo) : ICommand<Guid>;

internal sealed class CrearNotificationTemplateCommandValidator : AbstractValidator<CrearNotificationTemplateCommand>
{
    public CrearNotificationTemplateCommandValidator()
    {
        RuleFor(c => c.Codigo).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Locale).NotEmpty().MaximumLength(16);
        RuleFor(c => c.Asunto).MaximumLength(256);
        RuleFor(c => c.Cuerpo).NotEmpty().MaximumLength(4000);
    }
}

internal sealed class CrearNotificationTemplateCommandHandler(
    IRepository<NotificationTemplate, Guid> repository, INotificationsActorContext actorContext, IAuditWriter auditWriter)
    : IRequestHandler<CrearNotificationTemplateCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearNotificationTemplateCommand request, CancellationToken cancellationToken)
    {
        var yaExiste = await repository.AnyAsync(
            new NotificationTemplateBusquedaSpecification(request.Codigo, request.Canal, request.Locale),
            cancellationToken);
        if (yaExiste)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "Notifications.Plantillas.YaExiste",
                $"Ya existe una plantilla activa para código '{request.Codigo}', canal {request.Canal} y locale '{request.Locale}'."));
        }

        var template = new NotificationTemplate(
            Guid.NewGuid(), request.Codigo, request.Canal, request.Locale, request.Asunto, request.Cuerpo);

        await repository.AddAsync(template, cancellationToken);

        _ = await auditWriter.WriteAsync(
            new AuditEntryRequest(
                actor: actorContext.GetCurrentActor(),
                tenantId: null,
                action: "notifications.plantillas.crear",
                resource: new AuditResource("notifications.plantillas", template.Id.ToString()),
                outcome: AuditOutcome.Success,
                reason: null,
                metadata: new Dictionary<string, string?>
                {
                    ["codigo"] = template.Codigo,
                    ["canal"] = template.Canal.ToString(),
                    ["locale"] = template.Locale,
                }),
            cancellationToken);

        return template.Id;
    }
}
