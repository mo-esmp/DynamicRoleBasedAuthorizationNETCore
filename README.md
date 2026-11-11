# Dynamic Authorization for ASP.NET Core

[![CI](https://github.com/mo-esmp/Dynamic-Authorization/actions/workflows/ci.yml/badge.svg)](https://github.com/mo-esmp/Dynamic-Authorization/actions/workflows/ci.yml)
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

![dynamic-auth.jpg (2288×1008)](https://raw.githubusercontent.com/mo-esmp/Dynamic-Authorization/refs/heads/main/assets/dynamic-auth.jpg)

**Dynamic Authorization** turns every `[Authorize]` endpoint into a permission that you grant to roles at runtime. It works with
**MVC controllers, Razor Pages and Minimal APIs**, stores permissions in your **ASP.NET Core Identity database with Entity
Framework Core** (any provider), and ships a **dashboard** to manage roles, permissions and user roles.

## Packages

| Package | Description |
| --- | --- |
| `DynamicAuthorization.AspNetCore` | Permission discovery, the authorization handler, the `secure-content` tag helper and `IPermissionChecker`. |
| `DynamicAuthorization.AspNetCore.EntityFrameworkCore` | Stores role permissions in your Identity `DbContext`, with any EF Core provider. |
| `DynamicAuthorization.AspNetCore.Dashboard` | A Razor Pages dashboard to manage roles and user roles, inspect endpoints and check access. |
| `DynamicAuthorization.AspNetCore.HybridCache` | Caches user roles and role permissions in `HybridCache`, shared between instances through a distributed cache. |

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

That's it: every endpoint protected by `[Authorize]` now requires a permission, such as `orders.read` or `orders.update`.
Sign in as the super admin, open `/authz`, create roles, choose their permissions and assign them to users.

## Permissions: resources and operations

Permissions are named `{resource}.{operation}`. The **resource** comes from the controller, the Razor Pages folder or the
route. The **operation** is `read`, `create`, `update` or `delete`, or a custom operation such as `archive`:

| Endpoint | Permission |
| --- | --- |
| `OrdersController.Index`, `.Details`, any other `[HttpGet]` action | `orders.read` |
| `OrdersController.Create` (GET and POST) | `orders.create` |
| `OrdersController.Edit` (GET and POST) | `orders.update` |
| `OrdersController.Delete` (GET and POST) | `orders.delete` |
| `[HttpPost] OrdersController.Archive` | `orders.archive` |
| `[Area("Admin")] ReportsController.Index` | `admin.reports.read` |
| `/Orders/Index` and `/Orders/Details` pages | `orders.read` |
| `/Orders/Edit` page, `OnGet` and `OnPost` | `orders.update` |
| `/Orders/Index` page, `OnPostArchive` handler | `orders.archive` |
| `GET /api/orders`, `GET /api/orders/{id}` | `orders.read` |
| `POST /api/orders` | `orders.create` |
| `PUT` or `PATCH /api/orders/{id}` | `orders.update` |
| `DELETE /api/orders/{id}` | `orders.delete` |
| `POST /api/orders/{id}/ship` | `orders.ship` |

The operation is inferred in this order; the first rule that matches wins:

1. An override: `[Permission(...)]`, `.RequirePermission(...)` or `.RequirePermissionOperation(...)`.
2. The name of the action, page, page handler or endpoint (`WithName`): `Index`, `Details`, `List` and `Get…` mean read;
   `Create`, `Add…`, `New…` and `Post…` create; `Edit`, `Update…`, `Put…` and `Patch…` update; `Delete…` and `Remove…` delete.
3. The HTTP method, when it is unambiguous: GET means read, PUT and PATCH update, DELETE delete, and a POST to a collection
   (`POST /api/orders`) create.
4. Otherwise, the operation is named after the action, page handler or last route segment: `POST Orders/Approve` requires
   `orders.approve`. A POST is never guessed to be a create.

Endpoints with the same resource share its permissions: `orders.read` grants the read actions of `OrdersController`, the
pages in `/Orders` and `GET /api/orders` alike. Areas are part of the resource (`admin.reports`). A Minimal API's resource
is the first route segment after `api` and a version segment, so `/api/v2/orders` is `orders` too.

**Any operation implies read.** A role granted `orders.update` can also open the pages that require `orders.read`, such
as the list that links to the edit form. Set `OperationsImplyRead = false` to require read explicitly.

**Razor Page handlers are authorized separately.** `OnPostArchive` on the orders page requires `orders.archive` although
`OnGet` only requires `orders.read`.

### Overriding the convention

```csharp
[Permission("orders.approve")]                        // an explicit permission
public IActionResult Approve(int id) { ... }

[HttpPost]
[Permission(Operation = PermissionOperation.Update)]  // the derived resource with another operation: orders.update
public IActionResult Reopen(int id) { ... }

[PermissionResource("sales-orders", DisplayName = "Sales orders")]  // another resource for the whole controller
public class OrdersController : Controller { ... }

app.MapPost("/api/orders/{id}/approve", Approve).RequirePermission("orders.approve");
app.MapPost("/api/orders/{id}/reopen", Reopen).RequirePermissionOperation(PermissionOperation.Update);
app.MapGroup("/api/v1/sales").WithPermissionResource("orders");
```

`[Permission]` also works on page handler methods, for example on `OnPostApprove`.

### One permission per endpoint

To grant each action, page and route individually instead, use the endpoint convention:

```csharp
options.Convention = PermissionConvention.Endpoint;
```

Permissions are then named `mvc:{area}:{controller}:{action}` (`mvc::Orders:Edit`), `page:{area}:{page}`
(`page::/Orders/Edit`), and `endpoint:{name}` or `endpoint:{methods} {route}` (`endpoint:GET /api/orders`). All handlers of
a page share its permission.

### What is permission-checked

With the default options, the permission check is added to ASP.NET Core's **default authorization policy**. Every endpoint
protected by a plain `[Authorize]`, `RequireAuthorization()` or `AuthorizeFolder()` requires a permission, with no
attribute changes. What is **not** permission-checked:

- `[AllowAnonymous]` endpoints.
- `[Authorize(Roles = "...")]` or `[Authorize(Policy = "...")]` without a plain `[Authorize]`: you already chose static
  authorization for those.
- Endpoints marked `[IgnoreDynamicAuthorization]` or `.IgnoreDynamicAuthorization()`, and areas listed in `IgnoredAreas`
  (`Identity` by default, so the Identity UI account pages stay available). They still require a signed-in user.

`RequireDynamicAuthorization()` requires the permission even when `ApplyToDefaultPolicy` is disabled.

Your own policies are permission-checked when they include the requirement: `options.AddPolicy("Managers", p =>
p.RequireRole("Manager").AddRequirements(new DynamicPermissionRequirement()))` requires the role and the endpoint's
permission. Inline policies passed to `RequireAuthorization(policy)` replace the default policy, as in ASP.NET Core, so
they are permission-checked only when they include the requirement. The fallback policy can require an explicit
permission (`new DynamicPermissionRequirement("app.access")`) but not the permission of the endpoint: endpoints without
authorization metadata have none, and startup fails with an explanation.

A SignalR hub is one endpoint, although `MapHub` maps two: `MapHub<ChatHub>("/chat").RequireAuthorization()` requires
`chat.access` for the hub and for `/chat/negotiate`, which clients call first.

Razor runtime compilation is supported. Its pages are compiled when they are first described, for example when the
dashboard lists the endpoints, rather than on their first request, because only a compiled page has its authorization,
page model attributes and handlers.

### Display names

The dashboard names resources after the controller's `[DisplayName]` (or its name), the page folder or the Minimal API tag.
Set `[PermissionResource(..., DisplayName = "...")]` to rename one. Custom operations take the action's `[DisplayName]`:

```csharp
[Authorize, DisplayName("Orders")]
public class OrdersController : Controller
{
    [HttpPost, DisplayName("Approve orders")]
    public IActionResult Approve(int id) { ... }
}
```

### Permissions checked in code

Declare permissions that are not bound to an endpoint, and check them with `IPermissionChecker`, the tag helper, or a
policy. Name them `{resource}.{operation}` to list them with their resource in the dashboard:

```csharp
builder.Services.AddDynamicAuthorization(options => options.AddPermission("orders.export", "Export orders"));

if (await permissionChecker.HasPermissionAsync(User, "orders.export")) { ... }

builder.Services.AddAuthorization(options =>
    options.AddPolicy("CanExport", policy => policy.AddRequirements(new DynamicPermissionRequirement("orders.export"))));
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

<secure-content asp-page="/Orders/Index" asp-page-handler="Archive">
    <button form="archive">Archive</button>
</secure-content>

<secure-content asp-permission="orders.export">
    <button>Export</button>
</secure-content>
```

## Super admins

Super admins have every permission, which lets you bootstrap the first administrator before any role exists:

```csharp
options.SuperAdminUsers.Add("admin@example.com"); // by username
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
- **Roles**: create, rename and delete roles, and choose their permissions in a resources × operations matrix (read, create,
  update, delete and custom operations), with a filter and an unsaved-changes counter.
- **Users**: search users, assign roles, and see each user's effective permissions and which role grants them.
- **Access check**: pick a user and a permission to see whether access is granted, and why.

```csharp
.AddDashboard(options =>
{
    options.RoutePrefix = "admin/access";      // default: "authz"
    options.Title = "Contoso access control";
    options.AuthorizationPolicy = "Admins";    // default: the dynamic authorization policy
    options.UserSearch = UserSearchMatch.Contains; // default: StartsWith, which the Identity indexes can answer
});
```

By default, the dashboard is protected by permissions like any other part of the application: `authz.roles.read`,
`authz.roles.create`, `authz.roles.update`, `authz.roles.delete`, `authz.users.read`, `authz.users.update`, and read access to
the overview, endpoints and access check. You can let a role view roles without letting it change them, or manage roles
without managing users. Navigation and buttons only show what the user may do. Treat the role and user permissions as
administrator access: whoever can edit roles can grant themselves any permission.

### Searching users

The users page, like `IUserManagementService.GetUsersAsync`, matches the search text at the start of the username and email
address by default (`UserSearchMatch.StartsWith`). That becomes `LIKE 'TEXT%'` on the normalized columns, which Identity
indexes, so the count and the page can seek the indexes instead of reading every user. Whether the database does depends
on the collation of the two columns:

- **SQL Server** seeks the Identity indexes as they are.
- **SQLite** compares `LIKE` without regard to case, so its indexes must do the same: give `NormalizedUserName` and
  `NormalizedEmail` the `NOCASE` collation, as the sample does.
- **PostgreSQL** cannot use a B-tree index for `LIKE` under a linguistic collation: give the two columns the `C`
  collation, or add an index with the `varchar_pattern_ops` operator class.

```csharp
builder.Entity<IdentityUser>(user =>
{
    user.Property(u => u.NormalizedUserName).UseCollation("NOCASE"); // "C" on PostgreSQL
    user.Property(u => u.NormalizedEmail).UseCollation("NOCASE");
});
```

Add a migration after changing the collation. `UserSearchMatch.Contains` finds the text anywhere but reads every user on
each search, whatever the collation.

## Caching and multiple instances

User roles and role permissions are cached for `CacheDuration` (one minute by default), in the memory of the instance unless
you register another cache (see below). Changes made through the dashboard or the management services apply immediately on
the instance that made them; other instances, and changes made elsewhere (for example with `UserManager.AddToRoleAsync`),
become visible when the cache expires. Call `IDynamicAuthorizationCache.InvalidateUserAsync`/`InvalidateRoleAsync` after
changing roles in your own code, or set `CacheDuration` to `TimeSpan.Zero` to disable caching.

Concurrent requests for the same user or role share one load, and a change made while a load is in progress is never
hidden by it. Loads run in a service scope of their own, outside any ambient transaction, so they read committed data
only and never depend on the request that started them. When you call the management services inside your own
transaction, invalidate the affected role or user (or call `InvalidateAllAsync`) after committing: the services cannot see
when your transaction completes.

### Sharing the cache between instances

With more than one instance, cache the data in `HybridCache` on a distributed cache instead, so that every instance reads
the same entries and an invalidation on one instance reaches the others. Install `DynamicAuthorization.AspNetCore.HybridCache`,
register a distributed cache, and call `UseHybridCache()`:

```csharp
builder.Services.AddStackExchangeRedisCache(options => options.Configuration = "localhost:6379");

builder.Services
    .AddDynamicAuthorization()
    .AddEntityFrameworkStore<ApplicationDbContext>()
    .UseHybridCache();
```

`UseHybridCache()` registers `HybridCache` with its defaults when the application has not; configure it with
`services.AddHybridCache(options => ...)` if you need to. Entries live in the distributed cache for `CacheDuration` and in
the memory of each instance for `LocalCacheDuration` (five seconds by default, `UseHybridCache(o => o.LocalCacheDuration = ...)`).
An invalidation writes a marker to the cache, for the user or role or for everything, and removes the entry from the
distributed cache; every read compares an entry with the markers, so the other instances see the invalidation within
`LocalCacheDuration`, and a load that was running on any instance when it happened is not kept. `HybridCache` reads one key at a time, so the
permissions of roles that are not cached are loaded one role at a time; each load is shared by every instance that reads
the role. Keys and tags start with `KeyPrefix` (`dynamic-authorization:` by default); applications that share a distributed
cache but not their users and roles must use different prefixes.

To use another cache, implement `IDynamicAuthorizationCacheStore` and register it before `AddDynamicAuthorization()`, or
replace the registration afterwards. The interface documents the contract permission checks rely on: concurrent reads of
one key share one load, an invalidation during a load discards its result, failed loads are not kept, and a load runs to
completion when the caller that started it is cancelled. The cache tests in the repository run against every
implementation.

The permissions of roles that are not cached are loaded with one store call: the Entity Framework store runs one query,
and a custom `IPermissionStore` can implement `GetPermissionsAsync(IEnumerable<string> roleIds, ...)` to do the same.
Within one request, the roles of the user and the permissions of those roles are resolved once, however many
`secure-content` tags and permission checks the request makes, and no longer than the cache keeps them: a scope that
outlives a request, for example one held by a background service, sees changes made elsewhere when the cache does.

## Options

| Option | Default | Description |
| --- | --- | --- |
| `SuperAdminUsers` | empty | User names that have every permission. |
| `SuperAdminRoles` | empty | Roles whose members have every permission. |
| `Convention` | `Crud` | `Crud`: one permission per resource and operation (`orders.read`). `Endpoint`: one per action, page or route. |
| `OperationsImplyRead` | `true` | Any operation on a resource also grants `{resource}.read`. |
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

1.x granted roles individual actions. After adding the migration, import them once at startup, after the endpoints are
mapped. Each action is granted the permissions its endpoints require today, for example `orders.update` for `Orders/Edit`.
Imports are idempotent, merge with existing permissions and skip roles that no longer exist:

```csharp
// From the 1.x JSON store file. For the 1.x SQL Server store's RoleAccess table, read through your DbContext,
// call ImportDynamicAuthorizationV1SqlServerStoreAsync() instead.
DynamicAuthorizationV1ImportResult result = await app.Services.ImportDynamicAuthorizationV1JsonStoreAsync("RoleAccess.json");

foreach (string widened in result.WidenedAccess)
{
    app.Logger.LogWarning("Review: {Widened}", widened);
}
```

CRUD permissions can grant more than a 1.x action did: a role that could open `Orders/Index` but not `Orders/Details` gets
`orders.read`, which covers both. `WidenedAccess` lists every such case (for example
`Editors: orders.read also grants mvc::Orders:Details`), and `UnmatchedActions` lists 1.x actions that no longer exist. With
`Convention = PermissionConvention.Endpoint`, roles get exactly their 1.x actions.

Version 2 also authorizes Razor Pages and Minimal APIs: Razor Pages protected with `[Authorize]` (including the Identity UI if
you removed `Identity` from `IgnoredAreas`) now require permissions too.

## Sample

[`samples/SampleWebApp`](https://github.com/mo-esmp/Dynamic-Authorization/tree/main/samples/SampleWebApp) protects
Razor Pages, an MVC area and Minimal APIs, uses SQLite with migrations, and seeds three demo accounts (the password is
in `appsettings.json`):

```shell
dotnet run --project samples/SampleWebApp --launch-profile http
```

## Building

```shell
dotnet build DynamicAuthorization.slnx
dotnet test --solution DynamicAuthorization.slnx
```

CI also fails on code that differs from `.editorconfig`. Check it with the first command and fix what it reports with
the second:

```shell
dotnet format DynamicAuthorization.slnx --verify-no-changes
dotnet format DynamicAuthorization.slnx
```

## License

[MIT](https://github.com/mo-esmp/Dynamic-Authorization/blob/main/LICENSE)
