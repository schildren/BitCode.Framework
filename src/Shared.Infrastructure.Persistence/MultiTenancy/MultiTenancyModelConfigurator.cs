using System.Linq.Expressions;
using System.Reflection;
using BitCode.Framework.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;

/// <summary>
/// Lógica de filtro global (soft-delete + tenant) extraída de MultiTenantDbContext para que otras
/// bases de DbContext (p.ej. MultiTenantIdentityDbContext, que no puede heredar de
/// MultiTenantDbContext porque ya hereda de IdentityDbContext) la reutilicen sin duplicar código.
/// </summary>
public static class MultiTenancyModelConfigurator
{
    public static void ApplyGlobalFilters(ModelBuilder modelBuilder, Guid tenantId, bool isMultiTenancyEnabled)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            var isSoftDelete = typeof(ISoftDelete).IsAssignableFrom(clrType);
            var isTenantEntity = typeof(ITenantEntity).IsAssignableFrom(clrType);

            if (!isSoftDelete && !isTenantEntity)
            {
                continue;
            }

            var buildFilterMethod = typeof(MultiTenancyModelConfigurator)
                .GetMethod(nameof(BuildGlobalFilter), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(clrType);

            var filter = (LambdaExpression)buildFilterMethod.Invoke(
                null,
                [isSoftDelete, isTenantEntity, tenantId, isMultiTenancyEnabled])!;

            modelBuilder.Entity(clrType).HasQueryFilter(filter);
        }
    }

    private static LambdaExpression BuildGlobalFilter<TEntity>(
        bool isSoftDelete,
        bool isTenantEntity,
        Guid tenantId,
        bool isMultiTenancyEnabled)
        where TEntity : class
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        Expression? body = null;

        if (isSoftDelete)
        {
            Expression notDeleted = Expression.Not(
                Expression.Property(Expression.Convert(parameter, typeof(ISoftDelete)), nameof(ISoftDelete.IsDeleted)));
            body = notDeleted;
        }

        if (isTenantEntity)
        {
            var tenantIdAccess = Expression.Property(
                Expression.Convert(parameter, typeof(ITenantEntity)), nameof(ITenantEntity.TenantId));

            Expression tenantMatch = Expression.OrElse(
                Expression.Constant(!isMultiTenancyEnabled),
                Expression.Equal(tenantIdAccess, Expression.Constant(tenantId)));

            body = body is null ? tenantMatch : Expression.AndAlso(body, tenantMatch);
        }

        body ??= Expression.Constant(true);

        return Expression.Lambda(body, parameter);
    }
}
