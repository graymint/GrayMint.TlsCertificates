using GrayMint.TlsCertificates.Persistence;
using GrayMint.TlsCertificates.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace GrayMint.TlsCertificates;

/// <summary>How the domains' certificates stand, for a host to show its users or watch, and a domain's next order.</summary>
public sealed class TlsCertificateService
{
    private readonly TlsCertificatesDbContext _dbContext;

    internal TlsCertificateService(TlsCertificatesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Every domain the module keeps a certificate for, by name.</summary>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The domains, each with its certificate and its last order.</returns>
    public async Task<IReadOnlyList<TlsCertificateInfo>> List(CancellationToken cancellationToken)
    {
        var certificates = await _dbContext.Certificates
            .AsNoTracking()
            .OrderBy(x => x.Domain)
            .ToArrayAsync(cancellationToken);
        return certificates.Select(ToInfo).ToArray();
    }

    /// <summary>A domain's certificate and its last order.</summary>
    /// <param name="domain">The domain, in any form <see cref="TlsDomain.Normalize"/> takes.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The domain's certificate; <see langword="null"/> for one the renewal hasn't seen yet.</returns>
    public async Task<TlsCertificateInfo?> Get(string domain, CancellationToken cancellationToken)
    {
        var name = TlsDomain.Normalize(domain);
        if (name == null)
            return null;

        var certificate = await _dbContext.Certificates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Domain == name, cancellationToken);
        return certificate == null ? null : ToInfo(certificate);
    }

    /// <summary>
    /// Lets the domain's next order go without the wait its failures put on it, as a host does when its user has fixed
    /// the domain: the renewal's next run orders it, if it is due.
    /// </summary>
    /// <param name="domain">The domain, in any form <see cref="TlsDomain.Normalize"/> takes.</param>
    /// <param name="cancellationToken">The token that cancels the write.</param>
    /// <returns><see langword="true"/> if the domain had a wait to take away.</returns>
    public async Task<bool> RetryNow(string domain, CancellationToken cancellationToken)
    {
        var name = TlsDomain.Normalize(domain);
        if (name == null)
            return false;

        var updated = await _dbContext.Certificates
            .Where(x => x.Domain == name && x.ErrorCount > 0)
            .ExecuteUpdateAsync(x => x
                .SetProperty(y => y.ErrorCount, 0)
                .SetProperty(y => y.ErrorTime, (DateTime?)null), cancellationToken);
        return updated == 1;
    }

    private static TlsCertificateInfo ToInfo(CertificateModel certificate)
    {
        return new TlsCertificateInfo {
            Domain = certificate.Domain,
            HasCertificate = certificate.Pfx != null,
            IssuerDirectoryUri = certificate.IssuerDirectoryUri == null ? null : new Uri(certificate.IssuerDirectoryUri),
            Thumbprint = certificate.Thumbprint,
            IssueTime = certificate.IssueTime,
            ExpirationTime = certificate.ExpirationTime,
            ErrorCode = certificate.ErrorCode,
            ErrorMessage = certificate.ErrorMessage,
            ErrorTime = certificate.ErrorTime,
            ErrorCount = certificate.ErrorCount
        };
    }
}
