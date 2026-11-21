namespace GrayMint.TlsCertificates.Persistence.Models;

// The account at one authority, made at its first order and kept for every later one.
internal sealed class AccountModel
{
    public int AccountId { get; set; }
    public required string DirectoryUri { get; set; }

    // the account's key, as PEM, protected
    public required string AccountKey { get; set; }
    public DateTime CreatedTime { get; set; }
}
