using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicAuthorization.AspNetCore.Dashboard;

/// <summary>
///   The base class of the dashboard pages.
/// </summary>
/// <remarks>
///   Services are resolved on demand rather than through the constructor so that the pages can return 404 when the
///   dashboard is not enabled, instead of failing to activate.
/// </remarks>
internal abstract class DashboardPageModel : PageModel
{
    /// <inheritdoc />
    /// <remarks>
    ///   <see cref="NonHandlerAttribute"/> keeps Razor Pages from treating this <c>On…Async</c> override as a page
    ///   handler for a "Page" HTTP method.
    /// </remarks>
    [NonHandler]
    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (HttpContext.RequestServices.GetService<DashboardMarker>() is null)
        {
            context.Result = NotFound();
            return;
        }

        await next().ConfigureAwait(false);
    }

    private protected T GetService<T>()
        where T : notnull => HttpContext.RequestServices.GetRequiredService<T>();

    private protected void SetStatus(string message) => TempData[DashboardConstants.StatusKey] = message;

    private protected void SetError(IEnumerable<string> errors) => TempData[DashboardConstants.ErrorKey] = string.Join(" ", errors);
}
