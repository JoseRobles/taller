using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChargesApi.Tests;

/// <summary>
/// Fake authentication handler that auto-succeeds for integration tests.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "testuser@example.com"),
        }, "TestScheme");

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

// Happy-path smoke tests for the charges service.
public class ChargesApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ChargesApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            // Provide a fake Stripe key so PaymentProcessor doesn't throw
            builder.UseSetting("Stripe:ApiKey", "sk_test_fake_key_for_tests");

            builder.ConfigureServices(services =>
            {
                // Replace Entra auth with a fake test scheme
                services.AddAuthentication("TestScheme")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        "TestScheme", _ => { });
            });
        });
    }

    [Fact]
    public async Task CreateChargeReturns201ForAFreshKey()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/charges", new {
            idempotencyKey = "test_fresh_key",
            amount = 12.50m,
            currency = "USD",
            customerEmail = "happy@example.com",
            cardToken = "tok_visa"
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"id\":\"ch_", body);
    }

    [Fact]
    public async Task MissingIdempotencyKeyReturns400()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/charges", new {
            idempotencyKey = "",
            amount = 1.00m,
            currency = "USD",
            customerEmail = "x@y.com",
            cardToken = "tok_visa"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
