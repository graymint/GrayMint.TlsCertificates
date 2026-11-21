namespace GrayMint.TlsCertificates.Persistence.Models;

// A domain's certificate, the challenge of its order under way, and how its last order went. The domain is the key, in
// the one form TlsDomain gives it.
internal sealed class CertificateModel
{
    public required string Domain { get; set; }

    // the directory that issued Pfx: one from another directory is ordered again
    public string? IssuerDirectoryUri { get; set; }

    // the certificate as PFX (its key, itself and its intermediates) in base64, protected
    public string? Pfx { get; set; }
    public string? Thumbprint { get; set; }
    public DateTime? IssueTime { get; set; }
    public DateTime? ExpirationTime { get; set; }

    // what http://domain/.well-known/acme-challenge/ChallengeToken answers while the order runs
    public string? ChallengeToken { get; set; }
    public string? ChallengeKeyAuthorization { get; set; }
    public DateTime? ChallengeTime { get; set; }

    public TlsCertificateErrorCode? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? ErrorTime { get; set; }
    public int ErrorCount { get; set; }

    public DateTime CreatedTime { get; set; }
}
