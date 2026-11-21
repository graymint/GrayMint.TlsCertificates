namespace GrayMint.TlsCertificates;

/// <summary>
/// The DNS answers the domain checks read (<see cref="TlsDomainChecker"/>). The module's own asks the host's resolver
/// and the servers of <see cref="TlsDomainCheckOptions.DnsServers"/> at once; a host's tests register their own, as a
/// singleton, to answer for domains no DNS has.
/// </summary>
public interface ITlsDomainDns
{
    /// <summary>
    /// The target of the name's own CNAME record, without its final dot: the expected one when any DNS server asked has
    /// it so, as servers disagree for a while after a record changes; else one a server has.
    /// </summary>
    /// <param name="name">The name, in its one form (<see cref="TlsDomain.NormalizeName"/>).</param>
    /// <param name="expectedTarget">The target the check looks for, in the same form.</param>
    /// <param name="cancellationToken">The token that cancels the lookup.</param>
    /// <returns>
    /// The target, or <see langword="null"/> when the servers that answered have no CNAME record of the name, or no
    /// name.
    /// </returns>
    /// <exception cref="TlsDomainLookupException">No DNS server gave a usable answer.</exception>
    Task<string?> GetCName(string name, string expectedTarget, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a TXT record of the name itself holds the value, as any DNS server asked has it: the record's pieces
    /// joined, or one of them, is the value, but for the whitespace around it. A record of another name, which the
    /// name's CNAME leads to, holds nothing for it.
    /// </summary>
    /// <param name="name">The record's name.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">The token that cancels the lookup.</param>
    /// <returns>Whether a server's answer has a record of the name that holds it.</returns>
    /// <exception cref="TlsDomainLookupException">No DNS server gave a usable answer.</exception>
    Task<bool> HasTxt(string name, string value, CancellationToken cancellationToken);
}
