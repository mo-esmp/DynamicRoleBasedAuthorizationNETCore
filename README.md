# Dynamic Role-Based Authorization in ASP.NET Core 2.0
Nuget package comming soon, checkout [dev branch](https://github.com/mo-esmp/DynamicRoleBasedAuthorizationNETCore/tree/dev)
You already know how role-based authorization wokrs in ASP.NET Core.

```c#
[Authorize(Roles = "Administrator")]
public class AdministrationController : Controller
{
}
```

But what if you don't want hardcode roles in authorization attribute or create roles later and specify in which controller and action it has access without touching source code ?

Create ASP.NET Core Web Application project and change authentication to Individual User Accounts. 

![create project](https://raw.githubusercontent.com/mo-esmp/DynamicRoleBasedAuthorizationNETCore/master/assets/create-project.jpg)

After creating project first thing we need is to find all controllers inside project. Add two new classes `MvcControllerInfo` and `MvcActionInfo` inside `Models` folder:

```c#
public class MvcControllerInfo
{
    public string Id => $"{AreaName}:{Name}";

    public string Name { get; set; }

    public string DisplayName { get; set; }

    public string AreaName { get; set; }

    public IEnumerable<MvcActionInfo> Actions { get; set; }
}

public class MvcActionInfo
{
    public string Id => $"{ControllerId}:{Name}";

    public string Name { get; set; }

    public string DisplayName { get; set; }

    public string ControllerId { get; set; }
}
```

Add another class `MvcControllerDiscovery` to `Services` folder to discover all controllers and actions:

```c#
public class MvcControllerDiscovery : IMvcControllerDiscovery
{
    private List<MvcControllerInfo> _mvcControllers;
    private readonly IActionDescriptorCollectionProvider _actionDescriptorCollectionProvider;

    public MvcControllerDiscovery(IActionDescriptorCollectionProvider actionDescriptorCollectionProvider)
    {
        _actionDescriptorCollectionProvider = actionDescriptorCollectionProvider;
    }

    public IEnumerable<MvcControllerInfo> GetControllers()
    {
        if (_mvcControllers != null)
            return _mvcControllers;

        _mvcControllers = new List<MvcControllerInfo>();
        
        var items = _actionDescriptorCollectionProvider
            .ActionDescriptors.Items
            .Where(descriptor => descriptor.GetType() == typeof(ControllerActionDescriptor))
            .Select(descriptor => (ControllerActionDescriptor)descriptor)
            .GroupBy(descriptor => descriptor.ControllerTypeInfo.FullName)
            .ToList();

        foreach (var actionDescriptors in items)
        {
            if (!actionDescriptors.Any())
                continue;

            var actionDescriptor = actionDescriptors.First();
            var controllerTypeInfo = actionDescriptor.ControllerTypeInfo;
            var currentController = new MvcControllerInfo
            {
                AreaName = controllerTypeInfo.GetCustomAttribute<AreaAttribute>()?.RouteValue,
                DisplayName = controllerTypeInfo.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName,
                Name = actionDescriptor.ControllerName,
            };

            var actions = new List<MvcActionInfo>();
            foreach (var descriptor in actionDescriptors.GroupBy(a => a.ActionName).Select(g => g.First()))
            {
                var methodInfo = descriptor.MethodInfo;
                actions.Add(new MvcActionInfo
                {
                    ControllerId = currentController.Id,
                    Name = descriptor.ActionName,
                    DisplayName = methodInfo.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName,
                });
            }

            currentController.Actions = actions;
            _mvcControllers.Add(currentController);
        }

        return _mvcControllers;
    }
}
```
`IActionDescriptorCollectionProvider` provides the cached collection of `ActionDescriptor` which each descriptor represnts an action. Open `Startup` class and inside `Configure` method and register `MvcControllerDiscovery` dependency.

```c#
services.AddSingleton<IMvcControllerDiscovery, MvcControllerDiscovery>();
```

It's time to add role controller to manage roles. In `Controller` folder create `RoleController` then add `Create` action:

```
public class RoleController : Controller
{
    private readonly IMvcControllerDiscovery _mvcControllerDiscovery;

    public RoleController(IMvcControllerDiscovery mvcControllerDiscovery)
    {
        _mvcControllerDiscovery = mvcControllerDiscovery;
    }

    // GET: Role/Create
    public ActionResult Create()
    {
        ViewData["Controllers"] = _mvcControllerDiscovery.GetControllers();

        return View();
    }
}
```
Go to `Models` folder and add `RoleViewModel` class:
```c#
public class RoleViewModel
{
    [Required]
    [StringLength(256, ErrorMessage = "The {0} must be at least {2} characters long.")]
    public string Name { get; set; }

    public IEnumerable<MvcControllerInfo> SelectedControllers { get; set; }
}
```
And in `View` folder add another folder and name it `Role` then add `Create.cshtml` view. I used [jqeury.bonsai](https://github.com/aexmachina/jquery-bonsai) for showing controller and action hierarchy.

```
@model RoleViewModel

@{
    ViewData["Title"] = "Create Role";
    var controllers = (IEnumerable<MvcControllerInfo>)ViewData["Controllers"];
}

@section Header {
    <link href="~/lib/jquery-bonsai/jquery.bonsai.css" rel="stylesheet" />
}

<h2>Create Role</h2>

<hr />
<div class="row">
    <div class="col-md-6">
        <form asp-action="Create" class="form-horizontal">
            <div asp-validation-summary="ModelOnly" class="text-danger"></div>
            <div class="form-group">
                <label asp-for="Name" class="control-label col-md-2"></label>
                <div class="col-md-10">
                    <input asp-for="Name" class="form-control" />
                    <span asp-validation-for="Name" class="text-danger"></span>
                </div>
            </div>
            <div class="form-group">
                <label class="col-md-3 control-label">Access List</label>
                <div class="col-md-9">
                    <ol id="tree">
                        @foreach (var controller in controllers)
                        {
                            string name;
                            {
                                name = controller.DisplayName ?? controller.Name;
                            }
                            <li class="controller" data-value="@controller.Name">
                                <input type="hidden" class="area" value="@controller.AreaName" />
                                @name
                                @if (controller.Actions.Any())
                                {
                                    <ul>
                                        @foreach (var action in controller.Actions)
                                        {
                                            {
                                                name = action.DisplayName ?? action.Name;
                                            }
                                            <li data-value="@action.Name">@name</li>
                                        }
                                    </ul>
                                }
                            </li>
                        }
                    </ol>
                </div>
            </div>
            <div class="form-group">
                <input type="submit" value="Create" class="btn btn-default" />
            </div>
        </form>
    </div>
</div>

<div>
    <a asp-action="Index">Back to List</a>
</div>

@section Scripts {
    @{await Html.RenderPartialAsync("_ValidationScriptsPartial");}
    <script src="~/lib/jquery-qubit/jquery.qubit.js"></script>
    <script src="~/lib/jquery-bonsai/jquery.bonsai.js"></script>
    <script>
        $(function () {
            $('#tree').bonsai({
                expandAll: false,
                checkboxes: true,
                createInputs: 'checkbox'
            });

            $('form').submit(function () {
                var i = 0, j = 0;
                $('.controller > input[type="checkbox"]:checked, .controller > input[type="checkbox"]:indeterminate').each(function () {
                    var controller = $(this);
                    if ($(controller).prop('indeterminate')) {
                        $(controller).prop("checked", true);
                    }
                    var controllerName = 'SelectedControllers[' + i + ']';
                    $(controller).prop('name', controllerName + '.Name');

                    var area = $(controller).next().next();
                    $(area).prop('name', controllerName + '.AreaName');

                    $('ul > li > input[type="checkbox"]:checked', $(controller).parent()).each(function () {
                        var action = $(this);
                        var actionName = controllerName + '.Actions[' + j + '].Name';
                        $(action).prop('name', actionName);
                        j++;
                    });
                    j = 0;
                    i++;
                });

                return true;
            });
        });
    </script>
}
```

![create role](https://raw.githubusercontent.com/mo-esmp/DynamicRoleBasedAuthorizationNETCore/master/assets/create-role.jpg)

It's time to save role but before that we need to do some changes. 

* First create new class `ApplicationRole` inside `Models` folder:
```c#
public class ApplicationRole : IdentityRole
{
    public string Access { get; set; }
}
```

* Open `ApplicationDbContext` change it to:
```c#
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
```

* Open `Startup` class and inside `Configure` method change `services.AddIdentity...` to:
```
services.AddIdentity<ApplicationUser, ApplicationRole>()
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();
```
* finally add new EF migration, in nuget Package Manager Console run `Add-Migration RoleAccessAdded` command and new migration will be added to `Data->Migrations` folder.

Go back to the `RoleController` and add post method of create action.

```c#
private readonly IMvcControllerDiscovery _mvcControllerDiscovery;
private readonly RoleManager<ApplicationRole> _roleManager;

public RoleController(IMvcControllerDiscovery mvcControllerDiscovery, RoleManager<ApplicationRole> roleManager)
{
    _mvcControllerDiscovery = mvcControllerDiscovery;
    _roleManager = roleManager;
}

// POST: Role/Create
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<ActionResult> Create(RoleViewModel viewModel)
{
    if (!ModelState.IsValid)
    {
        ViewData["Controllers"] = _mvcControllerDiscovery.GetControllers();
        return View(viewModel);
    }

    var role = new ApplicationRole { Name = viewModel.Name };
    if (viewModel.SelectedControllers != null && viewModel.SelectedControllers.Any())
    {
        foreach (var controller in viewModel.SelectedControllers)
            foreach (var action in controller.Actions)
                action.ControllerId = controller.Id;

        var accessJson = JsonConvert.SerializeObject(viewModel.SelectedControllers);
        role.Access = accessJson;
    }

    var result = await _roleManager.CreateAsync(role);
    if (result.Succeeded)
        return RedirectToAction(nameof(Index));

    foreach (var error in result.Errors)
        ModelState.AddModelError("", error.Description);

    ViewData["Controllers"] = _mvcControllerDiscovery.GetControllers();

    return View(viewModel);
}
```
Selected controllers serialized into json and stored in role `Access` property. You can decorate controllers and actions with `DisplayName` attribute to show user more meaningful name instead of controller and action name.

```c#
[DisplayName("Access Management")]
public class AccessController : Controller
{

    // GET: Access
    [DisplayName("Access List")]
    public async Task<ActionResult> Index()
}
```

Next step is assigning roles to users. Add new view model and name it [UserRoleViewModel](https://github.com/mo-esmp/DynamicRoleBasedAuthorizationNETCore/blob/master/src/DynamicRoleBasedAuthorization/Models/RoleViewModel.cs) and new controller [AccessController](https://github.com/mo-esmp/DynamicRoleBasedAuthorizationNETCore/blob/master/src/DynamicRoleBasedAuthorization/Controllers/AccessController.cs). `AccessController` is straightforward has nothing complicated.

After assigning roles to users now we can check a user has permission to access a controller and action or not. Add new folder `Filters` then add new class `DynamicAuthorizationFilter` to the folder and `DynamicAuthorizationFilter` inherits `IAsyncAuthorizationFilter`.

```c#
public class DynamicAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly ApplicationDbContext _dbContext;

    public DynamicAuthorizationFilter(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!IsProtectedAction(context))
            return;

        if (!IsUserAuthenticated(context))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var actionId = GetActionId(context);
        var userName = context.HttpContext.User.Identity.Name;

        var roles = await (
            from user in _dbContext.Users
            join userRole in _dbContext.UserRoles on user.Id equals userRole.UserId
            join role in _dbContext.Roles on userRole.RoleId equals role.Id
            where user.UserName == userName
            select role
        ).ToListAsync();

        foreach (var role in roles)
        {
            var accessList = JsonConvert.DeserializeObject<IEnumerable<MvcControllerInfo>>(role.Access);
            if (accessList.SelectMany(c => c.Actions).Any(a => a.Id == actionId))
                return;
        }

        context.Result = new ForbidResult();
    }

    private bool IsProtectedAction(AuthorizationFilterContext context)
    {
        if (context.Filters.Any(item => item is IAllowAnonymousFilter))
            return false;

        var controllerActionDescriptor = (ControllerActionDescriptor)context.ActionDescriptor;
        var controllerTypeInfo = controllerActionDescriptor.ControllerTypeInfo;
        var actionMethodInfo = controllerActionDescriptor.MethodInfo;

        var authorizeAttribute = controllerTypeInfo.GetCustomAttribute<AuthorizeAttribute>();
        if (authorizeAttribute != null)
            return true;

        authorizeAttribute = actionMethodInfo.GetCustomAttribute<AuthorizeAttribute>();
        if (authorizeAttribute != null)
            return true;

        return false;
    }

    private bool IsUserAuthenticated(AuthorizationFilterContext context)
    {
        return context.HttpContext.User.Identity.IsAuthenticated;
    }

    private string GetActionId(AuthorizationFilterContext context)
    {
        var controllerActionDescriptor = (ControllerActionDescriptor)context.ActionDescriptor;
        var area = controllerActionDescriptor.ControllerTypeInfo.GetCustomAttribute<AreaAttribute>()?.RouteValue;
        var controller = controllerActionDescriptor.ControllerName;
        var action = controllerActionDescriptor.ActionName;

        return $"{area}:{controller}:{action}";
    }
}
```
* `IsProtectedAction` checks if requested controller and action has `Authorize` attribute or not and if controller has `Authorize` attribute, action has `AllowAnonymous` attribute or not because we don't want check access on unprotected controllers and actions.
* `IsUserAuthenticated` checks user whether is authenticated or not and if user is not authenticated `UnauthorizedResult` will be returned.
* then we fetch user roles and check if those roles has access to requested controller or not and if user has not access `ForbidResult` will be returned.

Now we need to register this filter golbaly in `Startup` class and modify `services.AddMvc()` to this:

```c#
services.AddMvc(options => options.Filters.Add(typeof(DynamicAuthorizationFilter)));
```

That's it now we are able to create role and assign roles to user and check user access on each request.

And finally we need a custom `TageHelper` to check whether user has access to view links or not.

```c#
[HtmlTargetElement("secure-content")]
public class SecureContentTagHelper : TagHelper
{
    private readonly ApplicationDbContext _dbContext;

    public SecureContentTagHelper(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HtmlAttributeName("asp-area")]
    public string Area { get; set; }

    [HtmlAttributeName("asp-controller")]
    public string Controller { get; set; }

    [HtmlAttributeName("asp-action")]
    public string Action { get; set; }

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        var user = ViewContext.HttpContext.User;

        if (!user.Identity.IsAuthenticated)
        {
            output.SuppressOutput();
            return;
        }

        var roles = await (
            from usr in _dbContext.Users
            join userRole in _dbContext.UserRoles on usr.Id equals userRole.UserId
            join role in _dbContext.Roles on userRole.RoleId equals role.Id
            where usr.UserName == user.Identity.Name
            select role
        ).ToListAsync();

        var actionId = $"{Area}:{Controller}:{Action}";

        foreach (var role in roles)
        {
            var accessList = JsonConvert.DeserializeObject<IEnumerable<MvcControllerInfo>>(role.Access);
            if (accessList.SelectMany(c => c.Actions).Any(a => a.Id == actionId))
                return;
        }

        output.SuppressOutput();
    }
}
```
In each view wrap anchor tag inside `secure-content` tag:

```html
<ul class="nav navbar-nav">
    <li><a asp-area="" asp-controller="Home" asp-action="Index">Home</a></li>
    <li><a asp-area="" asp-controller="Home" asp-action="About">About</a></li>
    <li><a asp-area="" asp-controller="Home" asp-action="Contact">Contact</a></li>
    
    <secure-content asp-area="" asp-controller="Role" asp-action="Index">
        <li><a asp-area="" asp-controller="Role" asp-action="Index">Role</a></li>
    </secure-content>
    <secure-content asp-area="" asp-controller="Access" asp-action="Index">
        <li><a asp-area="" asp-controller="Access" asp-action="Index">Access</a></li>
    </secure-content>
</ul>
```
</secure-content>


#### `secure-content` tage helper borrowed from [DNTIdentity](https://github.com/VahidN/DNTIdentity).
