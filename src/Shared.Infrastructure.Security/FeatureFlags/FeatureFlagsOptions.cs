namespace BitCode.Framework.Shared.Infrastructure.Security.FeatureFlags;

/// <summary>
/// Opciones enlazadas a la sección <see cref="SectionName"/> de <c>IConfiguration</c> (F4-12). Deriva de
/// <see cref="Dictionary{TKey, TValue}"/> a propósito -- no envuelve la colección en una propiedad
/// intermedia (por ejemplo, <c>Flags</c>) -- porque <c>ConfigurationBinder</c> vincula un tipo que
/// implementa <see cref="IDictionary{TKey, TValue}"/> asignando directamente cada clave hija de la sección
/// como entrada del diccionario. Esto es lo que permite que un <c>ConfigMap</c> declare cada flag como un
/// par plano <c>FeatureFlags__NuevoFlujoDePagos: "true"</c> (mismo patrón de doble guion bajo que el resto
/// de la configuración externalizada del repositorio, F4-02 en adelante) en vez de necesitar un nivel de
/// anidamiento adicional (<c>FeatureFlags__Flags__NuevoFlujoDePagos</c>) que no aportaría nada.
/// </summary>
public sealed class FeatureFlagsOptions : Dictionary<string, bool>
{
    public const string SectionName = "FeatureFlags";

    /// <summary>
    /// Comparación de claves insensible a mayúsculas/minúsculas -- mismo criterio que
    /// <see cref="IFeatureFlagProvider.IsEnabled"/> y que las claves de <c>IConfiguration</c> en general.
    /// </summary>
    public FeatureFlagsOptions() : base(StringComparer.OrdinalIgnoreCase)
    {
    }
}
