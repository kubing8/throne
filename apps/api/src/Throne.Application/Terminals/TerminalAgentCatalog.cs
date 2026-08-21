namespace Throne.Application.Terminals;

/// <summary>
/// Stable wire tokens for the provider-neutral agent axis: vendor names, the closed effort set,
/// model-source provenance and the default vendor. The per-vendor
/// <see cref="TerminalVendorDescriptor"/>s live in <see cref="TerminalVendorDescriptors"/> and are
/// resolved at runtime through the DI-registered <see cref="ITerminalVendorCatalog"/> (ADR-0045) —
/// not from a static list here. The effort set is provider-neutral and closed, so it stays a
/// constant on this token holder rather than a per-vendor extension point.
/// </summary>
public static class TerminalAgentCatalog
{
    public const string VendorClaude = "claude";
    public const string VendorCodex = "codex";
    public const string VendorOpencode = "opencode";

    public const string EffortLow = "low";
    public const string EffortMedium = "medium";
    public const string EffortHigh = "high";
    public const string EffortXhigh = "xhigh";

    /// <summary>Curated model list is hardcoded in the descriptor (no dynamic discovery).</summary>
    public const string ModelSourceStatic = "static";

    /// <summary>
    /// Model list is materialised at projection time from the operator's local OpenAI-compatible
    /// endpoint (<c>Throne:LocalModel:BaseUrl</c>, probed via <c>GET /v1/models</c>). No current
    /// vendor uses this source (the local-model channel stays a settings-only probe); the static
    /// <see cref="TerminalVendorDescriptor.Models"/> list on a <c>local</c> descriptor is empty
    /// by design.
    /// </summary>
    public const string ModelSourceLocal = "local";

    /// <summary>
    /// Model list is discovered live from the agent CLI itself: the models the operator enabled
    /// and authenticated in the agent's own configuration (for <see cref="VendorOpencode"/> —
    /// providers connected in the operator's opencode, flattened to <c>provider/model</c> ids by
    /// the serve's <c>GET /provider</c>). The static
    /// <see cref="TerminalVendorDescriptor.Models"/> list on an <c>agent</c> descriptor is empty
    /// by design; an empty live list means the CLI is missing, unauthenticated, or its serve
    /// cannot start.
    /// </summary>
    public const string ModelSourceAgent = "agent";

    /// <summary>Vendor used when neither the request nor settings pin one.</summary>
    public const string DefaultVendor = VendorClaude;

    /// <summary>Closed effort set, ordered low → xhigh; shared across effort-capable vendors.</summary>
    public static readonly IReadOnlyList<string> SharedEfforts =
        [EffortLow, EffortMedium, EffortHigh, EffortXhigh];

    private static readonly HashSet<string> KnownEfforts =
        new(SharedEfforts, StringComparer.Ordinal);

    public static bool IsKnownEffort(string effort) =>
        !string.IsNullOrEmpty(effort) && KnownEfforts.Contains(effort);
}
