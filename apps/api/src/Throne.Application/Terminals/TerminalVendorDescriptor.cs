namespace Throne.Application.Terminals;

/// <summary>
/// Provider-neutral capability descriptor for one embedded-terminal vendor. Holds the
/// vendor token, display label, the static curated model whitelist (for
/// <see cref="ModelSource"/> = <c>static</c>; empty for dynamic sources), whether the vendor
/// exposes a reasoning-effort axis (with its native default), the provenance of the model
/// list, and the spawn-argv builder.
/// <see cref="TerminalAgentCatalog"/> owns the closed set of descriptors; the resolver and
/// the spawn command read everything vendor-specific from here instead of switching on the
/// vendor token. For dynamic-sourced vendors (<c>local</c>, <c>agent</c>) the live model
/// list is materialised at catalog-projection time through <see cref="IVendorModelCatalog"/>,
/// not from <see cref="Models"/>.
///
/// Invariant: <see cref="SupportsEffort"/> ⇔ <see cref="DefaultEffort"/> is non-null and
/// <see cref="Efforts"/> is non-empty. A vendor without an effort axis carries a null
/// default, an empty effort list, and its <see cref="BuildBaseArgs"/> must not emit any
/// effort flag (the resolved <see cref="TerminalLaunchOptions.Effort"/> is null for it).
///
/// Per-vendor execution toggles live here as flags (<see cref="EnableMouse"/>,
/// <see cref="SupportsNativeHotAttach"/>) so spawn / hot-attach read a capability off the
/// descriptor instead of branching on the vendor token (ADR-0045).
/// </summary>
public sealed record TerminalVendorDescriptor(
    string Vendor,
    string Label,
    IReadOnlyList<string> Models,
    bool SupportsEffort,
    IReadOnlyList<string> Efforts,
    string? DefaultEffort,
    string ModelSource,
    Func<TerminalLaunchOptions, IReadOnlyList<string>> BuildBaseArgs,
    bool InDevelopment = false,
    bool EnableMouse = false,
    bool SupportsNativeHotAttach = false)
{
    /// <summary>
    /// Static native default model = first entry of <see cref="Models"/>. Null when the
    /// list is empty (only possible for dynamic model sources; that branch falls back to
    /// whatever the live catalog reports first).
    /// </summary>
    public string? DefaultModel => Models.Count == 0 ? null : Models[0];

    /// <summary>Whether <paramref name="model"/> is in this vendor's static whitelist.</summary>
    public bool HasModel(string model) =>
        !string.IsNullOrEmpty(model) && Models.Contains(model, StringComparer.Ordinal);
}
