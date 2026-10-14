# Dynamic Authorization for ASP.NET Core

[![CI](https://github.com/mo-esmp/DynamicRoleBasedAuthorizationNETCore/actions/workflows/ci.yml/badge.svg)](https://github.com/mo-esmp/DynamicRoleBasedAuthorizationNETCore/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/DynamicAuthorization.AspNetCore.svg)](https://www.nuget.org/packages/DynamicAuthorization.AspNetCore)

You already know role-based authorization in ASP.NET Core:

```csharp
[Authorize(Roles = "Administrator")]
public class AdministrationController : Controller
{
}
```

But what if you don't want to hard-code roles, and want to create roles later and decide what each one can access, without
touching the source code?

**Dynamic Authorization** turns every `[Authorize]` endpoint into a permission that you grant to roles at runtime. It works with
**MVC controllers, Razor Pages and Minimal APIs**, stores permissions in your **ASP.NET Core Identity database with Entity
Framework Core** (any provider), and ships a **dashboard** to manage roles, permissions and user roles.

## Packages

| Package | Description |
| --- | --- |
| `DynamicAuthorization.AspNetCore` | Permission discovery, the authorization handler, the `secure-content` tag helper and `IPermissionChecker`. |
| `DynamicAuthorization.AspNetCore.EntityFrameworkCore` | Stores role permissions in your Identity `DbContext`, with any EF Core provider. |
| `DynamicAuthorization.AspNetCore.Dashboard` | A Razor Pages dashboard to manage roles and user roles, inspect endpoints and check access. |

Version 2 targets **.NET 10**. Version 1.x (`DynamicAuthorization.Mvc.*`) remains available for older frameworks; see
[Migrating from 1.x](#migrating-from-1x).

## Getting started

### 1. Install the packages

```shell
dotnet add package DynamicAuthorization.AspNetCore.EntityFrameworkCore
dotnet add package DynamicAuthorization.AspNetCore.Dashboard
```

### 2. Add the permissions table to your Identity context

```csharp
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.EnableDynamicAuthorization(); // adds the AspNetRolePermissions table
    }
}
```

Any context that derives from `IdentityDbContext` with roles works, including custom user and role types, other key types
(`int`, `Guid`, ...) and contexts that inherit through your own base class. Then add a migration:

```shell
dotnet ef migrations add AddDynamicAuthorization
```

### 3. Register dynamic authorization

```csharp
builder.Services
    .AddIdentity<IdentityUser, IdentityRole>()            // roles are required
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services
    .AddDynamicAuthorization(options => options.SuperAdminUsers.Add("admin@example.com"))
    .AddEntityFrameworkStore<ApplicationDbContext>()
    .AddDashboard();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapDynamicAuthorizationDashboard();                   // https://your-app/authz
```

That's it: every endpoint protected by `[Authorize]` now requires a permission. Sign in as the super admin, open `/authz`,
create roles, choose their permissions and assign them to users.

## How endpoints are protected

With the default options, the permission check is added to ASP.NET Core's **default authorization policy**, so anything that
uses it is permission-checked, with no attribute changes:

| Endpoint | Protected by | Permission |
| --- | --- | --- |
| MVC action | `[Authorize]` on the controller or action | `mvc:{area}:{controller}:{action}`, e.g. `mvc::Orders:Edit` |
| Razor Page | `[Authorize]` on the page model, or `AuthorizeFolder("/Orders")` | `page:{area}:{page}`, e.g. `page::/Orders/Edit` |
| Minimal API | `RequireAuthorization()` | `endpoint:{name}` when named with `WithName`, otherwise `endpoint:{methods} {route}`, e.g. `endpoint:GET /api/orders` |

One permission covers every HTTP method of an action or page, e.g. both `GET` and `POST` of `Edit`. Keys are compared
case-insensitively.

What is **not** permission-checked:

- `[AllowAnonymous]` endpoints.
- `[Authorize(Roles = "...")]` or `[Authorize(Policy = "...")]` without a plain `[Authorize]`: you already chose static
  authorization for those.
- Endpoints marked `[IgnoreDynamicAuthorization]` or `.IgnoreDynamicAuthorization()`, and areas listed in `IgnoredAreas`
  (`Identity` by default, so the Identity UI account pages stay available). They still require a signed-in user.

### Explicit permissions

Name a permission explicitly to grant several endpoints together, or to keep the permission when you rename the controller,
action or page:

```csharp
[Permission("orders.delete", DisplayName = "Delete orders")]
public IActionResult Delete(int id) { ... }

app.MapDelete("/api/orders/{id}", DeleteOrder).RequirePermission("orders.delete");
```

### Minimal APIs and route groups

```csharp
var api = app.MapGroup("/api").RequireAuthorization();          // each endpoint requires its own permission
api.MapGet("/orders", GetOrders);                                // endpoint:GET /api/orders
api.MapGet("/orders/{id}", GetOrder).WithName("GetOrder");       // endpoint:GetOrder
```

`RequireDynamicAuthorization()` requires the permission even when `ApplyToDefaultPolicy` is disabled.

### Display names and groups

Permissions are shown in the dashboard grouped by controller, page folder or Minimal API tag. Use `[DisplayName]` on
controllers, actions and page models, and `[PermissionGroup]` on controllers and page models, to make them readable:

```csharp
[Authorize, DisplayName("Orders")]
public class OrdersController : Controller
{
    [DisplayName("Edit orders")]
    public IActionResult Edit(int id) { ... }
}
```

### Permissions checked in code

Declare permissions that are not bound to an endpoint, and check them with `IPermissionChecker`, the tag helper, or a policy:

```csharp
builder.Services.AddDynamicAuthorization(options => options.AddPermission("reports.export", "Export reports", "Reports"));

if (await permissionChecker.HasPermissionAsync(User, "reports.export")) { ... }

builder.Services.AddAuthorization(options =>
    options.AddPolicy("CanExport", policy => policy.AddRequirements(new DynamicPermissionRequirement("reports.export"))));
```

## Showing content only to users who may access it

Add the tag helper in `_ViewImports.cshtml`:

```cshtml
@addTagHelper *, DynamicAuthorization.AspNetCore
```

Then wrap content in `secure-content`. Pages and actions are evaluated with their complete authorization policy, exactly as the
request would be, so links to anonymous pages are always shown and links to role-protected pages are shown to role members:

```cshtml
<secure-content asp-page="/Orders/Index">
    <a asp-page="/Orders/Index">Orders</a>
</secure-content>

<secure-content asp-area="Admin" asp-controller="Reports" asp-action="Index">
    <a asp-area="Admin" asp-controller="Reports" asp-action="Index">Reports</a>
</secure-content>

<secure-content asp-permission="reports.export">
    <button>Export</button>
</secure-content>
```

## Super admins

Super admins have every permission, which lets you bootstrap the first administrator before any role exists:

```csharp
options.SuperAdminUsers.Add("admin@example.com"); // by user name
options.SuperAdminRoles.Add("Administrators");    // members of these roles
```

Super admin roles can't be renamed or deleted from the dashboard, and the last member of a super admin role can't be removed
when no super admin user is configured.

## Dashboard

The dashboard is a set of Razor Pages served under `/authz`, styled after the .NET Aspire dashboard, with light and dark themes.
It has no JavaScript or CSS dependencies and works with a strict Content Security Policy.

- **Overview**: roles, users, permissions and unprotected endpoints, plus what needs attention: endpoints without
  authorization, roles without users, and granted permissions that no longer match anything (for example after renaming an
  action).
- **Endpoints**: every page, action and Minimal API with its route, protection, permission and the roles that can access it.
- **Roles**: create, rename and delete roles, and choose their permissions in a searchable tree with an unsaved-changes counter.
- **Users**: search users, assign roles, and see each user's effective permissions and which role grants them.
- **Access check**: pick a user and a permission to see whether access is granted, and why.

```csharp
.AddDashboard(options =>
{
    options.RoutePrefix = "admin/access";      // default: "authz"
    options.Title = "Contoso access control";
    options.AuthorizationPolicy = "Admins";    // default: the dynamic authorization policy
});
```

By default, each dashboard page is itself a permission (in the *Authorization dashboard* group), so you can let a role manage
roles without letting it manage users. Navigation only shows pages the user may open. Treat access to the role and user pages
as administrator access: whoever can edit roles can grant themselves any permission.

## Caching and multiple instances

User roles and role permissions are cached in memory for `CacheDuration` (one minute by default). Changes made through the
dashboard or the management services apply immediately on the instance that made them; other instances, and changes made
elsewhere (for example with `UserManager.AddToRoleAsync`), become visible when the cache expires. Call
`IDynamicAuthorizationCache.InvalidateUser`/`InvalidateRole` after changing roles in your own code, or set `CacheDuration` to
`TimeSpan.Zero` to disable caching.

## Options

| Option | Default | Description |
| --- | --- | --- |
| `SuperAdminUsers` | empty | User names that have every permission. |
| `SuperAdminRoles` | empty | Roles whose members have every permission. |
| `ApplyToDefaultPolicy` | `true` | Permission-check every endpoint that uses the default policy (plain `[Authorize]`). |
| `IgnoredAreas` | `Identity` | Areas that only require a signed-in user. |
| `CacheDuration` | 1 minute | How long user roles and role permissions are cached. |
| `Permissions` / `AddPermission` | empty | Permissions that are not bound to an endpoint. |

`EnableDynamicAuthorization(o => { o.TableName = "..."; o.Schema = "..."; })` customizes the table.

## Migrating from 1.x

| 1.x | 2.x |
| --- | --- |
| `DynamicAuthorization.Mvc.Core` | `DynamicAuthorization.AspNetCore` |
| `DynamicAuthorization.Mvc.JsonStore`, `DynamicAuthorization.Mvc.MsSqlServerStore` | `DynamicAuthorization.AspNetCore.EntityFrameworkCore` |
| `DynamicAuthorization.Mvc.Ui` | `DynamicAuthorization.AspNetCore.Dashboard` |
| `AddDynamicAuthorization<TDbContext>(o => o.DefaultAdminUser = "...")` | `AddDynamicAuthorization(o => o.SuperAdminUsers.Add("..."))` + `.AddEntityFrameworkStore<TDbContext>()` |
| `.AddJsonStore()` / `.AddSqlServerStore(...)` | `.AddEntityFrameworkStore<TDbContext>()` and `modelBuilder.EnableDynamicAuthorization()` |
| `.AddUi(mvcBuilder)`, `/role`, `/userrole` | `.AddDashboard()` + `app.MapDynamicAuthorizationDashboard()`, `/authz` |
| `class MyTagHelper : SecureContentTagHelper<TDbContext, ...>` | Remove it; use `@addTagHelper *, DynamicAuthorization.AspNetCore` |
| `IMvcControllerDiscovery` | `IPermissionCatalogProvider` |
| `IRoleAccessStore` | `IPermissionStore`, `IRoleManagementService` |

Permissions saved by 1.x become `mvc:` permissions. After adding the migration, import them once at startup (imports are
idempotent, merge with existing permissions and skip roles that no longer exist):

```csharp
// From the 1.x JSON store file
await app.Services.ImportDynamicAuthorizationV1JsonStoreAsync("RoleAccess.json");

// From the 1.x SQL Server store's RoleAccess table, read through your DbContext
await app.Services.ImportDynamicAuthorizationV1SqlServerStoreAsync();
```

Version 2 also authorizes Razor Pages and Minimal APIs: Razor Pages protected with `[Authorize]` (including the Identity UI if
you removed `Identity` from `IgnoredAreas`) now require permissions too.

## Sample

[`samples/SampleWebApp`](samples/SampleWebApp) protects Razor Pages, an MVC area and Minimal APIs, uses SQLite with migrations,
and seeds three demo accounts (the password is in `appsettings.json`):

```shell
dotnet run --project samples/SampleWebApp --launch-profile http
```

## Building

```shell
dotnet build DynamicAuthorization.slnx
dotnet test --solution DynamicAuthorization.slnx
```

## License

[MIT](LICENSE)
