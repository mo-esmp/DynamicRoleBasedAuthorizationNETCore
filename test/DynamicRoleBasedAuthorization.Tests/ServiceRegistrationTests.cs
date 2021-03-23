using DynamicAuthorization.Mvc.Core.Extensions;
using DynamicAuthorization.Mvc.JsonStore.Extensions;
using DynamicAuthorization.Mvc.Ui;
using DynamicRoleBasedAuthorization.Tests.TestSetup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace DynamicRoleBasedAuthorization.Tests
{
    public class ServiceRegistrationTests
    {
        [Fact]
        public void Register_DynamicAuthorization_With_TypeOf_DbContext_With_Eight_Generic_Parameter()
        {
            //Arrange
            var builder = new WebHostBuilder().UseStartup<CustomStartup>();

            // Act
            var exception = Record.Exception(() => new TestServer(builder));

            // Assert
            Assert.Null(exception);
        }
    }

    internal class ApplicationUser : IdentityUser<int>
    {
        public string Name { get; set; }

        public virtual ICollection<ApplicationUserRole> UserRoles { get; set; }
    }

    internal class ApplicationRole : IdentityRole<int>
    {
    }

    internal class ApplicationUserRole : IdentityUserRole<int>
    {
        internal ApplicationUser User { get; set; }

        internal ApplicationRole Role { get; set; }
    }

    internal class ApplicationUserClaim : IdentityUserClaim<int>
    {
    }

    internal class ApplicationUserLogin : IdentityUserLogin<int>
    {
    }

    internal class ApplicationRoleClaim : IdentityRoleClaim<int>
    {
    }

    internal class ApplicationUserToken : IdentityUserToken<int>
    {
    }

    internal class CustomDbContext : IdentityDbContext<
        ApplicationUser, ApplicationRole, int,
        ApplicationUserClaim, ApplicationUserRole, ApplicationUserLogin,
        ApplicationRoleClaim, ApplicationUserToken>
    {
        public CustomDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
    }

    internal class CustomStartup
    {
        public CustomStartup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            var mvcBuilder = services.AddControllersWithViews();

            services.AddDbContext<CustomDbContext>(options => options.UseInMemoryDatabase("InMemoryDbForTesting"));

            services.AddIdentity<ApplicationUser, ApplicationRole>(options => options.SignIn.RequireConfirmedAccount = false)
                .AddEntityFrameworkStores<CustomDbContext>()
                .AddDefaultTokenProviders();

            services.ConfigureApplicationCookie(options =>
            {
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

            services.AddDynamicAuthorization<CustomDbContext>(options => options.DefaultAdminUser = InitialData.SuperUser.UserName)
                .AddJsonStore()
                .AddUi(mvcBuilder);

            services.AddScoped<DbInitializer>();
        }

        public void Configure(IApplicationBuilder app, IHostApplicationLifetime applicationLifetime)
        {
            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default-area",
                    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}