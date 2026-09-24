namespace Throne.Application.Terminals;

/// <summary>
/// Live model whitelist for vendors whose <see cref="TerminalVendorDescriptor.ModelSource"/> is
/// not <see cref="TerminalAgentCatalog.ModelSourceStatic"/> (e.g.
/// <see cref="TerminalAgentCatalog.ModelSourceAgent"/>). The static
/// <see cref="TerminalVendorDescriptor.Models"/> for such vendors is empty; the resolver and the
/// catalog projection call this seam to materialise the current list. Implementations live close
/// to their data source (OpenCode → the shared serve's provider surface). Returning an empty
/// list means the source is unavailable — surface it to the launch UI rather than failing the
/// catalog projection.
/// </summary>
public interface IVendorModelCatalog
{
    /// <summary>Vendor token (<see cref="TerminalAgentCatalog"/> constant) this catalog serves.</summary>
    string Vendor { get; }

    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);
}
