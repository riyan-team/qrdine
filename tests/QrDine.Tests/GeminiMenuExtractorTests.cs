using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using QrDine.Domain.Common;
using QrDine.Infrastructure.Menu;
using Xunit;

namespace QrDine.Tests;

/// <summary>Google's models are often briefly overloaded. A busy model must not stop a menu being read.</summary>
public class GeminiMenuExtractorTests
{
    private const string Menu = """{"candidates":[{"content":{"parts":[{"text":"{\"categories\":[{\"name\":\"Biryani\",\"items\":[{\"name\":\"Chicken Biryani\",\"price\":350}]}]}"}]}}]}""";
    private const string HighDemand = """{"error":{"code":503,"message":"This model is currently experiencing high demand.","status":"UNAVAILABLE"}}""";

    private sealed class FakeGoogle(Func<string, (HttpStatusCode, string)> answer) : HttpMessageHandler
    {
        public List<string> Models { get; } = new();
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            var model = path[(path.LastIndexOf('/') + 1)..].Split(':')[0];
            Models.Add(model);
            var (status, body) = answer(model);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static GeminiMenuExtractor Reader(FakeGoogle google, string? key = "test-key", string? fallbacks = "b") =>
        new(new HttpClient(google), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = key, ["Gemini:Model"] = "a", ["Gemini:FallbackModels"] = fallbacks, ["Gemini:RetryDelayMs"] = "0",
        }).Build(), NullLogger<GeminiMenuExtractor>.Instance);

    private static Task<QrDine.Application.Menu.MenuImportPreview> Read(GeminiMenuExtractor r) => r.Extract(new byte[] { 1, 2, 3 }, "image/jpeg");

    [Fact]
    public async Task A_busy_model_gets_one_more_go_then_the_next_model_reads_the_menu()
    {
        var google = new FakeGoogle(m => m == "a" ? (HttpStatusCode.ServiceUnavailable, HighDemand) : (HttpStatusCode.OK, Menu));
        var menu = await Read(Reader(google));
        Assert.Equal(new[] { "a", "a", "b" }, google.Models);
        Assert.Equal("Chicken Biryani", menu.Categories.Single().Items.Single().Name);
    }

    [Fact]
    public async Task A_busy_model_that_recovers_is_used()
    {
        var calls = 0;
        var google = new FakeGoogle(_ => ++calls == 1 ? (HttpStatusCode.ServiceUnavailable, HighDemand) : (HttpStatusCode.OK, Menu));
        await Read(Reader(google));
        Assert.Equal(new[] { "a", "a" }, google.Models);
    }

    [Fact]
    public async Task A_used_up_limit_moves_to_the_next_model()
    {
        var google = new FakeGoogle(m => m == "a" ? ((HttpStatusCode)429, """{"error":{"code":429,"status":"RESOURCE_EXHAUSTED"}}""") : (HttpStatusCode.OK, Menu));
        await Read(Reader(google));
        Assert.Equal(new[] { "a", "a", "b" }, google.Models);
    }

    [Fact]
    public async Task A_retired_model_is_skipped_straight_away()
    {
        var google = new FakeGoogle(m => m == "a" ? (HttpStatusCode.NotFound, """{"error":{"code":404,"message":"models/a is not found"}}""") : (HttpStatusCode.OK, Menu));
        await Read(Reader(google));
        Assert.Equal(new[] { "a", "b" }, google.Models);
    }

    [Fact]
    public async Task A_refused_key_stops_at_once()
    {
        var google = new FakeGoogle(_ => (HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT"}}"""));
        var e = await Assert.ThrowsAsync<DomainException>(() => Read(Reader(google)));
        Assert.Equal(new[] { "a" }, google.Models);
        Assert.Contains("tell Dine In", e.Message);
    }

    [Fact]
    public async Task A_photo_google_cant_use_stops_at_once()
    {
        var google = new FakeGoogle(_ => (HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"Unable to process input image.","status":"INVALID_ARGUMENT"}}"""));
        var e = await Assert.ThrowsAsync<DomainException>(() => Read(Reader(google)));
        Assert.Equal(new[] { "a" }, google.Models);
        Assert.Contains("different photo", e.Message);
    }

    [Fact]
    public async Task When_every_model_is_busy_the_owner_is_told_to_try_again_soon()
    {
        var google = new FakeGoogle(_ => (HttpStatusCode.ServiceUnavailable, HighDemand));
        var e = await Assert.ThrowsAsync<DomainException>(() => Read(Reader(google)));
        Assert.Equal(new[] { "a", "a", "b" }, google.Models);
        Assert.Contains("very busy", e.Message);
    }

    [Fact]
    public async Task Without_fallbacks_set_the_lite_model_is_the_backup()
    {
        var google = new FakeGoogle(m => m == "a" ? (HttpStatusCode.ServiceUnavailable, HighDemand) : (HttpStatusCode.OK, Menu));
        var reader = new GeminiMenuExtractor(new HttpClient(google), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = "test-key", ["Gemini:Model"] = "a", ["Gemini:RetryDelayMs"] = "0",
        }).Build(), NullLogger<GeminiMenuExtractor>.Instance);
        await Read(reader);
        Assert.Equal(new[] { "a", "a", "gemini-flash-lite-latest" }, google.Models);
    }

    [Fact]
    public async Task The_key_travels_in_a_header_never_in_the_address()
    {
        var google = new FakeGoogle(_ => (HttpStatusCode.OK, Menu));
        await Read(Reader(google));
        var request = google.Requests.Single();
        Assert.Equal("test-key", request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("key=", request.RequestUri!.Query);
    }

    [Fact]
    public async Task No_key_means_no_call_and_a_plain_message()
    {
        var google = new FakeGoogle(_ => (HttpStatusCode.OK, Menu));
        var e = await Assert.ThrowsAsync<DomainException>(() => Read(Reader(google, key: "")));
        Assert.Empty(google.Models);
        Assert.Contains("spreadsheet import", e.Message);
    }
}
