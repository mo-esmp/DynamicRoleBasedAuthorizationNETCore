using DynamicRoleBasedAuthorization.Tests.TestSetup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace DynamicRoleBasedAuthorization.Tests
{
    public class DynamicAuthorizationTests
    {
        private readonly HttpClient _httpClient;

        public DynamicAuthorizationTests()
        {
            var builder = new WebHostBuilder().UseStartup<Startup>();
            var testServer = new TestServer(builder);
            _httpClient = testServer.CreateClient();
        }

        [Fact]
        public async Task Default_Admin_User_Can_Access_Any_Authorized_Url()
        {
            // Arrange
            await Login(InitialData.SuperUser.UserName, InitialData.DefaultPassword);

            // Act
            var response = await _httpClient.GetAsync("/authorized/action1");
            var response2 = await _httpClient.GetAsync("/actionAuthorized/AuthorizedAction");

            // Assert
            response.EnsureSuccessStatusCode();
            response2.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task User_Without_Role_Can_Not_Access_Authorized_Url()
        {
            // Arrange
            await Login(InitialData.AdminUser.UserName, InitialData.DefaultPassword);

            // Act
            var response = await _httpClient.GetAsync("/authorized/action1");

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        public async Task Login(string userName, string password)
        {
            var content = new MultipartFormDataContent
            {
                { new StringContent(userName, Encoding.UTF8),  "userName"},
                { new StringContent(password, Encoding.UTF8),"password"}
            };

            var response = await _httpClient.PostAsync("/user/login", content);
            response.EnsureSuccessStatusCode();
            var cookie = response.Headers.GetValues("Set-Cookie");
            _httpClient.DefaultRequestHeaders.Add("cookie", cookie);
        }
    }
}