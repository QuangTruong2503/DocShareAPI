using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DocShareAPI.Tests;

public class CorsErrorResponseTests
{
    [Theory]
    [InlineData("https://docshare.id.vn", true)]
    [InlineData("https://untrusted.example", false)]
    public async Task UnhandledFailureReturnsReadableErrorOnlyForAllowedOrigin(string origin, bool allowed)
    {
        using var server = new TestServer(new WebHostBuilder()
            .UseEnvironment("Production")
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddProblemDetails();
                services.AddCors(options => options.AddPolicy("AllowSpecificOrigins", policy =>
                    policy.WithOrigins("https://docshare.id.vn").AllowAnyHeader().AllowAnyMethod()));
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseCors("AllowSpecificOrigins");
                app.UseExceptionHandler();
                app.Run(_ => throw new InvalidOperationException("private email provider failure"));
            }));
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Users/request-enable-2fa");
        request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("private email provider failure", await response.Content.ReadAsStringAsync());
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
            Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }
}
