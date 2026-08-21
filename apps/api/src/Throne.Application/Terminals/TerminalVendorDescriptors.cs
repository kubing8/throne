namespace Throne.Application.Terminals;

/// <summary>
/// The concrete <see cref="TerminalVendorDescriptor"/> definitions, one per supported embedded
/// terminal vendor. Each is registered individually into DI (see Application composition root)
/// and surfaced at runtime through <see cref="ITerminalVendorCatalog"/> — the same
/// port → DI fan-out → registry idiom as git providers and capability probes (ADR-0045).
/// Adding a vendor is a new descriptor here plus one DI registration line; no central list or
/// switch is edited.
/// </summary>
public static class TerminalVendorDescriptors
{
    public static readonly TerminalVendorDescriptor Claude = new(
        Vendor: TerminalAgentCatalog.VendorClaude,
        Label: "Claude",
        Models: ["opus", "sonnet", "haiku"],
        SupportsEffort: true,
        Efforts: TerminalAgentCatalog.SharedEfforts,
        DefaultEffort: TerminalAgentCatalog.EffortHigh,
        ModelSource: TerminalAgentCatalog.ModelSourceStatic,
        BuildBaseArgs: static options => ["--model", options.Model, "--effort", options.Effort!],
        SupportsNativeHotAttach: true);

    public static readonly TerminalVendorDescriptor Codex = new(
        Vendor: TerminalAgentCatalog.VendorCodex,
        Label: "Codex",
        Models: ["gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5"],
        SupportsEffort: true,
        Efforts: TerminalAgentCatalog.SharedEfforts,
        DefaultEffort: TerminalAgentCatalog.EffortHigh,
        ModelSource: TerminalAgentCatalog.ModelSourceStatic,
        // codex launches with --dangerously-bypass-approvals-and-sandbox (alias --yolo): the
        // operator presses run and walks away, so mid-task approval prompts on routine work
        // (git fetch / branch from a remote ref, dependency install — all blocked by the
        // default workspace-write sandbox's no-network policy) would strand the session.
        // tmux passes the argv straight to execvp, so the -c value is a raw unquoted token.
        BuildBaseArgs: static options =>
        [
            "-m", options.Model,
            "-c", $"model_reasoning_effort={options.Effort}",
            "--dangerously-bypass-approvals-and-sandbox",
        ],
        SupportsNativeHotAttach: true);

    // OpenCode reads its top-level provider/model setup from the operator's own opencode
    // configuration — Throne materialises neither a provider entry nor a model map in the
    // workspace `opencode.json` (the session-hook adapter only adds `instructions` + the
    // per-launch `model` default there). The spawn argv carries no model/effort flag: the
    // agent loop runs in a shared `opencode serve`, not in this pane, and the model is pinned
    // server-side on the prompt (`prompt_async` model={providerID,modelID}) by the
    // session-hook adapter. The pane only runs `opencode attach <url> --session <id>` — the
    // adapter supplies that whole argv as prepared args, so BuildBaseArgs is empty. No effort
    // axis either: OpenCode does not surface reasoning-effort tiers (SupportsEffort=false).
    public static readonly TerminalVendorDescriptor Opencode = new(
        Vendor: TerminalAgentCatalog.VendorOpencode,
        Label: "OpenCode",
        Models: [],
        SupportsEffort: false,
        Efforts: [],
        DefaultEffort: null,
        // Model list is the operator's own opencode surface: providers they connected in the
        // CLI (`opencode auth login` / /connect), flattened to `provider/model` ids from the
        // shared serve (ADR-0054). Empty until the serve answers — surfaced to the operator
        // instead of guessing a phantom default.
        ModelSource: TerminalAgentCatalog.ModelSourceAgent,
        BuildBaseArgs: static _ => [],
        // OpenCode TUI needs mouse reporting on in the tmux pane for scroll/select to work.
        EnableMouse: true);
}
