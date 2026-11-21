namespace GrayMint.TlsCertificates;

/// <summary>
/// Where the certificates are kept: tables of their own, in a schema of their own, in a database of their own or beside
/// the host's tables in its database.
/// </summary>
public static class TlsCertificatesSchema
{
    /// <summary>The database schema that holds the tables.</summary>
    public const string Name = "gmtls";

    /// <summary>The certificates, one for each domain, with the challenge of an order under way and its last failure.</summary>
    public const string CertificatesTable = "Certificates";

    /// <summary>The accounts at the authorities the certificates are ordered from, one for each authority.</summary>
    public const string AccountsTable = "Accounts";

    /// <summary>
    /// One row of what the module keeps about its own tables: <c>SchemaVersion</c>, the version of their shape, and the
    /// lease of the one renewal that may run at a time.
    /// </summary>
    public const string MetadataTable = "Metadata";

    // The shape the model gives the tables. A change to them raises it, with a step of the setup that takes tables of
    // the version before to it; the start refuses tables of any other version.
    internal const int Version = 1;
}
