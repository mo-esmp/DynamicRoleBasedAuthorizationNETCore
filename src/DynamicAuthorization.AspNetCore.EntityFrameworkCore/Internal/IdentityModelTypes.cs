using System.ComponentModel;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

    /// <summary>
    ///   Formats an identifier the way <see cref="Format"/> does, so that identifiers written differently (for example
    ///   a GUID in upper case) refer to the same cache entry. Identifiers that are not valid keys are returned as is.
    /// </summary>
    public static string Normalize(string id) => TryParse(id, out TKey? key) ? Format(key) : id;
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
    /// <remarks>
    ///   A retry runs on the same context as the attempt that failed. The entities that attempt began to track are
    ///   detached first; entities that were tracked before the operation keep the state the attempt left them in.
    /// </remarks>
    /// <param name="context">The context.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <param name="isolationLevel">
    ///   The isolation level of a new transaction, for operations whose checks must not interleave with concurrent
    ///   transactions; the provider's default otherwise.
    /// </param>
    public static async Task<TResult> ExecuteInTransactionAsync<TResult>(
        this DbContext context,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken,
        IsolationLevel? isolationLevel = null)
        where TResult : ManagementResult
    {
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        // What a failed attempt began to track was rolled back with its transaction, or is not needed again when only
        // the acknowledgement of its commit was lost. Left in the change tracker, the retry would save it once more.
        HashSet<object>? tracked = strategy.RetriesOnFailure
            ? new(context.ChangeTracker.Entries().Select(e => e.Entity), ReferenceEqualityComparer.Instance)
            : null;
        bool retrying = false;

        return await strategy.ExecuteAsync(
            async ct =>
            {
                if (retrying && tracked is not null)
                {
                    foreach (EntityEntry entry in context.ChangeTracker.Entries().Where(e => !tracked.Contains(e.Entity)).ToList())
                    {
                        entry.State = EntityState.Detached;
                    }
                }

                retrying = true;

                await using IDbContextTransaction transaction = isolationLevel is { } level
                    ? await context.Database.BeginTransactionAsync(level, ct).ConfigureAwait(false)
                    : await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

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
