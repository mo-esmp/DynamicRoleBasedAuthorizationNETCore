namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   The permissions granted to a role, indexed by resource when they are created so that finding an operation that
///   implies reading a resource is a lookup instead of a scan. One instance is cached per role.
/// </summary>
internal sealed class PermissionSet
{
    private readonly IReadOnlySet<string> _permissions;
    private readonly Dictionary<string, string>? _byResource;

    public PermissionSet(IReadOnlySet<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        _permissions = permissions;

        // The first granted operation on each resource; any of them implies reading the resource.
        foreach (string permission in permissions)
        {
            (string? resource, _) = PermissionKeys.Split(permission);
            if (resource is not null)
            {
                _byResource ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _byResource.TryAdd(resource, permission);
            }
        }
    }

    /// <summary>
    ///   Gets a set without permissions.
    /// </summary>
    public static PermissionSet Empty { get; } = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public bool Contains(string permission) => _permissions.Contains(permission);

    /// <summary>
    ///   Finds a granted operation on the resource of a <c>{resource}.read</c> permission, which implies reading it.
    /// </summary>
    /// <returns>
    ///   The granted permission, or <see langword="null"/> when <paramref name="permission"/> is not a read permission
    ///   or nothing is granted on its resource.
    /// </returns>
    public string? FindImplying(string permission)
    {
        if (_byResource is null)
        {
            return null;
        }

        (string? resource, string? operation) = PermissionKeys.Split(permission);
        if (resource is null || !string.Equals(operation, PermissionOperation.Read, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return _byResource.TryGetValue(resource, out string? implying) ? implying : null;
    }
}
