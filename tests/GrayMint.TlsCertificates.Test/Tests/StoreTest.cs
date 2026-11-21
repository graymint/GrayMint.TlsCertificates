using GrayMint.TlsCertificates.Persistence.Models;
using GrayMint.TlsCertificates.Test.Helper;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.TlsCertificates.Test.Tests;

[TestClass]
public class StoreTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod]
    [EveryDatabase]
    public async Task Domain_the_host_does_not_serve_reads_nothing_from_the_database(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var domain = host.Domains.Add();
        await host.Renew(CancellationToken);
        host.Domains.Remove(domain);
        host.Commands.Reset();

        Assert.IsNull(await host.Store.GetPfx(domain, CancellationToken));
        Assert.IsNull(await host.Store.GetPfx("made-up.example.com", CancellationToken));
        Assert.IsNull(await host.Store.GetKeyAuthorization(domain, "token", CancellationToken));
        Assert.IsNull(await host.Store.GetPfx("not a domain", CancellationToken));

        Assert.AreEqual(0, host.Commands.Count);
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Served_domain_with_no_certificate_yet_is_read_once_in_the_miss_time(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken,
            configure: options => options.MissCacheTimeout = TimeSpan.FromSeconds(10));
        var domain = host.Domains.Add();
        host.Commands.Reset();

        Assert.IsNull(await host.Store.GetPfx(domain, CancellationToken));
        Assert.IsNull(await host.Store.GetPfx(domain, CancellationToken));
        Assert.AreEqual(1, host.Commands.Count);

        // a certificate issued meanwhile is served once the miss time is over
        await host.Renew(CancellationToken);
        Assert.IsNull(await host.Store.GetPfx(domain, CancellationToken));
        host.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.IsNotNull(await host.Store.GetPfx(domain, CancellationToken));
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Challenge_a_stopped_order_left_behind_is_answered_no_more(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken,
            configure: options => options.ChallengeTimeout = TimeSpan.FromMinutes(15));
        var domain = host.Domains.Add();
        await using (var dbContext = host.CreateDbContext()) {
            dbContext.Certificates.Add(new CertificateModel {
                Domain = domain,
                ChallengeToken = "left-behind",
                ChallengeKeyAuthorization = "left-behind.thumbprint",
                ChallengeTime = host.Time.GetUtcNow().UtcDateTime,
                CreatedTime = host.Time.GetUtcNow().UtcDateTime
            });
            await dbContext.SaveChangesAsync(CancellationToken);
        }

        Assert.AreEqual("left-behind.thumbprint", await host.Store.GetKeyAuthorization(domain, "left-behind", CancellationToken));

        host.Time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        Assert.IsNull(await host.Store.GetKeyAuthorization(domain, "left-behind", CancellationToken));
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Status_lists_every_domain_with_its_certificate_and_its_last_order(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var issued = host.Domains.Add();
        var failed = host.Domains.Add();
        host.Domains.SetForeign(failed);
        await host.Renew(CancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var list = await scope.ServiceProvider.GetRequiredService<TlsCertificateService>().List(CancellationToken);

        CollectionAssert.AreEquivalent(new[] { issued, failed }, list.Select(x => x.Domain).ToArray());
        var issuedInfo = list.Single(x => x.Domain == issued);
        Assert.IsTrue(issuedInfo.HasCertificate);
        Assert.IsNotNull(issuedInfo.ExpirationTime);
        Assert.AreEqual(DateTimeKind.Utc, issuedInfo.ExpirationTime.Value.Kind);
        var failedInfo = list.Single(x => x.Domain == failed);
        Assert.IsFalse(failedInfo.HasCertificate);
        Assert.AreEqual(TlsCertificateErrorCode.OtherAnswer, failedInfo.ErrorCode);
        Assert.AreEqual(1, failedInfo.ErrorCount);
        Assert.AreEqual(DateTimeKind.Utc, failedInfo.ErrorTime?.Kind);
    }
}
