namespace GrayMint.TlsCertificates.Test.Helper;

// The host's domains: the ones a test serves, and the ones it points elsewhere, which answer the renewal's own read of a
// challenge as another server would.
public sealed class TestDomains : ITlsCertificateDomains
{
    private readonly Lock _lock = new();
    private readonly HashSet<string> _served = new(StringComparer.Ordinal);
    private readonly HashSet<string> _foreign = new(StringComparer.Ordinal);

    public string Add()
    {
        var domain = $"d{Guid.CreateVersion7():N}.example.com";
        Add(domain);
        return domain;
    }

    public void Add(string domain)
    {
        lock (_lock)
            _served.Add(domain);
    }

    public void Remove(string domain)
    {
        lock (_lock)
            _served.Remove(domain);
    }

    public void SetForeign(string domain)
    {
        lock (_lock)
            _foreign.Add(domain);
    }

    public bool IsForeign(string domain)
    {
        lock (_lock)
            return _foreign.Contains(domain);
    }

    public Task<IReadOnlyCollection<string>> GetDomains(CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyCollection<string>>(_served.ToArray());
    }

    public ValueTask<bool> IsServed(string domain, CancellationToken cancellationToken)
    {
        lock (_lock)
            return ValueTask.FromResult(_served.Contains(domain));
    }
}
