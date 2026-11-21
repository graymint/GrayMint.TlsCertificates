using System.Globalization;

namespace GrayMint.TlsCertificates;

/// <summary>
/// The one form a domain takes everywhere in the module, so a certificate ordered, kept and looked up for a name is the
/// same name each time.
/// </summary>
public static class TlsDomain
{
    private const int MaxLength = 253;
    private const int MaxLabelLength = 63;

    /// <summary>
    /// The domain in lower case, without a port or a trailing dot, and in its ASCII form (<c>xn--</c>) if it has
    /// letters of another script; <see langword="null"/> for anything a public certificate can't be for: an IP
    /// address, a wildcard, a name with no dot, a character a host name can't have, or a name or label too long.
    /// </summary>
    /// <param name="domain">A host name as a client or a user gives it, such as <c>Checkout.Example.com.</c> or
    /// <c>checkout.example.com:443</c>.</param>
    public static string? Normalize(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        var host = domain.Trim();

        // a Host header's port; an IPv6 address, which has colons of its own, is no domain
        var colon = host.IndexOf(':');
        if (colon >= 0) {
            if (host.IndexOf(':', colon + 1) >= 0 ||
                !int.TryParse(host.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                return null;
            host = host[..colon];
        }

        if (host.EndsWith('.'))
            host = host[..^1];

        try {
            // UseStd3AsciiRules refuses what a host name can't have, '_' and '*' among them
            host = new IdnMapping { UseStd3AsciiRules = true }.GetAscii(host).ToLowerInvariant();
        }
        catch (ArgumentException) {
            return null;
        }

        if (host.Length > MaxLength || !host.Contains('.') || Uri.CheckHostName(host) != UriHostNameType.Dns)
            return null;

        foreach (var label in host.Split('.'))
            if (label.Length is 0 or > MaxLabelLength || label.StartsWith('-') || label.EndsWith('-') ||
                label.Any(c => c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')))
                return null;

        return host;
    }

    /// <summary>
    /// A DNS name, as a user, a setting or a DNS answer gives it, in the form of <see cref="Normalize"/>: but a port,
    /// which a DNS name never has, is refused rather than dropped, so a domain is checked as the very name it is saved as.
    /// </summary>
    /// <param name="name">A host name, such as <c>Domains.Example.com.</c>.</param>
    /// <returns>The name in its one form, or <see langword="null"/> for a port or anything <see cref="Normalize"/> refuses.</returns>
    public static string? NormalizeName(string? name)
    {
        return name == null || name.Contains(':') ? null : Normalize(name);
    }
}
