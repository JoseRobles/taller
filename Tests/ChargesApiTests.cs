using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ChargesApi.Tests;

// Happy-path smoke tests for the charges service.
// These tests currently pass. That doesn't mean the service is correct.
public class ChargesApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ChargesApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

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
