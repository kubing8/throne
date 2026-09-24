namespace Throne.Application.Ports;

/// <summary>
/// Persistence port for the single operator-controlled terminal setting: the default
/// agent vendor used as the server-side fallback when a run request omits <c>vendor</c>,
/// plus the last successful launch preference used to pre-fill a new intent.
/// </summary>
public interface ITerminalSettingsStore
{
    /// <summary>
    /// Read the persisted default vendor. Returns the native default
    /// (<see cref="Throne.Application.Terminals.TerminalAgentCatalog.DefaultVendor"/>)
    /// when nothing was ever written, so callers never special-case a missing row.
    /// </summary>
    Task<string> GetDefaultVendorAsync(CancellationToken ct);

    /// <summary>Upsert the default vendor. <paramref name="vendor"/> is pre-validated.</summary>
    Task SetDefaultVendorAsync(string vendor, CancellationToken ct);

    /// <summary>Read the last successful vendor/model selection, if one exists.</summary>
    Task<(string? Vendor, string? Model)> GetLastLaunchAsync(CancellationToken ct);

    /// <summary>Persist the last successful vendor/model selection.</summary>
    Task SetLastLaunchAsync(string vendor, string model, CancellationToken ct);
}
