namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Implementación por defecto de <see cref="IPermissionCacheInvalidator"/> (F2-09) para un proyecto que
/// no llamó a <see cref="PermissionCacheServiceCollectionExtensions.AddSharedPermissionCache"/> -- no hay
/// ningún cache que invalidar, así que ambos métodos no hacen nada. Registrada con
/// <c>TryAddScoped</c> por <see cref="PermissionEvaluationServiceCollectionExtensions.AddSharedPermissionEvaluation"/>,
/// mismo patrón que <see cref="NullPermissionService"/>.
/// </summary>
public sealed class NullPermissionCacheInvalidator : IPermissionCacheInvalidator
{
    public ValueTask InvalidateUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask InvalidateRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
