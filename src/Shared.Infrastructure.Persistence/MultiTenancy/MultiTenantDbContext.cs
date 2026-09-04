using System.Linq.Expressions;
using System.Reflection;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;

public abstract class MultiTenantDbContext : DbContext
{
    private readonly Guid _tenantId;
    private readonly bool _isMultiTenancyEnabled;

    protected MultiTenantDbContext(DbContextOptions options, ITenantProvider tenantProvider) : base(options)
    {
        _tenantId = tenantProvider.TenantId ?? Guid.Empty;
        _isMultiTenancyEnabled = tenantProvider.IsMultiTenancyEnabled;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, PerInstanceModelCacheKeyFactory>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            var isSoftDelete = typeof(ISoftDelete).IsAssignableFrom(clrType);
            var isTenantEntity = typeof(ITenantEntity).IsAssignableFrom(clrType);

            if (!isSoftDelete && !isTenantEntity)
            {
                continue;
            }

            var buildFilterMethod = typeof(MultiTenantDbContext)
                .GetMethod(nameof(BuildGlobalFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(clrType);

            var filter = (LambdaExpression)buildFilterMethod.Invoke(this, [isSoftDelete, isTenantEntity])!;
            modelBuilder.Entity(clrType).HasQueryFilter(filter);
        }
    }

    private LambdaExpression BuildGlobalFilter<TEntity>(bool isSoftDelete, bool isTenantEntity)
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
                Expression.Constant(!_isMultiTenancyEnabled),
                Expression.Equal(tenantIdAccess, Expression.Constant(_tenantId)));

            body = body is null ? tenantMatch : Expression.AndAlso(body, tenantMatch);
        }

        body ??= Expression.Constant(true);

        return Expression.Lambda(body, parameter);
    }
}
