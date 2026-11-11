using System.Net;

namespace DynamicAuthorization.AspNetCore.Tests.Infrastructure;

internal static class HttpClientExtensions
{
    public static async Task<HttpStatusCode> GetStatusAsync(this HttpClient client, string url) =>
        (await client.GetAsync(url)).StatusCode;

    public static async Task<HttpStatusCode> SendStatusAsync(this HttpClient client, HttpMethod method, string url)
    {
        using HttpRequestMessage request = new(method, url);

        return (await client.SendAsync(request)).StatusCode;
    }
}
