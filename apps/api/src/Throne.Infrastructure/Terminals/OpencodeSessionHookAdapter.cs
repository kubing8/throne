using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Throne.Application.Terminals;

namespace Throne.Infrastructure.Terminals;

/// <summary>
/// OpenCode flavour of <see cref="ISessionHookAdapter"/>. Four concerns, expressed through
/// workspace-local files the OpenCode CLI auto-discovers at startup
/// (it searches the working directory upward to the nearest git root):
/// <list type="bullet">
///   <item>System-context delivery: OpenCode loads <c>instructions</c>-listed files as
///   ambient guidance, so the assembled rules block is written next to the config and
///   referenced from there — no inlining on the spawn argv. (Mirrors why the Claude/Codex
///   adapters route their multi-KB rules through a file: tmux's spawn imsg has a ~16 KB
///   cap.) The workspace <c>opencode.json</c> may be a file the cloned repo itself owns, so
///   Throne merges its instruction references into it instead of clobbering the repo's own
///   OpenCode settings; stale <c>throne-session.*</c> entries from a previous spawn of the
///   same intent are replaced idempotently.</item>
///   <item>Model wiring: providers/models are the operator's own opencode surface (their
///   auth + enabled models) — Throne writes no <c>provider</c> section at all. The launch-axis
///   model arrives as a <c>provider/model</c> id (live catalog, ADR-0054): it is pinned on the
///   initial prompt body (<c>prompt_async model={providerID,modelID}</c>) and also set as the
///   workspace config's top-level <c>model</c> default, so a bare front and later
///   operator-typed prompts resolve to the same choice.</item>
///   <item>Lifecycle hooks: OpenCode 1.17.7+ auto-loads project plugins from
///   <c>.opencode/plugins</c>, so the adapter writes a per-session shim that maps OpenCode
///   lifecycle events onto the existing Throne hook endpoint.</item>
/// </list>
///
/// Session delivery is native (<see cref="INativeSessionInitializer"/>): the loop runs in the
/// shared <c>opencode serve</c> (owned by <see cref="IOpencodeServeGateway"/>), the prompt is
/// submitted server-side, and the operator pane only attaches to the returned session id.
/// </summary>
internal sealed class OpencodeSessionHookAdapter(
    SessionHookOptions hookOptions,
    ISessionSkillMaterializer skillMaterializer,
    IOpencodeServeGateway serveGateway,
    IOpencodeTuiClient tuiClient) : ISessionHookAdapter, INativeSessionInitializer
{
    private const string ConfigFileName = "opencode.json";
    private const string SystemPromptFileName = "throne-session.append-system-prompt.txt";
    private const string SchemaUrl = "https://opencode.ai/config.json";

    /// <summary>Every per-session file this adapter drops into the workspace starts with this
    /// prefix — the instruction list rewrite uses it to replace stale spawn entries.</summary>
    private const string ThroneFilePrefix = "throne-session.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public string Vendor => TerminalAgentCatalog.VendorOpencode;

    public string? ReadinessHookEvent => TerminalHookEvents.SessionReady;

    public async Task<IReadOnlyList<string>> PrepareSpawnArgsAsync(
        string intentId,
        string workspacePath,
        string mode,
        string? systemPrompt,
        IReadOnlyList<SessionSkillPackage> skillPackages,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);

        Directory.CreateDirectory(workspacePath);

        await OpencodePluginShim.WriteAsync(
            workspacePath, intentId, mode, NormalizeBaseUrl(hookOptions.ApiBaseUrl), ct);
        var materialization = await skillMaterializer.MaterializeAsync(
            workspacePath, TerminalAgentCatalog.VendorOpencode, skillPackages, ct);

        var systemPromptPath = await WriteSystemPromptAsync(workspacePath, systemPrompt, ct);
        var skillHints = materialization.Skills
            .Select(skill => skill.SkillFileName)
            .OfType<string>()
            .ToArray();
        await WriteConfigAsync(workspacePath, InstructionFiles(systemPromptPath, skillHints), ct);

        // The agent runs in the shared `opencode serve`, not in this pane — the spawn argv carries
        // no server/model flags. The attach argv is produced later by InitializeSessionAsync, once
        // the session exists and its id is known.
        return [];
    }

    public async Task<IReadOnlyList<string>> InitializeSessionAsync(
        string intentId,
        string workspacePath,
        string model,
        string? userPrompt,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        var (providerId, modelId) = SplitModelId(model);

        // Pin the launch-axis model as the workspace default before anything resolves config in
        // this directory: the initial prompt below pins it on the message body explicitly, while
        // a bare front (empty task) and later operator-typed prompts fall back to the config
        // default and land on the same model.
        await WriteDefaultModelAsync(workspacePath, model, ct);

        var endpoint = await serveGateway.EnsureRunningAsync(ct);

        // `opencode attach <url> [--session <id>]`: the front pulls the session by id (no race).
        // Drop the trailing slash so the url matches the CLI's `http://host:port` shape; --dir keeps
        // the front's own config discovery aligned with the session's workspace.
        var attach = new List<string>
        {
            "attach",
            endpoint.AbsoluteUri.TrimEnd('/'),
            "--dir",
            workspacePath,
        };

        // Empty task → boot the front bare against the workspace; the operator types the first
        // prompt. A non-empty task is created + submitted server-side now, then pinned via --session.
        if (!string.IsNullOrWhiteSpace(userPrompt))
        {
            var sessionId = await tuiClient.CreateSessionAndSubmitAsync(
                endpoint, workspacePath, providerId, modelId, userPrompt!, ct);
            attach.Add("--session");
            attach.Add(sessionId);
        }

        return attach;
    }

    // OpenCode initial prompt delivery uses the provider-native TUI HTTP API. The glyph predicate is
    // intentionally disabled so only Claude/Codex keep using capture-pane readiness for tmux paste.
    public bool IsTuiReady(string paneSnapshot) => false;

    // No tmux paste path on the native-session route — RunPreflightSpawn skips the prompt-submit
    // confirmer for INativeSessionInitializer adapters, so this predicate is never queried.
    public bool IsPromptSubmitted(string paneSnapshot) => false;

    // Per-session files live inside the workspace (`opencode.json` + the system-prompt file),
    // so the intent-done workspace teardown reaps them. The shared serve is host-scoped and is
    // intentionally left running for other intents — nothing per-intent to tear down here.
    public Task CleanupAsync(string intentId, CancellationToken ct) => Task.CompletedTask;

    private static async Task<string?> WriteSystemPromptAsync(
        string workspacePath, string? systemPrompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            return null;
        }
        var path = Path.Combine(workspacePath, SystemPromptFileName);
        await File.WriteAllTextAsync(path, systemPrompt!, ct);
        return path;
    }

    // OpenCode model ids are `provider/model` on the wire (the live catalog is flattened from the
    // serve's provider surface, ADR-0054); the prompt API wants the pair split.
    private static (string ProviderId, string ModelId) SplitModelId(string model)
    {
        var separator = model.IndexOf('/');
        if (separator <= 0 || separator == model.Length - 1)
        {
            throw new InvalidOperationException(
                $"OpenCode model '{model}' is not in the provider/model format.");
        }
        return (model[..separator], model[(separator + 1)..]);
    }

    private static string[] InstructionFiles(string? systemPromptPath, IReadOnlyList<string> skillHints)
    {
        var files = new List<string>();
        if (systemPromptPath is not null)
        {
            files.Add(Path.GetFileName(systemPromptPath));
        }
        foreach (var hint in skillHints)
        {
            files.Add(hint);
        }
        return files.ToArray();
    }

    /// <summary>
    /// Writes the workspace <c>opencode.json</c>, merging Throne's instruction references into a
    /// file the cloned repo may itself own: repo-owned keys (agents, tools, permissions, model
    /// overrides…) are preserved verbatim, the <c>instructions</c> array is rebuilt from the
    /// repo's own entries plus the fresh per-spawn Throne files. An unparsable or non-object
    /// pre-existing file is treated as absent.
    /// </summary>
    private static async Task WriteConfigAsync(
        string workspacePath, IReadOnlyList<string> instructionFiles, CancellationToken ct)
    {
        var root = await ReadConfigAsync(workspacePath, ct) ?? NewConfig();
        var instructions = new JsonArray();
        if (root["instructions"] is JsonArray existing)
        {
            foreach (var entry in existing)
            {
                if (entry is JsonValue value
                    && value.TryGetValue<string>(out var file)
                    && !file.StartsWith(ThroneFilePrefix, StringComparison.Ordinal))
                {
                    instructions.Add(file);
                }
            }
        }

        foreach (var file in instructionFiles)
        {
            instructions.Add(file);
        }

        if (instructions.Count == 0)
        {
            root.Remove("instructions");
        }
        else
        {
            root["instructions"] = instructions;
        }

        await WriteConfigAsync(workspacePath, root, ct);
    }

    private static async Task WriteDefaultModelAsync(
        string workspacePath, string model, CancellationToken ct)
    {
        var root = await ReadConfigAsync(workspacePath, ct) ?? NewConfig();
        root["model"] = model;
        await WriteConfigAsync(workspacePath, root, ct);
    }

    private static JsonObject NewConfig() => new() { ["$schema"] = SchemaUrl };

    private static async Task<JsonObject?> ReadConfigAsync(string workspacePath, CancellationToken ct)
    {
        var configPath = Path.Combine(workspacePath, ConfigFileName);
        if (!File.Exists(configPath))
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(await File.ReadAllTextAsync(configPath, ct)) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteConfigAsync(string workspacePath, JsonObject root, CancellationToken ct)
    {
        var configPath = Path.Combine(workspacePath, ConfigFileName);
        await File.WriteAllTextAsync(configPath, root.ToJsonString(JsonOptions) + "\n", ct);
    }

    private static string NormalizeBaseUrl(string? apiBaseUrl) =>
        string.IsNullOrWhiteSpace(apiBaseUrl)
            ? SessionHookOptions.DefaultApiBaseUrl
            : apiBaseUrl.TrimEnd('/');
}
