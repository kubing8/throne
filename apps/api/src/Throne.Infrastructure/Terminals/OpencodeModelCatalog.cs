using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Throne.Application.Terminals;

namespace Throne.Infrastructure.Terminals;

/// <summary>
/// <see cref="IVendorModelCatalog"/> for the OpenCode vendor (ADR-0054): the live model list is
/// whatever the operator enabled in their own opencode — Throne never curates it. The shared
/// <c>opencode serve</c> (owned by <see cref="IOpencodeServeGateway"/>, brought up on demand) is
/// asked via <c>GET /provider</c>, and the response is flattened to <c>provider/model</c> ids
/// keeping only providers the operator connected (<c>opencode auth login</c> / the TUI
/// <c>/connect</c> command) — the same curated surface the opencode model picker offers. The
/// operator's enabled model subsets from their opencode config ride along in each provider's
/// <c>models</c> map.
///
/// Any failure (CLI missing, serve that will not start, HTTP/parse error) surfaces as an empty
/// list per the <see cref="IVendorModelCatalog"/> contract: the launch UI disables the model
/// picker for the vendor instead of the whole catalog endpoint failing.
/// </summary>
internal sealed class OpencodeModelCatalog(
    IOpencodeServeGateway serveGateway,
    IHttpClientFactory httpClientFactory,
    ILogger<OpencodeModelCatalog> logger) : IVendorModelCatalog
{
    public string Vendor => TerminalAgentCatalog.VendorOpencode;

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        try
        {
            var endpoint = await serveGateway.EnsureRunningAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "provider"));
            OpencodeServerAuth.Apply(request);
            using var response = await httpClientFactory
                .CreateClient(OpencodeTuiClient.HttpClientName)
                .SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                TerminalsLog.OpencodeModelListUnavailable(
                    logger, $"GET /provider returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                return [];
            }

            var payload = await response.Content
                .ReadFromJsonAsync<OpencodeProviderListResponse>(cancellationToken: ct);
            return Flatten(payload);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            TerminalsLog.OpencodeModelListUnavailable(logger, ex.Message);
            return [];
        }
    }

    private static List<string> Flatten(OpencodeProviderListResponse? payload)
    {
        if (payload?.All is null || payload.Connected is null)
        {
            return [];
        }

        var connected = new HashSet<string>(payload.Connected, StringComparer.Ordinal);
        var models = new List<string>();
        foreach (var provider in payload.All)
        {
            if (provider?.Id is null || provider.Models is null || !connected.Contains(provider.Id))
            {
                continue;
            }

            models.AddRange(provider.Models.Keys.Select(modelId => $"{provider.Id}/{modelId}"));
        }
        return models;
    }

    // Minimal projection of the serve's GET /provider payload — only the fields this catalog
    // reads. Deserialization goes through System.Net.Http.Json's Web defaults, so the camelCase
    // wire names ("all", "connected", "id", "models") bind case-insensitively.
    private sealed record OpencodeProviderListResponse(
        IReadOnlyList<OpencodeProviderEntry>? All,
        IReadOnlyList<string>? Connected);

    private sealed record OpencodeProviderEntry(
        string? Id,
        IReadOnlyDictionary<string, JsonElement>? Models);
}
