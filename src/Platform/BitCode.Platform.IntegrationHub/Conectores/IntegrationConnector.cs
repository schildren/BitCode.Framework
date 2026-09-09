using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.IntegrationHub.Conectores;

/// <summary>
/// Definición de un endpoint HTTP externo (Fase 6, módulo 9: "conectores" del Plan Maestro).
/// DELIBERADAMENTE un único conector HTTP saliente GENÉRICO y configurable (URL, método, autenticación)
/// -- no una integración productiva con un sistema comercial concreto (SAP, Salesforce, un ESB, etc.):
/// un conector específico de proveedor es responsabilidad de un consumidor real que implemente su propio
/// <see cref="Envio.IIntegrationConnectorSender"/> reutilizando este modelo de configuración, ver
/// <c>docs/guia-integration-hub.md</c>, sección "Conectores".
/// </summary>
/// <remarks>
/// <see cref="SecretKey"/> es una REFERENCIA a un secreto (la clave que
/// <see cref="Shared.Infrastructure.Security.Secrets.ISecretProvider"/> resuelve en el momento de
/// enviar, F2-12) -- NUNCA el valor del secreto en sí: esta entidad no persiste ningún API key/token en
/// texto plano. Ver el <c>remarks</c> de <c>IntegrationHubServiceCollectionExtensions</c> para por qué
/// este módulo no necesitó además <c>IEncryptionProvider</c> (F2-13).
///
/// Implementa <see cref="IHasConcurrencyToken"/> (F1-08): dos operaciones administrativas independientes
/// (<see cref="Activar"/>/<see cref="Desactivar"/>, cada una su propio comando HTTP) pueden llegar casi
/// simultáneamente sobre la misma fila sin ninguna coordinación entre sí. Corrección honesta tras
/// auditoría de arquitectura (2026-09-09): la documentación original de este comentario mencionaba
/// "editar credenciales" como un segundo camino de mutación -- ese comando NO existe en este corte (solo
/// activar/desactivar el mismo booleano `Activo`), y se agregará si un consumidor real lo necesita. El
/// token se mantiene igual porque activar/desactivar por sí solo ya es un caso real de dos escrituras
/// concurrentes plausibles, no porque haga falta "por las dudas" para una funcionalidad que no existe.
/// </remarks>
public sealed class IntegrationConnector : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity, IHasConcurrencyToken
{
    /// <summary>Código lógico estable del conector (por ejemplo <c>"crm-clientes"</c>), único por tenant
    /// -- lo que <c>EnviarSolicitudIntegracionCommand</c> recibe del llamador, nunca el <see cref="Id"/>
    /// interno.</summary>
    public string Codigo { get; private set; } = string.Empty;

    public string Nombre { get; private set; } = string.Empty;

    /// <summary>URL absoluta del endpoint externo. Validada como URI absoluto en
    /// <c>CrearConectorCommandValidator</c> (sanity check de UX, para no persistir un conector obviamente
    /// mal configurado) Y de nuevo en el momento de enviar
    /// (<c>Envio.HttpIntegrationConnectorSender</c>) -- defensa en profundidad: la segunda validación es
    /// la que realmente importa (nunca confiar en que un dato ya persistido siga siendo válido para
    /// construir una llamada real), la primera solo adelanta el error al momento de configuración.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    public MetodoHttpConector Metodo { get; private set; }

    public TipoAutenticacionConector TipoAutenticacion { get; private set; }

    /// <summary><see langword="null"/> cuando <see cref="TipoAutenticacion"/> es
    /// <see cref="TipoAutenticacionConector.Ninguna"/>. Clave de secreto (no el valor) resuelta vía
    /// <see cref="Shared.Infrastructure.Security.Secrets.ISecretProvider"/> en cada envío.</summary>
    public string? SecretKey { get; private set; }

    /// <summary>Solo aplica cuando <see cref="TipoAutenticacion"/> es
    /// <see cref="TipoAutenticacionConector.ApiKey"/>. Default <c>"X-Api-Key"</c> si no se especifica.</summary>
    public string? ApiKeyHeaderName { get; private set; }

    /// <summary>Un conector inactivo nunca se resuelve para procesar una <see cref="Solicitudes.IntegrationRequest"/>
    /// nueva ni pendiente -- mismo criterio que <c>NotificationTemplate.Activa</c>.</summary>
    public bool Activo { get; private set; } = true;

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public IntegrationConnector(
        Guid id,
        string codigo,
        string nombre,
        string baseUrl,
        MetodoHttpConector metodo,
        TipoAutenticacionConector tipoAutenticacion,
        string? secretKey,
        string? apiKeyHeaderName)
        : base(id)
    {
        Codigo = codigo;
        Nombre = nombre;
        BaseUrl = baseUrl;
        Metodo = metodo;
        TipoAutenticacion = tipoAutenticacion;
        SecretKey = secretKey;
        ApiKeyHeaderName = string.IsNullOrWhiteSpace(apiKeyHeaderName) ? "X-Api-Key" : apiKeyHeaderName;
    }

    private IntegrationConnector()
    {
    }

    public void Activar() => Activo = true;

    public void Desactivar() => Activo = false;
}
