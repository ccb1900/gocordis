using System.Net;
using HttpdHost;
using Xunit;

namespace Gocordis.Product.Tests;

public sealed class HttpdProbeTests
{
    [Fact]
    public async Task HttpdProbeRespondsAndStops()
    {
        // Port 0 asks the host to pick a free port; Address must report the real one.
        await using var host = new HttpdProbeHost("http://127.0.0.1:0");
        await host.StartAsync(CancellationToken.None);

        var address = host.Address;
        Assert.False(string.IsNullOrWhiteSpace(address));
        Assert.True(new Uri(address).Port > 0, $"ephemeral port was not resolved: {address}");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        Assert.Equal("hello", await host.ProbeAsync(client));

        // The wire contract, not just the helper's return value: JSON with one message field.
        using var response = await client.GetAsync($"{address}/hello");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("{\"message\":\"hello\"}", (await response.Content.ReadAsStringAsync()).Trim());

        // StopAsync is the inverse: no listener survives it, and DisposeAsync is safe afterwards.
        await host.StopAsync();
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync($"{address}/hello"));
    }
}
