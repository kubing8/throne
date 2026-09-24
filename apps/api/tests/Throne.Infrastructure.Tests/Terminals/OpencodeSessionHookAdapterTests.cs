using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Throne.Application.Terminals;
using Throne.Infrastructure.Terminals;

namespace Throne.Infrastructure.Tests.Terminals;

public class OpencodeSessionHookAdapterTests
{
    private static readonly SessionHookOptions HookOptions = new() { ApiBaseUrl = "http://localhost:5008/" };

    [Fact(DisplayName = "Пишет opencode.json без provider-секции и возвращает пустой spawn argv")]
    public async Task Writes_opencode_config_without_provider_and_returns_empty_argv()
    {
        var root = Path.Combine(Path.GetTempPath(), $"throne-opencode-{Guid.NewGuid():N}");
        var sut = NewAdapter();

        var args = await sut.PrepareSpawnArgsAsync(
            "intent-1", root, TerminalRunModes.Work, systemPrompt: null, skillPackages: [], CancellationToken.None);

        args.Should().BeEmpty();
        var configPath = Path.Combine(root, "opencode.json");
        File.Exists(configPath).Should().BeTrue();
        File.Exists(Path.Combine(root, ".opencode", "plugins", "throne.js")).Should().BeTrue();

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        doc.RootElement.GetProperty("$schema").GetString().Should().Be("https://opencode.ai/config.json");
        // Providers/models are the operator's own opencode surface — Throne never writes them.
        doc.RootElement.TryGetProperty("provider", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("instructions", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "Непустой systemPrompt пишется в файл и попадает в instructions по имени")]
    public async Task Writes_system_prompt_and_references_it_by_filename()
    {
        var root = Path.Combine(Path.GetTempPath(), $"throne-opencode-{Guid.NewGuid():N}");
        var sut = NewAdapter();

        await sut.PrepareSpawnArgsAsync(
            "intent-1", root, TerminalRunModes.Work, systemPrompt: "RULES\nblock", skillPackages: [], CancellationToken.None);

        var promptPath = Path.Combine(root, "throne-session.append-system-prompt.txt");
        (await File.ReadAllTextAsync(promptPath)).Should().Be("RULES\nblock");

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "opencode.json")));
        var instructions = doc.RootElement.GetProperty("instructions");
        instructions.GetArrayLength().Should().Be(1);
        instructions[0].GetString().Should().Be("throne-session.append-system-prompt.txt");
    }

    [Fact(DisplayName = "Пустой systemPrompt не пишет файл и не добавляет instructions")]
    public async Task Blank_system_prompt_writes_no_file()
    {
        var root = Path.Combine(Path.GetTempPath(), $"throne-opencode-{Guid.NewGuid():N}");
        var sut = NewAdapter();

        await sut.PrepareSpawnArgsAsync(
            "intent-1", root, TerminalRunModes.Work, systemPrompt: "   ", skillPackages: [], CancellationToken.None);

        File.Exists(Path.Combine(root, "throne-session.append-system-prompt.txt")).Should().BeFalse();
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "opencode.json")));
        doc.RootElement.TryGetProperty("instructions", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "Repo-owned opencode.json: ключи репо сохранены, throne-инструкции смержены, stale заменены")]
    public async Task Merges_instructions_into_repo_owned_config()
    {
        var root = Path.Combine(Path.GetTempPath(), $"throne-opencode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "opencode.json"), """
            {
              "$schema": "https://opencode.ai/config.json",
              "theme": "my-theme",
              "instructions": ["AGENTS.md", "throne-session.append-system-prompt.txt", "throne-session.intent.md"],
              "agent": { "build": { "model": "anthropic/claude-sonnet-4-5" } }
            }
            """);
        var sut = NewAdapter();

        await sut.PrepareSpawnArgsAsync(
            "intent-1", root, TerminalRunModes.Work, systemPrompt: "RULES", skillPackages: [], CancellationToken.None);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "opencode.json")));
        // Repo-owned keys survive verbatim.
        doc.RootElement.GetProperty("theme").GetString().Should().Be("my-theme");
        doc.RootElement.GetProperty("agent").GetProperty("build").GetProperty("model")
            .GetString().Should().Be("anthropic/claude-sonnet-4-5");
        // Instructions = repo's own entries + fresh Throne files; stale throne-session.* replaced.
        doc.RootElement.GetProperty("instructions").EnumerateArray().Select(i => i.GetString())
            .Should().Equal("AGENTS.md", "throne-session.append-system-prompt.txt");
    }

    [Fact(DisplayName = "Невалидный repo opencode.json перезаписывается, а не валит spawn")]
    public async Task Unparsable_repo_config_is_replaced()
    {
        var root = Path.Combine(Path.GetTempPath(), $"throne-opencode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "opencode.json"), "not json [");
        var sut = NewAdapter();

        var act = () => sut.PrepareSpawnArgsAsync(
            "intent-1", root, TerminalRunModes.Work, systemPrompt: null, skillPackages: [], CancellationToken.None);

        await act.Should().NotThrowAsync();
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "opencode.json")));
        doc.RootElement.GetProperty("$schema").GetString().Should().Be("https://opencode.ai/config.json");
    }

    [Fact(DisplayName = "OpenCode readiness объявляется через SessionReady, glyph-scrape отключён")]
    public void Opencode_readiness_uses_hook_event_not_glyph_scrape()
    {
        var sut = NewAdapter();

        sut.ReadinessHookEvent.Should().Be(TerminalHookEvents.SessionReady);
        sut.IsTuiReady("───\n> Tell OpenCode what to do…\n───").Should().BeFalse();
    }

    [Fact(DisplayName = "Непустой prompt: сессия на shared serve с provider/model-пином + model-дефолт в конфиге")]
    public async Task Non_empty_prompt_creates_session_and_builds_attach_args()
    {
        var root = Path.Combine(Path.GetTempPath(), $"throne-opencode-{Guid.NewGuid():N}");
        var client = new RecordingTuiClient();
        var sut = NewAdapter(client);

        var args = await sut.InitializeSessionAsync("intent-1", root, "opencode/gpt-5.1-codex", "TASK", CancellationToken.None);

        args.Should().Equal("attach", "http://127.0.0.1:4096", "--dir", root, "--session", "ses_made");
        client.Calls.Should().Equal(
            new TuiCall(new Uri("http://127.0.0.1:4096/"), root, "opencode", "gpt-5.1-codex", "TASK"));

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "opencode.json")));
        doc.RootElement.GetProperty("model").GetString().Should().Be("opencode/gpt-5.1-codex");
    }

    [Fact(DisplayName = "Пустой prompt: attach без --session, сессия не создаётся, но model-дефолт пишется")]
    public async Task Blank_prompt_attaches_without_session()
    {
        var root = Path.Combine(Path.GetTempPath(), $"throne-opencode-{Guid.NewGuid():N}");
        var client = new RecordingTuiClient();
        var sut = NewAdapter(client);

        var args = await sut.InitializeSessionAsync("intent-1", root, "opencode/gpt-5.1-codex", "   ", CancellationToken.None);

        args.Should().Equal("attach", "http://127.0.0.1:4096", "--dir", root);
        client.Calls.Should().BeEmpty();

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "opencode.json")));
        doc.RootElement.GetProperty("model").GetString().Should().Be("opencode/gpt-5.1-codex");
    }

    [Fact(DisplayName = "Модель без provider/model-формата отвергается с понятной ошибкой")]
    public async Task Model_without_provider_prefix_throws()
    {
        var root = Path.Combine(Path.GetTempPath(), $"throne-opencode-{Guid.NewGuid():N}");
        var sut = NewAdapter();

        var act = () => sut.InitializeSessionAsync("intent-1", root, "gpt-5.1-codex", "TASK", CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*provider/model*");
    }

    [Fact(DisplayName = "Plugin shim маппит OpenCode lifecycle events в существующий hook endpoint")]
    public async Task Plugin_shim_maps_lifecycle_events_to_hook_endpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), $"throne-opencode-{Guid.NewGuid():N}");
        var sut = NewAdapter();

        await sut.PrepareSpawnArgsAsync(
            "intent-1", root, TerminalRunModes.Interview, systemPrompt: null, skillPackages: [], CancellationToken.None);

        var calls = await RunPluginSmokeAsync(root);
        calls.Should().Equal(
            "curl -s -X POST http://localhost:5008/api/v1/intents/intent-1/terminal/hooks/SessionReady?mode=interview",
            "curl -s -X POST http://localhost:5008/api/v1/intents/intent-1/terminal/hooks/Stop?mode=interview",
            "curl -s -X POST http://localhost:5008/api/v1/intents/intent-1/terminal/hooks/UserPromptSubmit?mode=interview",
            "curl -s -X POST http://localhost:5008/api/v1/intents/intent-1/terminal/hooks/Notification?mode=interview",
            "curl -s -X POST http://localhost:5008/api/v1/intents/intent-1/terminal/hooks/PostToolUse?mode=interview",
            "curl -s -X POST http://localhost:5008/api/v1/intents/intent-1/terminal/hooks/PostToolUse?mode=interview");
    }

    [Fact(DisplayName = "OpenCodeBindings фиксируют подтвержденный минимум CLI и не переиспользуют Claude/Codex bindings")]
    public void Opencode_bindings_are_separate_and_version_pinned()
    {
        OpencodePluginShim.MinimumSupportedCliVersion.Should().Be("1.17.7");
        TerminalHookEvents.OpenCodeBindings
            .Select(binding => (binding.OpenCodeHook, binding.ThroneEvent, binding.BindingType))
            .Should().Equal(
                ("session.created", TerminalHookEvents.SessionReady, TerminalHookEvents.OpenCodeBindingEvent),
                ("session.idle", TerminalHookEvents.Stop, TerminalHookEvents.OpenCodeBindingEvent),
                ("tui.prompt.append", TerminalHookEvents.UserPromptSubmit, TerminalHookEvents.OpenCodeBindingEvent),
                ("permission.asked", TerminalHookEvents.Notification, TerminalHookEvents.OpenCodeBindingEvent),
                ("permission.replied", TerminalHookEvents.PostToolUse, TerminalHookEvents.OpenCodeBindingEvent),
                ("tool.execute.after", TerminalHookEvents.PostToolUse, TerminalHookEvents.OpenCodeBindingTypedHook));
    }

    private static OpencodeSessionHookAdapter NewAdapter(IOpencodeTuiClient? client = null) =>
        new(
            HookOptions,
            new SessionSkillMaterializer(),
            new FixedServeGateway(),
            client ?? new RecordingTuiClient());

    private sealed class FixedServeGateway : IOpencodeServeGateway
    {
        public Task<Uri> EnsureRunningAsync(CancellationToken ct) =>
            Task.FromResult(new Uri("http://127.0.0.1:4096/"));
    }

    private sealed record TuiCall(Uri Endpoint, string WorkspacePath, string ProviderId, string ModelId, string Prompt);

    private sealed class RecordingTuiClient : IOpencodeTuiClient
    {
        public List<TuiCall> Calls { get; } = [];

        public Task<string> CreateSessionAndSubmitAsync(
            Uri endpoint,
            string workspacePath,
            string providerId,
            string modelId,
            string prompt,
            CancellationToken ct)
        {
            Calls.Add(new TuiCall(endpoint, workspacePath, providerId, modelId, prompt));
            return Task.FromResult("ses_made");
        }
    }

    private static async Task<IReadOnlyList<string>> RunPluginSmokeAsync(string root)
    {
        var node = FindExecutable("node");
        node.Should().NotBeNull("OpenCode plugin smoke validates the generated ESM shim");

        await File.WriteAllTextAsync(Path.Combine(root, ".opencode", "package.json"), """{"type":"module"}""");
        var script = Path.Combine(root, ".opencode", "plugins", "smoke.mjs");
        await File.WriteAllTextAsync(script, """
            import { ThroneLifecyclePlugin } from "./throne.js";

            const calls = [];
            const $ = async (strings, ...values) => {
              calls.push(strings.reduce((acc, part, index) => acc + part + (values[index] ?? ""), ""));
            };
            const hooks = await ThroneLifecyclePlugin({ $ });
            await hooks.event({ event: { type: "session.created" } });
            await hooks.event({ event: { type: "session.idle" } });
            await hooks.event({ event: { type: "tui.prompt.append" } });
            await hooks.event({ event: { type: "permission.asked" } });
            await hooks.event({ event: { type: "permission.replied" } });
            await hooks["tool.execute.after"]({}, {});
            console.log(JSON.stringify(calls));
            """);

        var start = new ProcessStartInfo(node!, script)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.Combine(root, ".opencode", "plugins"),
        };
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        process.ExitCode.Should().Be(0, error);
        return JsonSerializer.Deserialize<string[]>(output.Trim())!;
    }

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
