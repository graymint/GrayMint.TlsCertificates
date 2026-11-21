namespace GrayMint.TlsCertificates;

/// <summary>
/// The domains the host serves, which have certificates: the one thing the module asks the host, which owns them and
/// decides who may have one. Registered as a singleton, as every TLS connection asks it.
/// </summary>
public interface ITlsCertificateDomains
{
    /// <summary>
    /// Every domain that should have a certificate, as the host's data has them now. The renewal asks once a run, and a
    /// domain left out keeps its certificate until it expires, unrenewed.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The domains, in any form <see cref="TlsDomain.Normalize"/> takes; one it refuses is left out.</returns>
    Task<IReadOnlyCollection<string>> GetDomains(CancellationToken cancellationToken);

    /// <summary>
    /// Whether the host serves the domain now. Every TLS connection to a name not in the certificate cache, and every
    /// read of a challenge, asks it whatever name the client sends, so it answers from memory: a database read for each
    /// would let any client make one by naming a domain.
    /// </summary>
    /// <param name="domain">The domain, as <see cref="TlsDomain.Normalize"/> gives it.</param>
    /// <param name="cancellationToken">The token that cancels the check.</param>
    /// <returns><see langword="true"/> if the host serves the domain.</returns>
    ValueTask<bool> IsServed(string domain, CancellationToken cancellationToken);
}
