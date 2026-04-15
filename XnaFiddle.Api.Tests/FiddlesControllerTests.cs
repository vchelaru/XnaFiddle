using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XnaFiddle.Api.Dtos;
using XnaFiddle.Api.Slugs;
using XnaFiddle.Api.Tests.Infrastructure;

namespace XnaFiddle.Api.Tests;

public class FiddlesControllerTests
{
    private static CreateFiddleRequest SampleRequest() =>
        new("public class Game1 { }", new List<string> { "https://example.com/tex.png" });

    [Fact]
    public async Task Post_ReturnsCreatedWithLocationHeaderAndBody()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/fiddles", SampleRequest());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();

        var body = await response.Content.ReadFromJsonAsync<FiddleResponse>();
        body.ShouldNotBeNull();
        body!.Slug.Length.ShouldBe(7);
        body.Content.ShouldBe(SampleRequest().Content);
        body.FileReferences.ShouldBe(SampleRequest().FileReferences);
        response.Headers.Location!.ToString().ShouldEndWith($"/api/fiddles/{body.Slug}");
    }

    [Fact]
    public async Task GetBySlug_ReturnsFiddle_AfterPost()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/fiddles", SampleRequest()))
            .Content.ReadFromJsonAsync<FiddleResponse>();

        var fetched = await client.GetFromJsonAsync<FiddleResponse>($"/api/fiddles/{created!.Slug}");

        fetched.ShouldNotBeNull();
        fetched!.Slug.ShouldBe(created.Slug);
        fetched.Content.ShouldBe(created.Content);
        fetched.FileReferences.ShouldBe(created.FileReferences);
    }

    [Fact]
    public async Task GetBySlug_UnknownSlug_Returns404()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/fiddles/NOPE123");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_RetriesOnSlugCollision_AndSucceedsWithNextSlug()
    {
        // First POST will claim "AAAAAAA". Second POST's generator will return
        // "AAAAAAA" (collision) then "BBBBBBB" (success on retry).
        var fakeGen = new QueuedSlugGenerator("AAAAAAA", "AAAAAAA", "BBBBBBB");

        await using var factory = new ApiFactory();
        var client = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<ISlugGenerator>();
            s.AddSingleton<ISlugGenerator>(fakeGen);
        })).CreateClient();

        var first = await client.PostAsJsonAsync("/api/fiddles", SampleRequest());
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await first.Content.ReadFromJsonAsync<FiddleResponse>())!.Slug.ShouldBe("AAAAAAA");

        var second = await client.PostAsJsonAsync("/api/fiddles", SampleRequest());
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await second.Content.ReadFromJsonAsync<FiddleResponse>())!.Slug.ShouldBe("BBBBBBB");
    }
}
