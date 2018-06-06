# DynamicRoleBasedAccessNetCore
You already know how role-based authorization wokrs in ASP.NET Core.

```c#
[Authorize(Roles = "Administrator")]
public class AdministrationController : Controller
{
}
```

But what if you don't want hardcode roles in authorization attribute or create roles later and specify in which controller and action it has access without touching source code ?

Create ASP.NET Core Web Application project and change authentication to Individual User Accounts. After creating project first thing we need is to find all controllers inside project. Add two new classes `MvcControllerInfo` and `MvcActionInfo` inside `Models` folder:

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
