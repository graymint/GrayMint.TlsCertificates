using System.Security.Cryptography.X509Certificates;
using GrayMint.Common.Acme;
using GrayMint.TlsCertificates.Persistence;
using GrayMint.TlsCertificates.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrayMint.TlsCertificates;

/// <summary>
/// Orders the certificates the host's domains need: a domain with none gets one, and one whose certificate has
/// <see cref="TlsCertificateRenewalOptions.RenewWhenLifetimeLeft"/> of its lifetime left, or that another directory
/// issued, a new one. A failed order waits <see cref="TlsCertificateRenewalOptions.RetryInterval"/>, twice as long after
/// each failure in a row. The renewal's job runs it every <see cref="TlsCertificateRenewalOptions.Interval"/>; a host
/// may run it itself, too.
/// </summary>
public sealed class TlsCertificateRenewer
{
    // An error's message is the authority's or the issuer's, written for a person; a longer one is cut.
    internal const int MaxErrorMessageLength = 1000;

    private const string FailedMessage = "The order failed for a reason of the host's own; its log has the cause.";

    private readonly TlsCertificatesDbContext _dbContext;
    private readonly ITlsCertificateDomains _domains;
    private readonly ITlsCertificateProtector _protector;
    private readonly AcmeCertificateIssuer _issuer;
    private readonly IServiceProvider _serviceProvider;
    private readonly TlsCertificateRenewalOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TlsCertificateRenewer> _logger;
    private readonly string _directoryUri;

    // a run's own, so a run that never gave the lease back is told apart from the one that holds it
    private readonly string _leaseOwner = Guid.NewGuid().ToString("N");
    private string? _accountKey;

    internal TlsCertificateRenewer(TlsCertificatesDbContext dbContext, ITlsCertificateDomains domains,
        ITlsCertificateProtector protector, AcmeCertificateIssuer issuer, IServiceProvider serviceProvider,
        IOptions<TlsCertificateRenewalOptions> options, TimeProvider timeProvider, ILogger<TlsCertificateRenewer> logger)
    {
        _dbContext = dbContext;
        _domains = domains;
        _protector = protector;
        _issuer = issuer;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _directoryUri = _options.DirectoryUri.ToString();
    }

    private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>
    /// One run: the orders every domain is due, one at a time, the most urgent first. A run that finds another one
    /// running does nothing.
    /// </summary>
    /// <param name="cancellationToken">The token that stops the run between two steps; an order it stops in the middle is
    /// ordered again by the next run.</param>
    /// <returns>What the run did.</returns>
    public async Task<TlsCertificateRenewalResult> Run(CancellationToken cancellationToken)
    {
        if (!await TakeLease(cancellationToken))
            return new TlsCertificateRenewalResult { HasLease = false, Issued = [], Failed = [], Removed = [] };

        try {
            return await RunWithLease(cancellationToken);
        }
        finally {
            // by its owner alone, so a run that lost the lease doesn't give back another's
            await _dbContext.Metadata
                .Where(x => x.MetadataId == TlsCertificatesDbContext.MetadataRowId && x.LeaseOwner == _leaseOwner)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(y => y.LeaseOwner, (string?)null)
                    .SetProperty(y => y.LeaseExpirationTime, (DateTime?)null), CancellationToken.None);
        }
    }

    private async Task<TlsCertificateRenewalResult> RunWithLease(CancellationToken cancellationToken)
    {
        var domains = (await _domains.GetDomains(cancellationToken))
            .Select(TlsDomain.Normalize)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var certificates = await _dbContext.Certificates
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Domain, StringComparer.Ordinal, cancellationToken);
        var now = Now;

        // A domain the host no longer serves is renewed no more, and goes once its certificate has expired: one served
        // again before then has it at once, and orders none the authority has just issued.
        var removed = certificates.Values
            .Where(x => !domains.Contains(x.Domain) && (x.ExpirationTime == null || x.ExpirationTime <= now))
            .Select(x => x.Domain)
            .ToArray();
        if (removed.Length > 0)
            await _dbContext.Certificates.Where(x => removed.Contains(x.Domain)).ExecuteDeleteAsync(cancellationToken);

        var newDomains = domains.Where(x => !certificates.ContainsKey(x)).ToArray();
        if (newDomains.Length > 0) {
            _dbContext.Certificates.AddRange(newDomains.Select(x => new CertificateModel { Domain = x, CreatedTime = now }));
            await _dbContext.SaveChangesAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();

            // the hosts' IsServed may take a moment to know them, and the authority reads their challenges through it
            await Task.Delay(_options.NewDomainDelay, _timeProvider, cancellationToken);
        }

        // a domain with no certificate yet first, then the one that expires soonest
        var due = domains
            .Select(x => certificates.GetValueOrDefault(x) ?? new CertificateModel { Domain = x, CreatedTime = now })
            .Where(x => IsDue(x, now))
            .OrderBy(x => x.ExpirationTime ?? DateTime.MinValue)
            .ThenBy(x => x.Domain, StringComparer.Ordinal)
            .Select(x => x.Domain)
            .ToArray();

        var issued = new List<string>();
        var failed = new List<string>();
        foreach (var domain in due) {
            // another run took the lease, as one whose host thought this one had stopped: it goes on from here
            if (!await TakeLease(cancellationToken))
                break;

            if (await Order(domain, cancellationToken))
                issued.Add(domain);
            else
                failed.Add(domain);
        }

        return new TlsCertificateRenewalResult { HasLease = true, Issued = issued, Failed = failed, Removed = removed };
    }

    // whether the domain's certificate is to be ordered now
    private bool IsDue(CertificateModel certificate, DateTime now)
    {
        if (certificate is { ErrorCount: > 0, ErrorTime: not null } &&
            now < certificate.ErrorTime.Value + RetryInterval(certificate.ErrorCount))
            return false;

        if (certificate.Pfx == null || certificate.IssueTime == null || certificate.ExpirationTime == null ||
            certificate.IssuerDirectoryUri != _directoryUri)
            return true;

        var lifetime = certificate.ExpirationTime.Value - certificate.IssueTime.Value;
        return now >= certificate.ExpirationTime.Value - lifetime * _options.RenewWhenLifetimeLeft;
    }

    internal TimeSpan RetryInterval(int errorCount)
    {
        var interval = _options.RetryInterval * Math.Pow(2, Math.Clamp(errorCount - 1, 0, 30));
        return interval < _options.MaxRetryInterval ? interval : _options.MaxRetryInterval;
    }

    // true if the domain got its certificate
    private async Task<bool> Order(string domain, CancellationToken cancellationToken)
    {
        byte[] pfx;
        try {
            pfx = await _issuer.Issue(new AcmeIssueRequest {
                AccountKeyPem = await GetAccountKey(cancellationToken),
                Domain = domain,
                PublishChallenge = (challenge, ct) => SetChallenge(domain, challenge, ct),
                WithdrawChallenge = (challenge, ct) => WithdrawChallenge(domain, challenge, ct),
                SelfCheckHandlerFactory = CreateSelfCheckHandlerFactory(),
                SelfCheckTimeout = _options.SelfCheckTimeout,
                ValidateTimeout = _options.ValidateTimeout,
                PollInterval = _options.PollInterval
            }, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            _logger.LogWarning(ex, "A certificate could not be ordered. Domain: {Domain}", domain);
            await SetError(domain, ex, cancellationToken);
            return false;
        }

        // The authority has signed, so what fails from here is the host's, not the order's: it is logged, not kept as
        // the domain's error, and the next run orders again, as the authority allows a few certificates a week for a
        // domain.
        try {
            await Keep(domain, pfx, cancellationToken);
            _logger.LogInformation("A certificate has been issued. Domain: {Domain}", domain);
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            _logger.LogError(ex, "A certificate was issued but could not be kept; the next run orders it again. " +
                                 "Domain: {Domain}", domain);
            return false;
        }
    }

    private Func<HttpMessageHandler>? CreateSelfCheckHandlerFactory()
    {
        var factory = _options.SelfCheckHandlerFactory;
        return _options.SkipSelfCheck || factory == null ? null : () => factory(_serviceProvider);
    }

    private async Task Keep(string domain, byte[] pfx, CancellationToken cancellationToken)
    {
        // the certificate with the key, for its dates and thumbprint; the key is read as it is used by no one here
        var certificates = X509CertificateLoader.LoadPkcs12Collection(pfx, password: null, X509KeyStorageFlags.EphemeralKeySet);
        try {
            var certificate = certificates.FirstOrDefault(x => x.HasPrivateKey)
                              ?? throw new InvalidOperationException("The issued PFX has no certificate with a private key.");
            var protectedPfx = _protector.Protect(Convert.ToBase64String(pfx));
            var thumbprint = certificate.Thumbprint;
            var issueTime = certificate.NotBefore.ToUniversalTime();
            var expirationTime = certificate.NotAfter.ToUniversalTime();
            await _dbContext.Certificates
                .Where(x => x.Domain == domain)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(y => y.Pfx, protectedPfx)
                    .SetProperty(y => y.Thumbprint, thumbprint)
                    .SetProperty(y => y.IssueTime, issueTime)
                    .SetProperty(y => y.ExpirationTime, expirationTime)
                    .SetProperty(y => y.IssuerDirectoryUri, _directoryUri)
                    .SetProperty(y => y.ErrorCode, (TlsCertificateErrorCode?)null)
                    .SetProperty(y => y.ErrorMessage, (string?)null)
                    .SetProperty(y => y.ErrorTime, (DateTime?)null)
                    .SetProperty(y => y.ErrorCount, 0), cancellationToken);
        }
        finally {
            foreach (var certificate in certificates)
                certificate.Dispose();
        }
    }

    private async Task SetError(string domain, Exception exception, CancellationToken cancellationToken)
    {
        var (code, message) = exception switch {
            AcmeSelfCheckException { Error: AcmeSelfCheckError.NoAddress } => (TlsCertificateErrorCode.NoAddress, exception.Message),
            AcmeSelfCheckException { Error: AcmeSelfCheckError.NotReached } => (TlsCertificateErrorCode.NotReached, exception.Message),
            AcmeSelfCheckException => (TlsCertificateErrorCode.OtherAnswer, exception.Message),
            AcmeTimeoutException => (TlsCertificateErrorCode.NoDecision, exception.Message),
            AcmeException => (TlsCertificateErrorCode.Refused, exception.Message),
            // the host's own, such as a database's or a protector's, whose words are for its log alone
            _ => (TlsCertificateErrorCode.Failed, FailedMessage)
        };
        if (message.Length > MaxErrorMessageLength)
            message = message[..MaxErrorMessageLength];

        var now = Now;
        await _dbContext.Certificates
            .Where(x => x.Domain == domain)
            .ExecuteUpdateAsync(x => x
                .SetProperty(y => y.ErrorCode, code)
                .SetProperty(y => y.ErrorMessage, message)
                .SetProperty(y => y.ErrorTime, now)
                .SetProperty(y => y.ErrorCount, y => y.ErrorCount + 1), cancellationToken);
    }

    private async Task SetChallenge(string domain, AcmeChallenge challenge, CancellationToken cancellationToken)
    {
        var now = Now;
        await _dbContext.Certificates
            .Where(x => x.Domain == domain)
            .ExecuteUpdateAsync(x => x
                .SetProperty(y => y.ChallengeToken, challenge.Token)
                .SetProperty(y => y.ChallengeKeyAuthorization, challenge.KeyAuthorization)
                .SetProperty(y => y.ChallengeTime, now), cancellationToken);
    }

    // this order's challenge alone, so one taken away late never takes a later order's
    private async Task WithdrawChallenge(string domain, AcmeChallenge challenge, CancellationToken cancellationToken)
    {
        await _dbContext.Certificates
            .Where(x => x.Domain == domain && x.ChallengeToken == challenge.Token)
            .ExecuteUpdateAsync(x => x
                .SetProperty(y => y.ChallengeToken, (string?)null)
                .SetProperty(y => y.ChallengeKeyAuthorization, (string?)null)
                .SetProperty(y => y.ChallengeTime, (DateTime?)null), cancellationToken);
    }

    // the account at the directory, made at its first order
    private async Task<string> GetAccountKey(CancellationToken cancellationToken)
    {
        if (_accountKey != null)
            return _accountKey;

        var account = await _dbContext.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.DirectoryUri == _directoryUri, cancellationToken);
        if (account != null)
            return _accountKey = _protector.Unprotect(account.AccountKey);

        var accountKey = await _issuer.CreateAccount(cancellationToken);
        _dbContext.Accounts.Add(new AccountModel {
            DirectoryUri = _directoryUri,
            AccountKey = _protector.Protect(accountKey),
            CreatedTime = Now
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
        _logger.LogInformation("An ACME account has been made. DirectoryUri: {DirectoryUri}", _directoryUri);
        return _accountKey = accountKey;
    }

    // The lease is the metadata's one row, taken by a write that holds only while it is free, or expired, or this run's
    // own, so of two runs that meet one alone gets it. Each step of a run renews it.
    private async Task<bool> TakeLease(CancellationToken cancellationToken)
    {
        var now = Now;
        var expirationTime = now + _options.LeaseTime;
        var taken = await _dbContext.Metadata
            .Where(x => x.MetadataId == TlsCertificatesDbContext.MetadataRowId &&
                        (x.LeaseOwner == null || x.LeaseOwner == _leaseOwner || x.LeaseExpirationTime < now))
            .ExecuteUpdateAsync(x => x
                .SetProperty(y => y.LeaseOwner, _leaseOwner)
                .SetProperty(y => y.LeaseExpirationTime, expirationTime), cancellationToken);
        return taken == 1;
    }
}
