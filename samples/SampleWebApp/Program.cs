using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SampleWebApp;
using SampleWebApp.Data;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentity<IdentityUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// Every page in /Orders requires a permission, without an [Authorize] attribute on each page.
builder.Services.AddRazorPages(options => options.Conventions.AuthorizeFolder("/Orders"));
builder.Services.AddControllersWithViews();

builder.Services
    .AddDynamicAuthorization(options =>
    {
        // The super admin can access everything, including the dashboard, before any role exists.
        options.SuperAdminUsers.Add(SeedData.AdminUserName);

        // A permission that is checked in code rather than bound to an endpoint. Named {resource}.{operation}, it is
        // listed with the other operations on orders in the dashboard.
        options.AddPermission("orders.export", "Export orders");
    })
    .AddEntityFrameworkStore<ApplicationDbContext>()
    .AddDashboard();

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapControllerRoute("areas", "{area:exists}/{controller=Home}/{action=Index}/{id?}");

// Minimal APIs share the "orders" resource with the /Orders pages: GET requires orders.read, DELETE orders.delete.
RouteGroupBuilder api = app.MapGroup("/api/orders").RequireAuthorization().WithTags("Orders API");
api.MapGet("", () => SampleOrders.All).WithSummary("List orders");
api.MapDelete("/{id:int}", (int id) => Results.Ok(new { deleted = id })).WithSummary("Delete an order");

app.MapDynamicAuthorizationDashboard();

await SeedData.InitializeAsync(app.Services, app.Configuration);

app.Run();
