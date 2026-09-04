namespace BitCode.Framework.Shared.Infrastructure.Caching;

public class CachingOptions
{
    public const string SectionName = "Caching";

    /// <summary>Connection string de Redis para la capa L2. Si es null/vacío, HybridCache opera
    /// solo con la capa L1 en memoria (válido para una única instancia del proyecto).</summary>
    public string? RedisConnectionString { get; set; }
}
