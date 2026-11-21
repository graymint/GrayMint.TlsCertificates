namespace GrayMint.TlsCertificates;

/// <summary>A domain the module keeps a certificate for: the certificate it has, and how its last order went.</summary>
public sealed class TlsCertificateInfo
{
    /// <summary>The domain, as <see cref="TlsDomain.Normalize"/> gives it.</summary>
    public required string Domain { get; init; }

    /// <summary>Whether the domain has a certificate; one it still serves may have expired.</summary>
    public required bool HasCertificate { get; init; }

    /// <summary>The ACME directory that issued the certificate.</summary>
    public required Uri? IssuerDirectoryUri { get; init; }

    /// <summary>The certificate's SHA-1 thumbprint, as <c>X509Certificate2.Thumbprint</c> gives it.</summary>
    public required string? Thumbprint { get; init; }

    /// <summary>When the certificate became valid, in UTC.</summary>
    public required DateTime? IssueTime { get; init; }

    /// <summary>When the certificate expires, in UTC.</summary>
    public required DateTime? ExpirationTime { get; init; }

    /// <summary>Why the last order failed, or <see langword="null"/> if it didn't.</summary>
    public required TlsCertificateErrorCode? ErrorCode { get; init; }

    /// <summary>
    /// The last failure in words: the authority's or the renewal's, with nothing of the host's own; the host's log has
    /// the cause of a <see cref="TlsCertificateErrorCode.Failed"/>.
    /// </summary>
    public required string? ErrorMessage { get; init; }

    /// <summary>When the last order failed, in UTC.</summary>
    public required DateTime? ErrorTime { get; init; }

    /// <summary>The orders that failed in a row; each waits twice as long as the one before.</summary>
    public required int ErrorCount { get; init; }
}
