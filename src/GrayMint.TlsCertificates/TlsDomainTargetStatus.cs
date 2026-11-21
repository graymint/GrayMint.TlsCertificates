namespace GrayMint.TlsCertificates;

/// <summary>Where a domain points, as its CNAME record says (<see cref="TlsDomainChecker.CheckTarget"/>).</summary>
public enum TlsDomainTargetStatus
{
    /// <summary>To <see cref="TlsDomainCheckOptions.TargetCName"/>.</summary>
    PointsHere,

    /// <summary>The domain has no CNAME record, or doesn't exist.</summary>
    NoCName,

    /// <summary>Its CNAME record names another target (<see cref="TlsDomainTargetResult.Target"/>).</summary>
    OtherTarget
}
