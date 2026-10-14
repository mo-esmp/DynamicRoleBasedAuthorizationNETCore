using System.ComponentModel;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DynamicAuthorization.AspNetCore.Tests.TestApplication;

[Authorize]
[DisplayName("Orders")]
public sealed class OrdersController : Controller
{
    public IActionResult Index() => Content("orders");

    [HttpGet]
    public IActionResult Edit(int id) => Content($"edit {id}");

    [HttpPost]
    [ActionName("Edit")]
    public IActionResult EditPost(int id) => Content($"saved {id}");

    [AllowAnonymous]
    public IActionResult Public() => Content("public");

    [Permission("orders.delete", DisplayName = "Delete orders")]
    public IActionResult Delete(int id) => Content($"deleted {id}");

    [IgnoreDynamicAuthorization]
    public IActionResult Mine() => Content("mine");
}

public sealed class ReportsController : Controller
{
    public IActionResult Index() => Content("reports");

    [Authorize]
    [DisplayName("Export reports")]
    public IActionResult Export() => Content("export");
}

[Authorize]
public abstract class SecuredControllerBase : Controller;

/// <summary>
///   Inherits [Authorize] and declares two more; DynamicAuthorization.Mvc 1.x threw AmbiguousMatchException here.
/// </summary>
[Authorize]
[Authorize(AuthenticationSchemes = TestAuthHandler.SchemeName)]
[PermissionGroup("Inherited")]
public sealed class InheritedController : SecuredControllerBase
{
    public IActionResult Index() => Content("inherited");
}

[Authorize(Roles = "Manager")]
public sealed class ManagersController : Controller
{
    public IActionResult Index() => Content("managers");
}

[Area("Admin")]
[Authorize]
public sealed class SettingsController : Controller
{
    public IActionResult Index() => Content("settings");
}
