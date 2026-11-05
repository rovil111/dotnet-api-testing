using Inventory.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Tests;

/// <summary>Boots the real API on a free port, so tests exercise real HTTP, routing and serialization.</summary>
public sealed class ApiHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public HttpClient Client { get; }

    private ApiHost(WebApplication app, HttpClient client)
    {
        _app = app;
        Client = client;
    }

    public static async Task<ApiHost> StartAsync()
    {
        var app = InventoryApi.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=Warning"]);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new ApiHost(app, new HttpClient { BaseAddress = new Uri(address) });
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
