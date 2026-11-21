namespace GrayMint.TlsCertificates;

/// <summary>Where a domain points (<see cref="TlsDomainChecker.CheckTarget"/>).</summary>
public sealed class TlsDomainTargetResult
{
    /// <summary>Whether it points to the host, or why not, for the host to word for its user.</summary>
    public required TlsDomainTargetStatus Status { get; init; }

    /// <summary>The target its CNAME record names, when it has one, as DNS gives it.</summary>
    public string? Target { get; init; }
}
