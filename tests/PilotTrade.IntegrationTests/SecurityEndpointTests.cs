namespace PilotTrade.IntegrationTests;

using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;

public class SecurityEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public SecurityEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ConfigureCredentials_Unauthenticated_Returns403()
    {
        var resp = await _client.PostAsJsonAsync("/api/demo/configure-credentials", new { apiKey = "x", apiPassword = "y", identifier = "z", replace = true });
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConfigureCredentials_MissingCsrf_Returns403()
    {
        var resp = await _client.PostAsJsonAsync("/api/demo/configure-credentials", new { apiKey = "x", apiPassword = "y", identifier = "z", replace = true });
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CredentialStatus_ReturnsOnlyExistsBooleans()
    {
        var resp = await _client.GetAsync("/api/demo/credential-status");
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        json.Should().NotContain("Length");
        json.Should().NotContain("Hash");
    }
}