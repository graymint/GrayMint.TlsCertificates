namespace GrayMint.TlsCertificates.Test.Helper;

// A host's own DNS for the domain checks, as a host's tests register one: every name's CNAME is CName, every TXT value
// asked is there, and each lookup is kept.
public sealed class TestDomainDns : ITlsDomainDns
{
    private readonly List<string> _lookups = [];

    public string? CName { get; init; }
    public IReadOnlyList<string> Lookups => _lookups;

    public Task<string?> GetCName(string name, string expectedTarget, CancellationToken cancellationToken)
    {
        _lookups.Add($"CNAME {name} {expectedTarget}");
        return Task.FromResult(CName);
    }

    public Task<bool> HasTxt(string name, string value, CancellationToken cancellationToken)
    {
        _lookups.Add($"TXT {name} {value}");
        return Task.FromResult(true);
    }
}
