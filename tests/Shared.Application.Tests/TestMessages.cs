using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Shared.Application.Tests;

public record TestCommand(string Name) : ICommand<string>;

public class TestCommandHandler : IRequestHandler<TestCommand, Result<string>>
{
    public Task<Result<string>> Handle(TestCommand request, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success($"Hola, {request.Name}"));
}

public class TestQueryHandler : IRequestHandler<TestQuery, Result<string>>
{
    public Task<Result<string>> Handle(TestQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success($"Consulta: {request.Name}"));
}

public class TestCommandValidator : AbstractValidator<TestCommand>
{
    public TestCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty();
    }
}

public record TestQuery(string Name) : IQuery<string>;

public class NoOpTestCommandValidator : AbstractValidator<TestCommand>
{
    // Sin reglas: representa un validador registrado que nunca falla, para distinguir
    // "sin validadores registrados" de "validadores registrados que aprueban".
}
