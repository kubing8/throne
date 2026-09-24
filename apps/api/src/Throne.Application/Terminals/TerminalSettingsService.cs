using Throne.Application.Ports;

namespace Throne.Application.Terminals;

/// <summary>
/// Read/write the operator-controlled default terminal vendor. The write path is the only
/// mutator and runs inside a unit of work so the settings singleton upserts atomically.
/// </summary>
public sealed class TerminalSettingsService(
    ITerminalSettingsStore store,
    ITerminalVendorCatalog vendors,
    IUnitOfWork unitOfWork)
{
    public Task<string> GetDefaultVendorAsync(CancellationToken ct) =>
        store.GetDefaultVendorAsync(ct);

    public async Task<string> SetDefaultVendorAsync(string vendor, CancellationToken ct)
    {
        if (!vendors.IsKnownVendor(vendor))
        {
            throw TerminalFailures.VendorInvalid(vendors, vendor);
        }

        await unitOfWork.ExecuteAsync(inner => store.SetDefaultVendorAsync(vendor, inner), ct);
        return vendor;
    }

    public Task<(string? Vendor, string? Model)> GetLastLaunchAsync(CancellationToken ct) =>
        store.GetLastLaunchAsync(ct);

    public async Task SetLastLaunchAsync(string vendor, string model, CancellationToken ct)
    {
        if (!vendors.IsKnownVendor(vendor))
        {
            throw TerminalFailures.VendorInvalid(vendors, vendor);
        }

        await unitOfWork.ExecuteAsync(inner => store.SetLastLaunchAsync(vendor, model, inner), ct);
    }
}
