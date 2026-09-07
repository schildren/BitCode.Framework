using BitCode.Framework.Shared.Infrastructure.Observability;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Gateway.RateLimiting;
using BitCode.Gateway.Security;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Telemetría (F3-10/F4-08): instrumenta ASP.NET Core + HttpClient. YARP reenvía el request usando el
// stack HTTP estándar de .NET (SocketsHttpHandler/DiagnosticsHandler): el Activity/traceparent (W3C
// Trace Context) del request entrante se propaga automáticamente al request proxyado hacia el
// backend sin código adicional -- lo mismo que ya hace cualquier HttpClient instrumentado del
// framework (ver Shared.Infrastructure.Observability). Sección "OpenTelemetry" obligatoria (ServiceName).
builder.Services.AddSharedObservability(builder.Configuration);

// Auth boundary (F4-08): el Gateway valida el JWT del request ANTES de proxyar -- reutiliza
// AddSharedJwtBearerAuthentication (Shared.Infrastructure.Security), el mismo criterio de validación
// (issuer/audience/firma/vigencia) que usa AddSharedSecurity, sin duplicar la construcción de
// TokenValidationParameters ni requerir Identity/EF Core (el Gateway no posee usuarios propios).
// Sección "Jwt" obligatoria (SecretKey/Issuer/Audience) -- ver docs/guia-rbac-2.md para el mecanismo
// de emisión/validación completo.
builder.Services.AddSharedJwtBearerAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// Headers (F4-08): X-Forwarded-For/Proto/Host del hop ANTERIOR (cliente -> este Gateway, p.ej. un
// Ingress/LB delante) se procesan acá para que HttpContext.Connection/Request reflejen el origen
// real -- sin KnownProxies/KnownNetworks explícitos, ForwardedHeadersMiddleware solo confía en
// loopback por defecto (comportamiento seguro; un despliegue real detrás de un Ingress/LB conocido
// declara sus proxies confiables vía configuración de cada overlay, fuera de alcance de esta tarea).
// YARP agrega POR SU CUENTA (comportamiento por defecto de LoadFromConfig, sin transform adicional)
// los X-Forwarded-* del hop SIGUIENTE (este Gateway -> backend) a cada request proxyado.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;
});

// Rate limiting (F4-08): fixed window global, política "gateway" (GatewayRateLimitingOptions,
// sección "RateLimiting" -- configurable, no hardcodeada).
builder.Services.AddGatewayRateLimiting(builder.Configuration);

// Routing (F4-08): rutas/clusters declarados en la sección "ReverseProxy" (ReverseProxy:Routes/
// ReverseProxy:Clusters, formato estándar de YARP) -- externalizable por ambiente, ningún host/ruta
// hardcodeado en código. AddTransforms agrega SensitiveHeaderSanitizingTransform a TODAS las rutas
// configuradas (headers internos que un cliente externo nunca debe poder inyectar hacia el backend).
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transformBuilderContext =>
        transformBuilderContext.RequestTransforms.Add(new SensitiveHeaderSanitizingTransform()));

var app = builder.Build();

app.UseForwardedHeaders();

// Liveness propio del Gateway: solo confirma que el proceso .NET responde -- no depende de que el/los
// backend(s) proxyados estén disponibles (mismo criterio que /health/live de Shared.Infrastructure.Web,
// F1-25 -- no se referencia ese paquete acá para mantener el Gateway liviano, ver BitCode.Gateway.csproj).
app.MapGet("/health/live", () => Results.Ok());

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Auth boundary + rate limiting aplicados a TODO lo que YARP proxya: un request sin token válido, o
// que supera la política de rate limiting, nunca llega a reenviarse al backend.
app.MapReverseProxy()
    .RequireAuthorization()
    .RequireRateLimiting(GatewayRateLimitingServiceCollectionExtensions.PolicyName);

app.Run();

public partial class Program;
