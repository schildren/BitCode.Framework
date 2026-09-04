using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Shared.Application.Behaviors;

public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(context, cancellationToken)));

        var errors = validationResults
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .Select(failure => Error.Validation(failure.PropertyName, failure.ErrorMessage))
            .Distinct()
            .ToArray();

        if (errors.Length == 0)
        {
            return await next();
        }

        return CreateValidationFailureResult<TResponse>(ValidationError.FromErrors(errors));
    }

    private static TResult CreateValidationFailureResult<TResult>(ValidationError validationError)
        where TResult : Result
    {
        if (typeof(TResult) == typeof(Result))
        {
            return (TResult)(object)Result.Failure(validationError);
        }

        var valueType = typeof(TResult).GetGenericArguments()[0];

        var failureMethod = typeof(Result)
            .GetMethods()
            .Single(m => m.Name == nameof(Result.Failure) && m.IsGenericMethod)
            .MakeGenericMethod(valueType);

        return (TResult)failureMethod.Invoke(null, [validationError])!;
    }
}
