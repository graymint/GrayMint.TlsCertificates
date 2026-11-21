namespace GrayMint.TlsCertificates;

/// <summary>What a run of the renewal did.</summary>
public sealed class TlsCertificateRenewalResult
{
    /// <summary>
    /// Whether the run had the lease. Without it another renewal was running, as another host or a second start of
    /// this one, and this run did nothing.
    /// </summary>
    public required bool HasLease { get; init; }

    /// <summary>The domains that got a certificate.</summary>
    public required IReadOnlyList<string> Issued { get; init; }

    /// <summary>The domains whose order failed; each certificate's error says why.</summary>
    public required IReadOnlyList<string> Failed { get; init; }

    /// <summary>The domains the host no longer serves whose certificate had expired, which went from the tables.</summary>
    public required IReadOnlyList<string> Removed { get; init; }
}
