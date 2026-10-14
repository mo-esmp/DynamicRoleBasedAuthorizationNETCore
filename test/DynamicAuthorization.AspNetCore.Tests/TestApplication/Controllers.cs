using System.ComponentModel;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DynamicAuthorization.AspNetCore.Tests.TestApplication;

/// <summary>
///   A scaffolded MVC controller plus actions that exercise every operation inference rule.
/// </summary>
[Authorize]
[DisplayName("Orders")]
public sealed class OrdersController : Controller
{
    public IActionResult Index() => Content("orders");

    public IActionResult Details(int id) => Content($"details {id}");

    // GET with a name that names no operation: read.
    [HttpGet]
    public IActionResult Summary() => Content("summary");

    [HttpGet]
    public IActionResult Create() => Content("create form");

    [HttpPost]
    [ActionName("Create")]
    public IActionResult CreatePost() => Content("created");

    [HttpGet]
    public IActionResult Edit(int id) => Content($"edit {id}");

    [HttpPost]
    [ActionName("Edit")]
    public IActionResult EditPost(int id) => Content($"saved {id}");

    [HttpGet]
    public IActionResult Delete(int id) => Content($"delete {id}");

    [HttpPost]
    [ActionName("Delete")]
    public IActionResult DeleteConfirmed(int id) => Content($"deleted {id}");

    // POST with a name that names no operation: its own operation, orders.archive.
    [HttpPost]
    public IActionResult Archive(int id) => Content($"archived {id}");

    // Any HTTP method with a name that names no operation: its own operation, orders.report.
    public IActionResult Report() => Content("report");

    [HttpPost]
    [Permission(Operation = PermissionOperation.Delete)]
    public IActionResult Purge() => Content("purged");

    [Permission("orders.export", DisplayName = "Export orders")]
    public IActionResult Download() => Content("download");

    [AllowAnonymous]
    public IActionResult Public() => Content("public");

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

/// <summary>
///   A REST API controller with the method names of the Web API scaffold.
/// </summary>
[ApiController]
[Route("api/products")]
[Authorize]
public sealed class ProductsController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("products");

    [HttpGet("{id:int}")]
    public IActionResult GetById(int id) => Ok(id);

    [HttpPost]
    public IActionResult Post() => Ok("created");

    [HttpPut("{id:int}")]
    public IActionResult Put(int id) => Ok(id);

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => Ok(id);
}

[Authorize]
public abstract class SecuredControllerBase : Controller;

/// <summary>
///   Inherits [Authorize] and declares two more; DynamicAuthorization.Mvc 1.x threw AmbiguousMatchException here.
/// </summary>
[Authorize]
[Authorize(AuthenticationSchemes = TestAuthHandler.SchemeName)]
[PermissionResource("legacy", DisplayName = "Legacy")]
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

/// <summary>
///   Requires roles through <see cref="IAuthorizationRequirementData"/> rather than [Authorize].
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireRolesAttribute(params string[] roles) : Attribute, IAuthorizationRequirementData
{
    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new RolesAuthorizationRequirement(roles)];
}

[RequireRoles("Auditor")]
public sealed class AuditsController : Controller
{
    public IActionResult Index() => Content("audits");
}
