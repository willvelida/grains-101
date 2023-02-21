using Microsoft.AspNetCore.Http.Extensions;
using Orleans.Runtime;

var builder = WebApplication.CreateBuilder();

// Configuring the silo
// Can contain one or more grains. A group of silos is called a cluster
// This configures a cluster running on localhost with a silo that can store grains
builder.Host.UseOrleans(siloBuilder =>
{
    siloBuilder.UseLocalhostClustering();
    // This uses AddMemoryGrainStorage to persist grains in memory
    // Can use storage services like Azure Blob storage instead
    siloBuilder.AddMemoryGrainStorage("urls");
});

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapGet("/shorten/{*path}", async (IGrainFactory grains, HttpRequest request, string path) =>
{
    // Create a unique short ID
    var shortenedRouteSegement = Guid.NewGuid().GetHashCode().ToString("X");

    // Create and persist a grain with the shortened ID and full URL
    var shortenerGrain = grains.GetGrain<IUrlShortenerGrain>(shortenedRouteSegement);
    await shortenerGrain.SetUrl(path);

    // Return the shortend URL
    var resultBuilder = new UriBuilder(request.GetEncodedUrl())
    {
        Path = $"/go/{shortenedRouteSegement}"
    };

    return Results.Ok(resultBuilder.Uri);
});

app.MapGet("/go/{shortenedRouteSegement}", async (IGrainFactory grains, string shortenedRouteSegement) =>
{
    // Retrieve the grain using the ID and redirect to original URL
    var shortenerGrain = grains.GetGrain<IUrlShortenerGrain>(shortenedRouteSegement);
    var url = await shortenerGrain.GetUrl();

    return Results.Redirect(url);
});

app.Run();

// Our GrainInterface with the grain key identitifer type
// We use strings since they work for URLs, but you can use Ints, Guids or compond keys.
public interface IUrlShortenerGrain : IGrainWithStringKey
{
    Task SetUrl(string fullUrl);
    Task<string> GetUrl();
}

// Our Grain Class. All grain class inherit form the Grain Base class, which manages internal behaviors and integration points with the Orleans framework
public class UrlShortenerGrain : Grain, IUrlShortenerGrain
{
    private readonly IPersistentState<UrlDetails> _state;

    // This class uses IPersistenceState interface for managing reading and writing state values for the URLs to the storage we configured for our silo.
    public UrlShortenerGrain(
        [PersistentState(stateName: "url",storageName:"urls")] IPersistentState<UrlDetails> state)
    {
        _state= state;
    }

    public Task<string> GetUrl()
    {
        return Task.FromResult(_state.State.FullUrl);
    }

    public async Task SetUrl(string fullUrl)
    {
        _state.State = new UrlDetails() { ShortenedRouteSegement = this.GetPrimaryKeyString(), FullUrl = fullUrl };
        await _state.WriteStateAsync();
    }
}

[GenerateSerializer]
public record UrlDetails
{
    public string FullUrl { get; set; }
    public string ShortenedRouteSegement { get; set; }
}