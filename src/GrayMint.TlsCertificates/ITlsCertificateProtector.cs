namespace GrayMint.TlsCertificates;

/// <summary>
/// Keeps the private keys secret in the database: the certificates' and the account's. Every host that registers the
/// module reads what the host running the renewal wrote, so they protect alike: with ASP.NET Data Protection, the
/// same key ring, application name and purpose in each. Registered by the host, for a scope or as a singleton.
/// </summary>
public interface ITlsCertificateProtector
{
    /// <summary>The text as it is kept in the database.</summary>
    /// <param name="plaintext">A key, or a certificate with its key, as text.</param>
    /// <returns>The protected text.</returns>
    string Protect(string plaintext);

    /// <summary>The text <see cref="Protect"/> was given.</summary>
    /// <param name="protectedText">The text as the database keeps it.</param>
    /// <returns>The text, unprotected.</returns>
    string Unprotect(string protectedText);
}
