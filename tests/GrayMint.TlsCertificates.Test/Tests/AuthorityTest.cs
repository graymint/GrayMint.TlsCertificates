using System.Security.Cryptography.X509Certificates;
using GrayMint.TlsCertificates.Test.Helper;

namespace GrayMint.TlsCertificates.Test.Tests;

// The renewal against a real ACME authority: Pebble, Let's Encrypt's own test server, which compose.yaml starts. It finds
// every challenge valid without reading it, so the renewal's own read of the challenge is what reads it here.
[TestClass]
public class AuthorityTest(TestContext testContext)
{
    private static readonly Uri PebbleDirectoryUri = new("https://127.0.0.1:14041/dir");

    private CancellationToken CancellationToken => testContext.CancellationToken;

    // Pebble's directory is under a certificate of its own, made at its start, that nothing trusts
    private static HttpClient CreatePebbleClient()
    {
        return new HttpClient(new HttpClientHandler {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        });
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Authority_signs_the_domain_with_its_intermediate_on_one_account(TestDatabaseProvider provider)
    {
        using var pebbleClient = CreatePebbleClient();
        await using var host = await TestTlsHost.Create(provider, CancellationToken,
            configureRenewal: options => {
                options.DirectoryUri = PebbleDirectoryUri;
                options.AuthorityHttpClient = pebbleClient;
            }, realAuthority: true);
        var domain = host.Domains.Add();
        var otherDomain = host.Domains.Add();

        var result = await host.Renew(CancellationToken);

        CollectionAssert.AreEquivalent(new[] { domain, otherDomain }, result.Issued.ToArray());
        var pfx = await host.Store.GetPfx(domain, CancellationToken);
        Assert.IsNotNull(pfx);
        var certificates = X509CertificateLoader.LoadPkcs12Collection(pfx, password: null);
        var certificate = certificates.Single(x => x.HasPrivateKey);
        var names = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single().EnumerateDnsNames();
        CollectionAssert.AreEqual(new[] { domain }, names.ToArray());
        Assert.IsTrue(certificates.Count >= 2, "the authority's intermediate goes with the certificate");

        var info = await host.GetInfo(domain, CancellationToken);
        Assert.AreEqual(PebbleDirectoryUri, info?.IssuerDirectoryUri);
        Assert.AreEqual(certificate.Thumbprint, info?.Thumbprint);
    }
}
