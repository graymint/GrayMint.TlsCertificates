namespace GrayMint.TlsCertificates;

/// <summary>How a host serves the certificates: the reads every host that registers the module makes.</summary>
public class TlsCertificatesOptions
{
    /// <summary>
    /// The domain whose certificate a TLS connection gets when it names no domain, or one with no certificate;
    /// <see langword="null"/> refuses such a connection. A client that checks the name still refuses it.
    /// </summary>
    public string? DefaultDomain { get; set; }

    /// <summary>How long Kestrel uses a certificate it has read before it reads it again, which takes a renewed one.</summary>
    public TimeSpan CertificateCacheTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a domain no connection names stays in Kestrel's cache.</summary>
    public TimeSpan CertificateIdleTimeout { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long a served domain found with no certificate yet is not read again: connections to it don't each read the
    /// database, and a certificate just issued is served after this at most.
    /// </summary>
    public TimeSpan MissCacheTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a challenge is answered after the renewal published it, longer than any order takes: one a renewal left
    /// behind, when its host stopped in the middle of an order, is answered no more.
    /// </summary>
    public TimeSpan ChallengeTimeout { get; set; } = TimeSpan.FromMinutes(15);

    internal void Validate()
    {
        ThrowIfNotPositive(CertificateCacheTimeout, nameof(CertificateCacheTimeout));
        ThrowIfNotPositive(CertificateIdleTimeout, nameof(CertificateIdleTimeout));
        ThrowIfNegative(MissCacheTimeout, nameof(MissCacheTimeout));
        ThrowIfNotPositive(ChallengeTimeout, nameof(ChallengeTimeout));
        if (DefaultDomain != null && TlsDomain.Normalize(DefaultDomain) == null)
            throw new ArgumentException($"{nameof(DefaultDomain)} is not a domain a certificate can be for: {DefaultDomain}");
    }

    internal static void ThrowIfNotPositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be longer than zero.");
    }

    internal static void ThrowIfNegative(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(name, value, $"{name} can't be negative.");
    }
}
