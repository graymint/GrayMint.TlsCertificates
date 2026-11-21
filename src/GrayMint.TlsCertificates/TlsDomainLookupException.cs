namespace GrayMint.TlsCertificates;

/// <summary>No DNS server answered a domain check's lookup: the check couldn't tell, and may be run again.</summary>
public class TlsDomainLookupException : Exception
{
    /// <summary>A lookup no DNS server answered.</summary>
    /// <param name="message">Which record of which name.</param>
    public TlsDomainLookupException(string message) : base(message)
    {
    }
}
