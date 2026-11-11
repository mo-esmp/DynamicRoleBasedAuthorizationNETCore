using DynamicAuthorization.AspNetCore.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///   Extension methods for adding the dynamic authorization entities to an Identity model.
/// </summary>
public static class DynamicAuthorizationModelBuilderExtensions
{
    /// <summary>
    ///   Adds the role permissions table to the model. Call it in <c>OnModelCreating</c> after
    ///   <c>base.OnModelCreating(modelBuilder)</c>, then add a migration.
    /// </summary>
    /// <param name="modelBuilder">The model builder of a context that derives from <c>IdentityDbContext</c> with roles.</param>
    /// <param name="configure">Configures the table name and schema.</param>
    /// <returns>The same model builder so that multiple calls can be chained.</returns>
    /// <exception cref="InvalidOperationException">The model does not contain an Identity role entity.</exception>
    public static ModelBuilder EnableDynamicAuthorization(this ModelBuilder modelBuilder, Action<DynamicAuthorizationModelOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        DynamicAuthorizationModelOptions options = new();
        configure?.Invoke(options);

        (IMutableEntityType roleEntity, Type keyType) = FindRoleEntity(modelBuilder.Model)
            ?? throw new InvalidOperationException(
                "No ASP.NET Core Identity role entity was found in the model. Call EnableDynamicAuthorization() after " +
                "base.OnModelCreating(modelBuilder) in a context that derives from IdentityDbContext<TUser, TRole, TKey>.");

        Type permissionType = typeof(RolePermission<>).MakeGenericType(keyType);
        const string roleId = nameof(RolePermission<string>.RoleId);
        const string permission = nameof(RolePermission<string>.Permission);

        modelBuilder.Entity(permissionType, entity =>
        {
            entity.ToTable(options.TableName, options.Schema);
            entity.HasKey(roleId, permission);
            entity.Property(permission).HasMaxLength(DynamicAuthorizationModelOptions.MaxPermissionLength).IsRequired();
            entity.HasIndex(permission);
            entity.HasOne(roleEntity.ClrType).WithMany().HasForeignKey(roleId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        });

        return modelBuilder;
    }

    private static (IMutableEntityType Entity, Type KeyType)? FindRoleEntity(IMutableModel model)
    {
        foreach (IMutableEntityType entity in model.GetEntityTypes())
        {
            if (entity.BaseType is not null)
            {
                continue;
            }

            for (Type? type = entity.ClrType; type is not null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IdentityRole<>))
                {
                    return (entity, type.GetGenericArguments()[0]);
                }
            }
        }

        return null;
    }
}
