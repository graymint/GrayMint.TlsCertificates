using System.Text;

namespace GrayMint.TlsCertificates.Test.Helper;

// A protector whose protected text says so, so a test sees that nothing went to the database unprotected; and that can
// fail, as one that has lost its keys does.
public sealed class TestProtector : ITlsCertificateProtector
{
    public const string Prefix = "protected:";

    public bool FailProtect { get; set; }

    public string Protect(string plaintext)
    {
        if (FailProtect)
            throw new InvalidOperationException("The protector's key ring could not be read: password=secret.");

        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
    }

    public string Unprotect(string protectedText)
    {
        if (!protectedText.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("The text was not protected.");

        return Encoding.UTF8.GetString(Convert.FromBase64String(protectedText[Prefix.Length..]));
    }
}
