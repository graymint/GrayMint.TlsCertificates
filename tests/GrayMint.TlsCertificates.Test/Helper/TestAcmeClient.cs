using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GrayMint.Common.Acme;

namespace GrayMint.TlsCertificates.Test.Helper;

// An authority without the network: an account is a random key, and an order signs its domain on the test's clock,
// unless the test makes the domain's orders fail. The test can look into each order while its challenge is published.
public sealed class TestAcmeClient(TestTimeProvider time) : IAcmeClient
{
    private readonly Lock _lock = new();
    private readonly List<string> _orders = [];
    private readonly Dictionary<string, Func<Exception>> _failures = new(StringComparer.Ordinal);
    private int _accountCount;

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(90);

    // runs in each order's validation, while its challenge is published
    public Func<IAcmeOrder, Task>? OnValidate { get; set; }

    public int AccountCount => Volatile.Read(ref _accountCount);

    public IReadOnlyList<string> Orders {
        get {
            lock (_lock)
                return _orders.ToArray();
        }
    }

    public int OrderCount(string domain)
    {
        lock (_lock)
            return _orders.Count(x => x == domain);
    }

    public void Fail(string domain, Func<Exception> failure)
    {
        lock (_lock)
            _failures[domain] = failure;
    }

    public void Succeed(string domain)
    {
        lock (_lock)
            _failures.Remove(domain);
    }

    public Task<string> CreateAccount(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _accountCount);
        return Task.FromResult($"account-{Guid.CreateVersion7():N}");
    }

    public Task<IAcmeOrder> CreateOrder(string accountKeyPem, string domain, CancellationToken cancellationToken = default)
    {
        Func<Exception>? failure;
        lock (_lock) {
            _orders.Add(domain);
            failure = _failures.GetValueOrDefault(domain);
        }

        return Task.FromResult<IAcmeOrder>(new TestAcmeOrder(this, domain, failure));
    }

    // a self-signed certificate of the domain with its key, valid from now on the test's clock for its lifetime
    private byte[] Sign(string domain)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={domain}", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(domain);
        request.CertificateExtensions.Add(san.Build());
        var notBefore = time.GetUtcNow().AddMinutes(-1);
        using var certificate = request.CreateSelfSigned(notBefore, notBefore + Lifetime);
        return certificate.Export(X509ContentType.Pkcs12);
    }

    private sealed class TestAcmeOrder(TestAcmeClient client, string domain, Func<Exception>? failure) : IAcmeOrder
    {
        public string Domain => domain;
        public string Token { get; } = Guid.CreateVersion7().ToString("N");
        public string KeyAuthorization => $"{Token}.thumbprint";

        public async Task<byte[]?> Validate(CancellationToken cancellationToken = default)
        {
            if (client.OnValidate != null)
                await client.OnValidate(this);

            if (failure != null)
                throw failure();

            return client.Sign(domain);
        }
    }
}
