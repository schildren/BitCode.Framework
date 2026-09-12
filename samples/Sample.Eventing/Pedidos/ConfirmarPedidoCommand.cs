using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace Sample.Eventing.Pedidos;

/// <summary>
/// F3-13 (prueba de referencia): comando real de aplicación (MediatR/ICommand, mismo patrón que
/// <c>CrearProductoCommand</c> de Sample.Api) que ejercita el pipeline completo — ValidationBehavior,
/// LoggingBehavior y, sobre todo, TransactionBehavior (Shared.Application, F2-05), que hace el
/// SaveChangesAsync real donde el interceptor de Outbox (F1-23) escribe la fila OutboxMessage del
/// evento levantado por <see cref="Pedido.Confirmar"/>, atómicamente con el cambio de negocio.
/// </summary>
public record ConfirmarPedidoCommand(Guid PedidoId, string Cliente, decimal Monto) : ICommand;

public class ConfirmarPedidoCommandValidator : AbstractValidator<ConfirmarPedidoCommand>
{
    public ConfirmarPedidoCommandValidator()
    {
        RuleFor(c => c.Cliente).NotEmpty();
        RuleFor(c => c.Monto).GreaterThan(0);
    }
}

public class ConfirmarPedidoCommandHandler(IRepository<Pedido, Guid> repository)
    : IRequestHandler<ConfirmarPedidoCommand, Result>
{
    public async Task<Result> Handle(ConfirmarPedidoCommand request, CancellationToken cancellationToken)
    {
        var pedido = new Pedido(request.PedidoId, request.Cliente, request.Monto);
        pedido.Confirmar();

        await repository.AddAsync(pedido, cancellationToken);

        return Result.Success();
    }
}
