# ADR-0054: OpenCode Vendor on Operator-Managed Providers

## Status

Accepted (amends [ADR-0042](0042-opencode-shared-serve-and-attach-front.md) for the
model/provider wiring; the shared-serve + attach architecture itself is unchanged)

Date: 2026-08-19

Related: [ADR-0026](0026-embedded-terminal-capabilities-and-run-preflight.md),
[ADR-0042](0042-opencode-shared-serve-and-attach-front.md),
[ADR-0045](0045-throne-extension-pattern.md)

## Context

The OpenCode vendor shipped behind `InDevelopment=true`: Throne materialised a synthetic
`throne-local` provider in the workspace `opencode.json` (`@ai-sdk/openai-compatible` pointed at
`Throne:LocalModel:BaseUrl`, models map mirrored from the local `/v1/models` probe). That pinned
OpenCode to a local OpenAI-compatible endpoint — a channel that ended up temporarily unsupported —
so the vendor surfaced as «в разработке»: visible in `/settings`, never launchable, excluded from
the readiness check.

Meanwhile Claude and Codex vendors work entirely off the operator's own CLI auth and curated model
whitelists. Parity for OpenCode means the same shape: the operator authenticates in the CLI
(`opencode auth login` / the TUI `/connect` — including an OpenCode Go subscription), enables the
models they want in their own opencode settings, and Throne offers exactly that surface. Throne
should not own provider credentials, model maps, or a second source of truth about what is
available.

OpenCode's own surface already fits: the shared `opencode serve` (ADR-0042) exposes
`GET /provider` returning `{ all: Provider[], default, connected: string[] }` — `connected` lists
the providers the operator authenticated, each with the models their config enables. Model ids are
`provider/model` (e.g. `opencode/gpt-5.1-codex`), and the prompt API pins
`model={providerID, modelID}` — which the TUI client already sends.

## Decision

- **Model source `agent`.** New `TerminalModelSource` wire value `agent` (joining `static` and the
  now-unused `local`): the vendor's model list is discovered live from the agent CLI itself.
  `OpencodeModelCatalog` (`IVendorModelCatalog`) asks the shared serve via `GET /provider`,
  filters to `connected` providers, and flattens their model maps to `provider/model` ids — the
  same list the opencode model picker offers. Any failure (CLI missing, serve won't start, HTTP
  error) surfaces as an empty list: the launch UI disables the model picker instead of the catalog
  endpoint failing. The `local` channel survives as the settings-only local-model probe
  (`GET /settings/local-model/models`); no vendor consumes it.
- **No synthetic provider.** The session-hook adapter writes no `provider` section into the
  workspace `opencode.json` anymore — providers, credentials and enabled models are the operator's
  own opencode state. The adapter only merges `instructions` (system-context + skill files) and a
  top-level `model` default into the config, preserving repo-owned keys when the cloned repository
  ships its own `opencode.json`.
- **Launch axis.** The opencode model on the wire is a `provider/model` id; the adapter splits it
  for the `prompt_async` model pin (execution stays in the shared serve, ADR-0042) and writes it as
  the workspace config's `model` default so a bare front and later operator prompts resolve to the
  same choice. No effort axis (unchanged).
- **Vendor selectable.** `InDevelopment` is dropped: opencode is `selectable`, appears in the
  launch dropdowns and the default-vendor selector, and gets a login probe
  (`opencode auth list`) feeding `login_status` and the readiness check like Claude/Codex.
- **Dream source.** `dream_sources` gains `opencode` (`~/.local/share/opencode`) so the dream skill
  can mine opencode dialogues alongside claude/codex.

## Consequences

### Positive

- Vendor parity: install/login/readiness remedies, launch dropdown, settings card — OpenCode now
  behaves like Claude/Codex, with the model list reflecting the operator's subscription and
  enabled models instead of a Throne-managed endpoint.
- Throne stops duplicating provider/model truth (no `throne-local`, no workspace model map); the
  single source is the operator's opencode.
- `local` model-source plumbing (`LocalModelDiscoveryService`, settings endpoint) stays untouched
  behind its public contract — no API break.

### Negative / Risks

- The vendor catalog now spawns the shared serve on demand when it was down — the first
  `/terminal/vendors` call after boot may take seconds (serve cold start) or come back with an
  empty opencode model list when the serve cannot start. The catalog itself stays healthy; the
  model picker alone degrades.
- Model ids are `provider/model` strings owned by the operator's opencode; persisted per-intent
  launch axes (ADR-0041) can go stale when the operator disables a provider/model — resurfacing as
  `terminal.args_invalid` on the next spawn of that intent, same as a removed curated model for
  Claude/Codex.
- Merging into a repo-owned `opencode.json` is best-effort: an unparsable or non-object file is
  overwritten (treated as absent) rather than failing the spawn.
