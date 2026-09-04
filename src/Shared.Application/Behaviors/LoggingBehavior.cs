using System.Diagnostics;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BitCode.Framework.Shared.Application.Behaviors;

public class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    LoggingBehaviorOptions? options = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    private readonly LoggingBehaviorOptions _options = options ?? new LoggingBehaviorOptions();

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation("Iniciando {RequestName}", requestName);

        var response = await next();

        stopwatch.Stop();

        if (response.IsSuccess)
        {
            logger.LogInformation(
                "{RequestName} completado en {ElapsedMilliseconds}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);
        }
        else
        {
            logger.LogWarning(
                "{RequestName} falló en {ElapsedMilliseconds}ms con error {ErrorCode}: {ErrorDescription}",
                requestName,
                stopwatch.ElapsedMilliseconds,
                response.Error.Code,
                response.Error.Description);
        }

        if (stopwatch.ElapsedMilliseconds > _options.SlowRequestThresholdMilliseconds)
        {
            logger.LogWarning(
                "{RequestName} superó el umbral de lentitud ({ElapsedMilliseconds}ms > {ThresholdMilliseconds}ms)",
                requestName,
                stopwatch.ElapsedMilliseconds,
                _options.SlowRequestThresholdMilliseconds);
        }

        return response;
    }
}

public class LoggingBehaviorOptions
{
    public long SlowRequestThresholdMilliseconds { get; set; } = 500;
}
