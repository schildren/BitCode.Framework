using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace Sample.Api.Productos;

public record CrearProductoCommand(string Nombre, decimal Precio) : ICommand<Guid>;

public class CrearProductoCommandValidator : AbstractValidator<CrearProductoCommand>
{
    public CrearProductoCommandValidator()
    {
        RuleFor(c => c.Nombre).NotEmpty();
        RuleFor(c => c.Precio).GreaterThan(0);
    }
}

// No llama IUnitOfWork.SaveChangesAsync explícitamente: TransactionBehavior ya lo hace después de
// que el handler retorna un Result exitoso. Este comando modifica un único agregado en una sola
// operación, por lo que no necesita ser ITransactionalCommand (transacción explícita con rollback
// coordinado); ver Shared.Application/Behaviors/TransactionBehavior.cs y
// Shared.Application/Messaging/ICommand.cs.
public class CrearProductoCommandHandler(IRepository<Producto, Guid> repository)
    : IRequestHandler<CrearProductoCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearProductoCommand request, CancellationToken cancellationToken)
    {
        var producto = new Producto(Guid.NewGuid(), request.Nombre, request.Precio);
        await repository.AddAsync(producto, cancellationToken);

        return producto.Id;
    }
}
