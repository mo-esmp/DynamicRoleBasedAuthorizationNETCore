using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DynamicAuthorization.AspNetCore.EntityFrameworkCore;

/// <summary>
///   The Identity entity types of a context, found by walking its type hierarchy.
/// </summary>
internal sealed record IdentityModelTypes(Type Context, Type User, Type Role, Type Key, Type UserRole)
{
    public static IdentityModelTypes FromContext(Type contextType)
    {
        // Every IdentityDbContext variant derives from the eight-argument definition.
        for (Type? type = contextType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IdentityDbContext<,,,,,,,>))
            {
                Type[] arguments = type.GetGenericArguments();
                return new IdentityModelTypes(contextType, arguments[0], arguments[1], arguments[2], arguments[4]);
            }
        }

        throw new InvalidOperationException(
            $"'{contextType.FullName}' must derive from IdentityDbContext with roles, for example IdentityDbContext<TUser, TRole, TKey>.");
    }
}

/// <summary>
///   Converts Identity keys from and to their string representation, like the Identity stores do.
/// </summary>
internal static class IdentityKey<TKey>
    where TKey : IEquatable<TKey>
{
    private static readonly TypeConverter s_converter = TypeDescriptor.GetConverter(typeof(TKey));

    public static bool TryParse([NotNullWhen(true)] string? value, [MaybeNullWhen(false)] out TKey key)
    {
        key = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            key = (TKey?)s_converter.ConvertFromInvariantString(value);
            return key is not null;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or NotSupportedException or OverflowException)
        {
            return false;
        }
    }

    public static string Format(TKey key) => Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty;
}

internal static class DbContextExtensions
{
    public static DbSet<RolePermission<TKey>> RolePermissions<TKey>(this DbContext context)
        where TKey : IEquatable<TKey>
    {
        if (context.Model.FindEntityType(typeof(RolePermission<TKey>)) is null)
        {
            throw new InvalidOperationException(
                $"The role permission entity is not part of the model of '{context.GetType().Name}'. Call " +
                "modelBuilder.EnableDynamicAuthorization() in OnModelCreating, after base.OnModelCreating(modelBuilder).");
        }

        return context.Set<RolePermission<TKey>>();
    }

    /// <summary>
    ///   Runs an operation in a transaction that is committed only when the operation succeeds. Uses the context's
    ///   execution strategy so that retrying providers work, and joins an existing transaction when there is one.
    /// </summary>
    public static async Task<TResult> ExecuteInTransactionAsync<TResult>(
        this DbContext context,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
        where TResult : ManagementResult
    {
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        return await context.Database.CreateExecutionStrategy().ExecuteAsync(
            async ct =>
            {
                await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

                TResult result = await operation(ct).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                }

                return result;
            },
            cancellationToken).ConfigureAwait(false);
    }
}
