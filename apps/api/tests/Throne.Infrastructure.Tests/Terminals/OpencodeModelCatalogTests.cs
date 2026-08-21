using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Throne.Application.Terminals;
using Throne.Infrastructure.Terminals;

namespace Throne.Infrastructure.Tests.Terminals;

public class OpencodeModelCatalogTests
{
    [Fact(DisplayName = "Flatten: только connected-провайдеры, id = provider/model в порядке ответа")]
    public async Task Lists_connected_provider_models_as_slashed_ids()
    {
        var handler = new RecordingHandler("""
            {
              "all": [
                { "id": "opencode", "models": { "gpt-5.1-codex": {}, "claude-sonnet-4-5": {} } },
                { "id": "anthropic", "models": { "claude-sonnet-4-5": {} } },
                { "id": "openai", "models": { "gpt-5.2": {} } }
              ],
              "connected": ["opencode", "anthropic"]
            }
            """);
        var sut = NewCatalog(handler);

        var models = await sut.ListModelsAsync(CancellationToken.None);

        sut.Vendor.Should().Be(TerminalAgentCatalog.VendorOpencode);
        models.Should().Equal(
            "opencode/gpt-5.1-codex",
            "opencode/claude-sonnet-4-5",
            "anthropic/claude-sonnet-4-5");
    }

    [Fact(DisplayName = "GET /provider не 2xx → пустой список, не бросает")]
    public async Task Http_error_surfaces_as_empty_list()
    {
        var sut = NewCatalog(new RecordingHandler("{}", HttpStatusCode.InternalServerError));

        var models = await sut.ListModelsAsync(CancellationToken.None);

        models.Should().BeEmpty();
    }

    [Fact(DisplayName = "Serve не поднимается → пустой список, не бросает")]
    public async Task Serve_failure_surfaces_as_empty_list()
    {
        var handler = new ThrowingHandler();
        var sut = new OpencodeModelCatalog(
            new FailingServeGateway(), new FixedHttpClientFactory(new HttpClient(handler)), NullLogger<OpencodeModelCatalog>.Instance);

        var models = await sut.ListModelsAsync(CancellationToken.None);

        models.Should().BeEmpty();
    }

    private static OpencodeModelCatalog NewCatalog(RecordingHandler handler) =>
        new(
            new FixedServeGateway(),
            new FixedHttpClientFactory(new HttpClient(handler)),
            NullLogger<OpencodeModelCatalog>.Instance);

    private sealed class FixedServeGateway : IOpencodeServeGateway
    {
        public Task<Uri> EnsureRunningAsync(CancellationToken ct) =>
            Task.FromResult(new Uri("http://127.0.0.1:4096/"));
    }

    private sealed class FailingServeGateway : IOpencodeServeGateway
    {
        public Task<Uri> EnsureRunningAsync(CancellationToken ct) =>
            throw new InvalidOperationException("opencode serve did not become healthy.");
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    private sealed class RecordingHandler(string payload, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = JsonContent.Create(JsonNode.Parse(payload)),
            });
    }
}
