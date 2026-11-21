using GrayMint.Common.Acme;

namespace GrayMint.TlsCertificates;

/// <summary>How the renewal orders the certificates: in the one host that runs it.</summary>
public class TlsCertificateRenewalOptions
{
    /// <summary>
    /// The authority's ACME directory; Let's Encrypt by default. Each certificate keeps the directory that issued it,
    /// and one from another is ordered again at the next run, so a move from a staging directory replaces its
    /// certificates.
    /// </summary>
    public Uri DirectoryUri { get; set; } = AcmeDirectories.LetsEncrypt;

    /// <summary>
    /// The client every request to the authority goes through, such as one through a proxy, or one that trusts a test
    /// authority's own certificate; <see langword="null"/> for one of the ACME client's own.
    /// </summary>
    public HttpClient? AuthorityHttpClient { get; set; }

    /// <summary>How long the renewal waits after a run before the next; it never runs two at once.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How much of a certificate's lifetime is left when it is renewed: a third, as Let's Encrypt advises, which is 30
    /// days of its 90.
    /// </summary>
    public double RenewWhenLifetimeLeft { get; set; } = 1.0 / 3;

    /// <summary>The wait after a domain's first failed order; it doubles with each failure in a row.</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The longest wait between two failed orders of a domain.</summary>
    public TimeSpan MaxRetryInterval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// How long the hosts' <see cref="ITlsCertificateDomains.IsServed"/> may take to know a domain just added, so the
    /// authority's read of its challenge finds it served. A run with a new domain waits this before its first order.
    /// </summary>
    public TimeSpan NewDomainDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Makes the connection for the renewal's own read of a challenge's URL, before the authority is asked to read it,
    /// so a domain that doesn't reach the host fails without a failed validation at the authority, which allows few. The
    /// domain is the host's user's: the handler should connect to public addresses only, checked as it connects, and
    /// follow no redirect. Required unless <see cref="SkipSelfCheck"/>.
    /// </summary>
    public Func<IServiceProvider, HttpMessageHandler>? SelfCheckHandlerFactory { get; set; }

    /// <summary>Orders without the renewal's own read of the challenge first, which only a host on a closed network wants.</summary>
    public bool SkipSelfCheck { get; set; }

    /// <summary>How long the renewal's own read of a challenge may take.</summary>
    public TimeSpan SelfCheckTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the authority may take to decide on a challenge; Let's Encrypt takes seconds.</summary>
    public TimeSpan ValidateTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How often the authority is asked how its check of a challenge stands.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a run holds the lease that keeps a second renewal out, as another host or a second start of this one; a
    /// run renews it before each order, and gives it back at its end. A host that stops in the middle of a run holds it
    /// until it ends.
    /// </summary>
    public TimeSpan LeaseTime { get; set; } = TimeSpan.FromMinutes(10);

    internal void Validate()
    {
        if (!DirectoryUri.IsAbsoluteUri)
            throw new ArgumentException($"{nameof(DirectoryUri)} must be an absolute URL: {DirectoryUri}");
        if (RenewWhenLifetimeLeft is <= 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(RenewWhenLifetimeLeft), RenewWhenLifetimeLeft,
                $"{nameof(RenewWhenLifetimeLeft)} is a share of a lifetime, more than 0 and less than 1.");
        if (SelfCheckHandlerFactory == null && !SkipSelfCheck)
            throw new ArgumentException(
                $"{nameof(SelfCheckHandlerFactory)} is required, as the renewal reads each challenge before the " +
                $"authority does; set {nameof(SkipSelfCheck)} to order without it.");

        TlsCertificatesOptions.ThrowIfNotPositive(Interval, nameof(Interval));
        TlsCertificatesOptions.ThrowIfNotPositive(RetryInterval, nameof(RetryInterval));
        TlsCertificatesOptions.ThrowIfNotPositive(MaxRetryInterval, nameof(MaxRetryInterval));
        TlsCertificatesOptions.ThrowIfNegative(NewDomainDelay, nameof(NewDomainDelay));
        TlsCertificatesOptions.ThrowIfNotPositive(SelfCheckTimeout, nameof(SelfCheckTimeout));
        TlsCertificatesOptions.ThrowIfNotPositive(ValidateTimeout, nameof(ValidateTimeout));
        TlsCertificatesOptions.ThrowIfNotPositive(PollInterval, nameof(PollInterval));
        TlsCertificatesOptions.ThrowIfNotPositive(LeaseTime, nameof(LeaseTime));
        if (LeaseTime <= SelfCheckTimeout + ValidateTimeout)
            throw new ArgumentOutOfRangeException(nameof(LeaseTime), LeaseTime,
                $"{nameof(LeaseTime)} must be longer than an order takes: its self-check and its validation.");
    }
}
