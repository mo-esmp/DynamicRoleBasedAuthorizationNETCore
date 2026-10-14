namespace DynamicAuthorization.AspNetCore.Management;

/// <summary>
///   The result of a management operation.
/// </summary>
public class ManagementResult
{
    private static readonly ManagementResult s_ok = new(true, []);

    private protected ManagementResult(bool succeeded, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Errors = errors;
    }

    /// <summary>
    ///   Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    ///   Gets the errors that caused the operation to fail.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    ///   Gets a successful result.
    /// </summary>
    /// <returns>The result.</returns>
    public static ManagementResult Ok() => s_ok;

    /// <summary>
    ///   Gets a successful result with a value.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The result.</returns>
    public static ManagementResult<T> Ok<T>(T value) => new(value, true, []);

    /// <summary>
    ///   Gets a failed result.
    /// </summary>
    /// <param name="errors">The errors.</param>
    /// <returns>The result.</returns>
    public static ManagementResult Fail(params IEnumerable<string> errors) => new(false, [.. errors]);

    /// <summary>
    ///   Gets a failed result for an operation that returns a value.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="errors">The errors.</param>
    /// <returns>The result.</returns>
    public static ManagementResult<T> Fail<T>(params IEnumerable<string> errors) => new(default, false, [.. errors]);
}

/// <summary>
///   The result of a management operation that returns a value.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class ManagementResult<T> : ManagementResult
{
    internal ManagementResult(T? value, bool succeeded, IReadOnlyList<string> errors)
        : base(succeeded, errors) => Value = value;

    /// <summary>
    ///   Gets the value, when the operation succeeded.
    /// </summary>
    public T? Value { get; }
}
