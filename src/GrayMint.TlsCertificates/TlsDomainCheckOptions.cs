using System.Net;

namespace GrayMint.TlsCertificates;

/// <summary>What the domain checks expect of a user's domain, and where they read DNS (<see cref="TlsDomainChecker"/>).</summary>
public class TlsDomainCheckOptions
{
    /// <summary>
    /// The name a user's domain points to by its CNAME record: the host's, which leads to the server that serves the
    /// domains. Required, a DNS name with no port.
    /// </summary>
    public string TargetCName { get; set; } = "";

    /// <summary>
    /// The TXT record by which a user proves a domain is theirs. Required: its
    /// <see cref="TlsDomainOwnershipFormat.Purpose"/> is the host's own, so a record left for another host proves nothing
    /// here.
    /// </summary>
    public TlsDomainOwnershipFormat? OwnershipFormat { get; set; }

    /// <summary>Whether the host's own resolver is asked too, beside <see cref="DnsServers"/>.</summary>
    public bool UseSystemDnsServers { get; set; } = true;

    /// <summary>
    /// The DNS servers asked at once, beside the host's resolver: Cloudflare's, Google's and OpenDNS's, as one of them
    /// can still cache a record a user has just added or changed as it was. Empty asks the host's resolver alone.
    /// </summary>
    public IReadOnlyList<IPEndPoint> DnsServers { get; set; } = [
        new(IPAddress.Parse("1.1.1.1"), 53), new(IPAddress.Parse("1.0.0.1"), 53),
        new(IPAddress.Parse("8.8.8.8"), 53), new(IPAddress.Parse("8.8.4.4"), 53),
        new(IPAddress.Parse("208.67.222.222"), 53), new(IPAddress.Parse("208.67.220.220"), 53)
    ];

    /// <summary>How long one DNS server may take to answer.</summary>
    public TimeSpan LookupTimeout { get; set; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        if (TlsDomain.NormalizeName(TargetCName) == null)
            throw new ArgumentException($"{nameof(TargetCName)} must be a domain name with no port: '{TargetCName}'.");
        if (OwnershipFormat == null)
            throw new ArgumentException(
                $"{nameof(OwnershipFormat)} is required: its {nameof(TlsDomainOwnershipFormat.Purpose)} is the host's own.");
        OwnershipFormat.Validate();

        if (DnsServers == null)
            throw new ArgumentException($"{nameof(DnsServers)} is required; an empty list asks the host's resolver alone.");
        if (!UseSystemDnsServers && DnsServers.Count == 0)
            throw new ArgumentException(
                $"{nameof(DnsServers)} is empty and {nameof(UseSystemDnsServers)} is off: no lookup could be answered.");
        foreach (var server in DnsServers)
            if (server == null || server.Port == 0 || server.Address.Equals(IPAddress.Any) || server.Address.Equals(IPAddress.IPv6Any))
                throw new ArgumentException($"{nameof(DnsServers)} has an address no lookup can be sent to: '{server}'.");
        TlsCertificatesOptions.ThrowIfNotPositive(LookupTimeout, nameof(LookupTimeout));
    }
}
