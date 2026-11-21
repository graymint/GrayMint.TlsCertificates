namespace GrayMint.TlsCertificates.Persistence.Models;

// The one row of what the module keeps about its tables: their version, and the lease of the renewal that runs.
internal sealed class MetadataModel
{
    public int MetadataId { get; set; }
    public int SchemaVersion { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpirationTime { get; set; }
}
