using System.Collections.Concurrent;
using GrayMint.TlsCertificates.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GrayMint.TlsCertificates;

/// <summary>
/// What a host serving the domains reads: a domain's certificate for its TLS connections, and the challenge of an order
/// under way for the authority's read of port 80. A domain the host doesn't serve is refused by
/// <see cref="ITlsCertificateDomains.IsServed"/>, before any read of the database.
/// </summary>
public sealed class TlsCertificateStore
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ITlsCertificateDomains _domains;
    private readonly TlsCertificatesOptions _options;
    private readonly TimeProvider _timeProvider;

    // the served domains found with no certificate yet, by the time they were read; only a served domain is kept here,
    // so the names a client makes up never fill it
    private readonly ConcurrentDictionary<string, DateTime> _missTimes = new(StringComparer.Ordinal);

    internal TlsCertificateStore(IServiceScopeFactory serviceScopeFactory, ITlsCertificateDomains domains,
        IOptions<TlsCertificatesOptions> options, TimeProvider timeProvider)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _domains = domains;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>The certificate of a domain the host serves, as Kestrel's certificate cache loads it.</summary>
    /// <param name="domain">The domain, in any form <see cref="TlsDomain.Normalize"/> takes.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>
    /// The certificate as PFX bytes with no password (its key, itself and its intermediates); <see langword="null"/>
    /// for a domain the host doesn't serve, or one with no certificate yet.
    /// </returns>
    public async ValueTask<byte[]?> GetPfx(string domain, CancellationToken cancellationToken)
    {
        var name = TlsDomain.Normalize(domain);
        if (name == null || !await _domains.IsServed(name, cancellationToken))
            return null;

        var now = Now;
        if (_missTimes.TryGetValue(name, out var missTime) && now - missTime < _options.MissCacheTimeout)
            return null;

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var protectedPfx = await scope.ServiceProvider.GetRequiredService<TlsCertificatesDbContext>().Certificates
            .AsNoTracking()
            .Where(x => x.Domain == name && x.Pfx != null)
            .Select(x => x.Pfx)
            .SingleOrDefaultAsync(cancellationToken);
        if (protectedPfx == null) {
            _missTimes[name] = now;
            return null;
        }

        _missTimes.TryRemove(name, out _);
        var protector = scope.ServiceProvider.GetRequiredService<ITlsCertificateProtector>();
        return Convert.FromBase64String(protector.Unprotect(protectedPfx));
    }

    /// <summary>
    /// What <c>http://domain/.well-known/acme-challenge/token</c> answers while the domain's order runs, as the ACME
    /// challenge's middleware asks it.
    /// </summary>
    /// <param name="domain">The request's host, in any form <see cref="TlsDomain.Normalize"/> takes.</param>
    /// <param name="token">The challenge's token, the last segment of the path.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>
    /// The challenge's key authorization; <see langword="null"/> for a domain the host doesn't serve, a token of no order
    /// under way on it, or a challenge older than <see cref="TlsCertificatesOptions.ChallengeTimeout"/>.
    /// </returns>
    public async ValueTask<string?> GetKeyAuthorization(string domain, string token, CancellationToken cancellationToken)
    {
        var name = TlsDomain.Normalize(domain);
        if (name == null || !await _domains.IsServed(name, cancellationToken))
            return null;

        var since = Now - _options.ChallengeTimeout;
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TlsCertificatesDbContext>().Certificates
            .AsNoTracking()
            .Where(x => x.Domain == name && x.ChallengeToken == token && x.ChallengeTime > since)
            .Select(x => x.ChallengeKeyAuthorization)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
