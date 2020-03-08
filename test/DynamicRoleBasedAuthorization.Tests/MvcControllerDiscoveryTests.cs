using DynamicAuthorization.Mvc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using Xunit;

namespace DynamicRoleBasedAuthorization.Tests
{
    public class MvcControllerDiscoveryTests
    {
        private readonly TestServer _testServer;

        public MvcControllerDiscoveryTests()
        {
            var builder = new WebHostBuilder().UseStartup<Startup>();
            _testServer = new TestServer(builder);
        }

        [Fact]
        public void When_Controller_Decorated_With_Authorize_Attribute_Should_Return_All_Actions()
        {
            // Arrange
            var actionDescriptorCollectionProvider = _testServer.Services.GetService<IActionDescriptorCollectionProvider>();
            var controllerDiscovery = new MvcControllerDiscovery(actionDescriptorCollectionProvider);

            // Act
            var controllers = controllerDiscovery.GetControllers();
            var actionCount = controllers.SingleOrDefault(c => c.Id == ":Authorized")?.Actions?.Count();

            // Assert
            Assert.NotEmpty(controllers);
            Assert.Equal(2, actionCount);
        }

        [Fact]
        public void When_Controller_Not_Decorated_With_Authorize_Attribute_Should_Return_Only_Authorized_Actions()
        {
            // Arrange
            var actionDescriptorCollectionProvider = _testServer.Services.GetService<IActionDescriptorCollectionProvider>();
            var controllerDiscovery = new MvcControllerDiscovery(actionDescriptorCollectionProvider);

            // Act
            var controllers = controllerDiscovery.GetControllers();
            var actions = controllers.SingleOrDefault(c => c.Id == ":ActionAuthorized")?.Actions;

            // Assert
            Assert.NotEmpty(controllers);
            Assert.NotNull(actions);
            Assert.Equal(1, actions.Count());
            Assert.Equal(":ActionAuthorized:AuthorizedAction", actions.FirstOrDefault().Id);
        }

        [Fact]
        public void When_Area_Exist_Should_Return_Authorized_Controllers_And_Actions()
        {
            // Arrange
            var actionDescriptorCollectionProvider = _testServer.Services.GetService<IActionDescriptorCollectionProvider>();
            var controllerDiscovery = new MvcControllerDiscovery(actionDescriptorCollectionProvider);

            // Act
            var controllers = controllerDiscovery.GetControllers();
            var areaControllers = controllers.Where(c => c.Id.StartsWith("AuthorizedArea"));

            // Assert
            Assert.NotEmpty(areaControllers);
            Assert.Single(areaControllers);
        }
    }
}