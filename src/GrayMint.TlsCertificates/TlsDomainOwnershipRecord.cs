namespace GrayMint.TlsCertificates;

/// <summary>The TXT record by which a user proves a domain is theirs (<see cref="TlsDomainOwnershipFormat"/>).</summary>
public sealed class TlsDomainOwnershipRecord
{
    /// <summary>The record's name, as <c>_domain-verify.shop.example.com</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The record's value.</summary>
    public required string Value { get; init; }
}
