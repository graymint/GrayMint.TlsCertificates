using Microsoft.Extensions.Options;

namespace GrayMint.TlsCertificates;

/// <summary>
/// The checks of a user's domain before the host serves it: that it points to the host, by a CNAME record that names
/// <see cref="TargetCName"/>, and that it is the user's, by a TXT record only the domain's owner can publish
/// (<see cref="TlsDomainCheckOptions.OwnershipFormat"/>). The authority can tell neither before an order: it reads a
/// challenge minutes later, and finds a domain valid that reaches the host, whoever claims it. Registered by
/// <c>AddGrayMintTlsDomainChecks</c>; the host runs the checks when a user saves a domain, and words their answers.
/// </summary>
public sealed class TlsDomainChecker
{
    private readonly ITlsDomainDns _dns;
    private readonly TlsDomainOwnershipFormat _ownershipFormat;

    internal TlsDomainChecker(ITlsDomainDns dns, IOptions<TlsDomainCheckOptions> options)
    {
        _dns = dns;
        _ownershipFormat = options.Value.OwnershipFormat
                           ?? throw new ArgumentException(
                               $"{nameof(TlsDomainCheckOptions.OwnershipFormat)} is required.", nameof(options));
        TargetCName = TlsDomain.NormalizeName(options.Value.TargetCName)
                      ?? throw new ArgumentException(
                          $"{nameof(TlsDomainCheckOptions.TargetCName)} must be a domain name.", nameof(options));
    }

    /// <summary>The name users' domains point to, in its one form, to show them.</summary>
    public string TargetCName { get; }

    /// <summary>Whether the domain's own CNAME record names <see cref="TargetCName"/>.</summary>
    /// <param name="domain">The user's domain, in any form <see cref="TlsDomain.NormalizeName"/> takes.</param>
    /// <param name="cancellationToken">The token that cancels the lookups.</param>
    /// <returns>
    /// Where it points: here when any DNS server asked says so, as one can still have a record as it was before the user
    /// changed it. A domain whose CNAME names the target only through another name doesn't point here: the record the
    /// user adds names the target itself.
    /// </returns>
    /// <exception cref="ArgumentException">The domain is not a domain name, or has a port.</exception>
    /// <exception cref="TlsDomainLookupException">No DNS server gave a usable answer.</exception>
    public async Task<TlsDomainTargetResult> CheckTarget(string domain, CancellationToken cancellationToken)
    {
        var name = TlsDomain.NormalizeName(domain)
                   ?? throw new ArgumentException($"Not a domain name: '{domain}'.", nameof(domain));
        var target = await _dns.GetCName(name, TargetCName, cancellationToken);
        if (target == null)
            return new TlsDomainTargetResult { Status = TlsDomainTargetStatus.NoCName };

        return new TlsDomainTargetResult {
            Status = TlsDomain.NormalizeName(target) == TargetCName ? TlsDomainTargetStatus.PointsHere : TlsDomainTargetStatus.OtherTarget,
            Target = target
        };
    }

    /// <summary>The TXT record by which the owner proves the domain is theirs, to show them.</summary>
    /// <param name="domain">The user's domain, in any form <see cref="TlsDomain.NormalizeName"/> takes.</param>
    /// <param name="ownerKey">Who claims it, in the host's terms: a key of the user or account that never changes.</param>
    /// <returns>The record's name and value.</returns>
    /// <exception cref="ArgumentException">
    /// The domain is not a domain name, has a port, or is too long for the record's name; or the owner's key is empty.
    /// </exception>
    public TlsDomainOwnershipRecord GetOwnershipRecord(string domain, string ownerKey)
    {
        return _ownershipFormat.GetRecord(domain, ownerKey);
    }

    /// <summary>Whether DNS has the owner's record for the domain (<see cref="GetOwnershipRecord"/>).</summary>
    /// <param name="domain">The user's domain, in any form <see cref="TlsDomain.NormalizeName"/> takes.</param>
    /// <param name="ownerKey">Who claims it, in the host's terms: a key of the user or account that never changes.</param>
    /// <param name="cancellationToken">The token that cancels the lookups.</param>
    /// <returns>Whether any DNS server asked has the record, of its own name, with the owner's value.</returns>
    /// <exception cref="ArgumentException">
    /// The domain is not a domain name, has a port, or is too long for the record's name; or the owner's key is empty.
    /// </exception>
    /// <exception cref="TlsDomainLookupException">No DNS server gave a usable answer.</exception>
    public async Task<bool> HasOwnershipRecord(string domain, string ownerKey, CancellationToken cancellationToken)
    {
        var record = GetOwnershipRecord(domain, ownerKey);
        return await _dns.HasTxt(record.Name, record.Value, cancellationToken);
    }
}
