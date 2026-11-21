using System.Security.Cryptography;
using System.Text;

namespace GrayMint.TlsCertificates;

/// <summary>
/// The TXT record by which a user proves a domain is theirs: named <see cref="Label"/> and the domain, and holding
/// <see cref="ValuePrefix"/> and 32 hex digits of the SHA-256 of <see cref="Purpose"/> and the owner's key. The value is
/// no secret: it ties the record to one owner of one host, and only the domain's owner can publish it. A change to any
/// of the three asks every user for a record again.
/// </summary>
public sealed class TlsDomainOwnershipFormat
{
    private const int MaxNameLength = 253;
    private const int MaxLabelLength = 63;

    // a TXT record's piece holds 255 characters, and the hash takes 32 of them
    private const int MaxValuePrefixLength = 255 - 32;

    /// <summary>The label before the domain in the record's name: one that starts with an underscore, as no host's does.</summary>
    public string Label { get; init; } = "_domain-verify";

    /// <summary>What the value starts with, which tells a user what the record is for.</summary>
    public string ValuePrefix { get; init; } = "domain-verify=";

    /// <summary>
    /// What is hashed before the owner's key: the host's own, as <c>shop-domain-ownership:</c>, so the same key of an
    /// owner of another host, which uses the same format, never has the same value.
    /// </summary>
    public required string Purpose { get; init; }

    /// <summary>The owner's record for the domain, to show them.</summary>
    /// <param name="domain">The user's domain, in any form <see cref="TlsDomain.NormalizeName"/> takes.</param>
    /// <param name="ownerKey">Who claims it, in the host's terms: a key of the user or account that never changes.</param>
    /// <returns>The record's name and value.</returns>
    /// <exception cref="ArgumentException">
    /// The domain is not a domain name, has a port, or is too long for the record's name; or the owner's key is empty.
    /// </exception>
    public TlsDomainOwnershipRecord GetRecord(string domain, string ownerKey)
    {
        return new TlsDomainOwnershipRecord { Name = GetName(domain), Value = GetValue(ownerKey) };
    }

    /// <summary>The name of the domain's ownership record.</summary>
    /// <param name="domain">The user's domain, in any form <see cref="TlsDomain.NormalizeName"/> takes.</param>
    /// <returns>The label, and the domain in its one form.</returns>
    /// <exception cref="ArgumentException">The domain is not a domain name, has a port, or is too long for the record's name.</exception>
    public string GetName(string domain)
    {
        var name = TlsDomain.NormalizeName(domain)
                   ?? throw new ArgumentException($"Not a domain name: '{domain}'.", nameof(domain));
        var recordName = $"{Label}.{name}";
        return recordName.Length <= MaxNameLength
            ? recordName
            : throw new ArgumentException($"The domain is too long for its ownership record's name: '{domain}'.", nameof(domain));
    }

    /// <summary>The value of the owner's record, the same for each of their domains.</summary>
    /// <param name="ownerKey">Who claims the domain, in the host's terms: a key of the user or account that never changes.</param>
    /// <returns>The prefix and the hash's 32 hex digits, in lower case.</returns>
    /// <exception cref="ArgumentException">The owner's key is empty: every owner without one would share a value.</exception>
    public string GetValue(string ownerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Purpose + ownerKey));
        return ValuePrefix + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    internal void Validate()
    {
        if (Label is not { Length: >= 2 and <= MaxLabelLength } || Label[0] != '_' ||
            Label.Any(x => x is not ('_' or '-' or >= 'a' and <= 'z' or >= '0' and <= '9')))
            throw new ArgumentException(
                $"{nameof(Label)} must be one DNS label of up to {MaxLabelLength} characters that starts with '_', " +
                $"of a-z, 0-9, '-' and '_': '{Label}'.");

        // a record's value is compared but for the whitespace around it, which a prefix of its own would lose
        if (string.IsNullOrWhiteSpace(ValuePrefix) || ValuePrefix != ValuePrefix.Trim() || ValuePrefix.Length > MaxValuePrefixLength)
            throw new ArgumentException(
                $"{nameof(ValuePrefix)} is required, of up to {MaxValuePrefixLength} characters with no whitespace around it: '{ValuePrefix}'.");
        if (string.IsNullOrWhiteSpace(Purpose))
            throw new ArgumentException($"{nameof(Purpose)} is required: the host's own, so its values are its own.");
    }
}
