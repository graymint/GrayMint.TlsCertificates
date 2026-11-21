using System.Security.Cryptography.X509Certificates;
using GrayMint.Common.Acme;
using GrayMint.TlsCertificates.Test.Helper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.TlsCertificates.Test.Tests;

[TestClass]
public class RenewalTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    [TestMethod]
    [EveryDatabase]
    public async Task New_domain_gets_its_certificate_kept_protected(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var domain = host.Domains.Add();

        var result = await host.Renew(CancellationToken);

        CollectionAssert.AreEqual(new[] { domain }, result.Issued.ToArray());
        var info = await host.GetInfo(domain, CancellationToken);
        Assert.IsNotNull(info);
        Assert.IsTrue(info.HasCertificate);
        Assert.AreEqual(TestTlsHost.FakeDirectoryUri, info.IssuerDirectoryUri);
        Assert.AreEqual(0, info.ErrorCount);
        Assert.IsNull(info.ErrorCode);

        var pfx = await host.Store.GetPfx(domain, CancellationToken);
        Assert.IsNotNull(pfx);
        using var certificate = X509CertificateLoader.LoadPkcs12(pfx, password: null);
        Assert.AreEqual($"CN={domain}", certificate.Subject);
        Assert.AreEqual(info.Thumbprint, certificate.Thumbprint);

        // the key and the account's are kept protected, and the challenge goes once the order has ended
        await using var dbContext = host.CreateDbContext();
        var row = await dbContext.Certificates.AsNoTracking().SingleAsync(x => x.Domain == domain, CancellationToken);
        StringAssert.StartsWith(row.Pfx, TestProtector.Prefix);
        Assert.IsNull(row.ChallengeToken);
        Assert.IsNull(row.ChallengeKeyAuthorization);
        var account = await dbContext.Accounts.AsNoTracking().SingleAsync(CancellationToken);
        StringAssert.StartsWith(account.AccountKey, TestProtector.Prefix);
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Challenge_is_answered_while_its_order_runs_for_its_domain_and_token_alone(
        TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var domain = host.Domains.Add();
        var otherDomain = host.Domains.Add();
        var answers = new List<string?>();
        host.AcmeClient.OnValidate = async order => {
            if (order.Domain != domain)
                return;
            answers.Add(await host.Store.GetKeyAuthorization(domain, order.Token, CancellationToken));
            answers.Add(await host.Store.GetKeyAuthorization(domain.ToUpperInvariant() + ".", order.Token, CancellationToken));
            answers.Add(await host.Store.GetKeyAuthorization(otherDomain, order.Token, CancellationToken));
            answers.Add(await host.Store.GetKeyAuthorization(domain, "other-token", CancellationToken));
        };

        await host.Renew(CancellationToken);

        var keyAuthorization = answers[0];
        Assert.IsNotNull(keyAuthorization);
        CollectionAssert.AreEqual(new[] { keyAuthorization, keyAuthorization, null, null }, answers);
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Certificate_is_renewed_when_a_third_of_its_lifetime_is_left(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var domain = host.Domains.Add();
        await host.Renew(CancellationToken);
        var first = await host.GetInfo(domain, CancellationToken);

        // 90 days: renewed at 60, with 30 left
        host.Time.Advance(TimeSpan.FromDays(59));
        Assert.AreEqual(0, (await host.Renew(CancellationToken)).Issued.Count);
        Assert.AreEqual(1, host.AcmeClient.OrderCount(domain));

        host.Time.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        CollectionAssert.AreEqual(new[] { domain }, (await host.Renew(CancellationToken)).Issued.ToArray());
        var renewed = await host.GetInfo(domain, CancellationToken);
        Assert.AreNotEqual(first?.Thumbprint, renewed?.Thumbprint);
        Assert.IsTrue(renewed?.ExpirationTime > first?.ExpirationTime);
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Short_lived_certificate_is_renewed_at_its_own_third_not_every_run(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        host.AcmeClient.Lifetime = TimeSpan.FromDays(6);
        var domain = host.Domains.Add();
        await host.Renew(CancellationToken);

        host.Time.Advance(TimeSpan.FromDays(3));
        await host.Renew(CancellationToken);
        Assert.AreEqual(1, host.AcmeClient.OrderCount(domain), "half its lifetime left: not yet");

        host.Time.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        await host.Renew(CancellationToken);
        Assert.AreEqual(2, host.AcmeClient.OrderCount(domain));
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Certificate_from_another_directory_is_ordered_again(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var domain = host.Domains.Add();
        await host.Renew(CancellationToken);
        await using (var dbContext = host.CreateDbContext())
            await dbContext.Certificates.ExecuteUpdateAsync(
                x => x.SetProperty(y => y.IssuerDirectoryUri, "https://staging.example.com/directory"), CancellationToken);

        CollectionAssert.AreEqual(new[] { domain }, (await host.Renew(CancellationToken)).Issued.ToArray());
        Assert.AreEqual(TestTlsHost.FakeDirectoryUri, (await host.GetInfo(domain, CancellationToken))?.IssuerDirectoryUri);
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Failed_order_waits_twice_as_long_after_each_failure(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var domain = host.Domains.Add();
        host.AcmeClient.Fail(domain, () => new AcmeException("The certificate authority found the challenge invalid. Rate limited"));

        CollectionAssert.AreEqual(new[] { domain }, (await host.Renew(CancellationToken)).Failed.ToArray());
        var info = await host.GetInfo(domain, CancellationToken);
        Assert.AreEqual(TlsCertificateErrorCode.Refused, info?.ErrorCode);
        StringAssert.Contains(info?.ErrorMessage, "Rate limited");
        Assert.AreEqual(1, info?.ErrorCount);
        Assert.IsFalse(info?.HasCertificate);

        // 5 minutes after the first failure, 10 after the second
        await host.Renew(CancellationToken);
        Assert.AreEqual(1, host.AcmeClient.OrderCount(domain));
        host.Time.Advance(TimeSpan.FromMinutes(5));
        await host.Renew(CancellationToken);
        Assert.AreEqual(2, host.AcmeClient.OrderCount(domain));
        host.Time.Advance(TimeSpan.FromMinutes(5));
        await host.Renew(CancellationToken);
        Assert.AreEqual(2, host.AcmeClient.OrderCount(domain));
        host.Time.Advance(TimeSpan.FromMinutes(5));
        await host.Renew(CancellationToken);
        Assert.AreEqual(3, host.AcmeClient.OrderCount(domain));
        Assert.AreEqual(3, (await host.GetInfo(domain, CancellationToken))?.ErrorCount);
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Each_failure_is_kept_with_its_code(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var foreign = host.Domains.Add();
        host.Domains.SetForeign(foreign);
        var undecided = host.Domains.Add();
        host.AcmeClient.Fail(undecided, () => new AcmeTimeoutException("The certificate authority did not check in time."));
        var broken = host.Domains.Add();
        host.AcmeClient.Fail(broken, () => new InvalidOperationException("Login failed for user 'sa', password=secret."));
        var validated = new List<string>();
        host.AcmeClient.OnValidate = order => {
            validated.Add(order.Domain);
            return Task.CompletedTask;
        };

        await host.Renew(CancellationToken);

        var foreignInfo = await host.GetInfo(foreign, CancellationToken);
        Assert.AreEqual(TlsCertificateErrorCode.OtherAnswer, foreignInfo?.ErrorCode);
        CollectionAssert.DoesNotContain(validated, foreign, "its own read failed, so the authority was never asked");
        Assert.AreEqual(TlsCertificateErrorCode.NoDecision, (await host.GetInfo(undecided, CancellationToken))?.ErrorCode);

        // the host's own failure keeps nothing of its words, which may hold a secret
        var brokenInfo = await host.GetInfo(broken, CancellationToken);
        Assert.AreEqual(TlsCertificateErrorCode.Failed, brokenInfo?.ErrorCode);
        Assert.IsFalse(brokenInfo?.ErrorMessage?.Contains("secret"));
        Assert.IsFalse(brokenInfo?.ErrorMessage?.Contains("Login"));
    }

    [TestMethod]
    [EveryDatabase]
    public async Task RetryNow_lets_the_next_order_go_without_the_wait(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var domain = host.Domains.Add();
        host.AcmeClient.Fail(domain, () => new AcmeException("Not yet."));
        await host.Renew(CancellationToken);
        host.AcmeClient.Succeed(domain);

        await using (var scope = host.Services.CreateAsyncScope())
            Assert.IsTrue(await scope.ServiceProvider.GetRequiredService<TlsCertificateService>().RetryNow(domain, CancellationToken));
        CollectionAssert.AreEqual(new[] { domain }, (await host.Renew(CancellationToken)).Issued.ToArray());

        var info = await host.GetInfo(domain, CancellationToken);
        Assert.IsTrue(info?.HasCertificate);
        Assert.AreEqual(0, info?.ErrorCount);
        Assert.IsNull(info?.ErrorMessage);
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Domain_no_longer_served_is_not_renewed_and_goes_once_expired(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var domain = host.Domains.Add();
        await host.Renew(CancellationToken);
        host.Domains.Remove(domain);

        host.Time.Advance(TimeSpan.FromDays(61));
        var result = await host.Renew(CancellationToken);
        Assert.AreEqual(0, result.Issued.Count + result.Removed.Count);
        Assert.IsNotNull(await host.GetInfo(domain, CancellationToken), "kept while it is valid, for a domain served again");

        host.Time.Advance(TimeSpan.FromDays(30));
        CollectionAssert.AreEqual(new[] { domain }, (await host.Renew(CancellationToken)).Removed.ToArray());
        Assert.IsNull(await host.GetInfo(domain, CancellationToken));
        Assert.AreEqual(1, host.AcmeClient.OrderCount(domain));
    }

    [TestMethod]
    [EveryDatabase]
    public async Task New_domain_is_ordered_first_then_the_one_that_expires_soonest(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var older = host.Domains.Add();
        await host.Renew(CancellationToken);
        host.Time.Advance(TimeSpan.FromDays(10));
        var newer = host.Domains.Add();
        await host.Renew(CancellationToken);
        var added = host.Domains.Add();

        // both certificates due: the older expires 10 days before the newer
        host.Time.Advance(TimeSpan.FromDays(61));
        await host.Renew(CancellationToken);

        CollectionAssert.AreEqual(new[] { added, older, newer }, host.AcmeClient.Orders.Skip(2).ToArray());
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Second_run_waits_on_the_lease_of_the_one_running(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var domain = host.Domains.Add();
        var ordering = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        host.AcmeClient.OnValidate = async _ => {
            ordering.TrySetResult();
            await release.Task;
        };

        var firstRun = host.Renew(CancellationToken);
        await ordering.Task.WaitAsync(CancellationToken);

        // the first run holds the lease while it orders: a second does nothing
        var secondRun = await host.Renew(CancellationToken);
        Assert.IsFalse(secondRun.HasLease);
        Assert.AreEqual(1, host.AcmeClient.OrderCount(domain));

        release.SetResult();
        Assert.IsTrue((await firstRun).HasLease);

        // and gives it back at its end
        Assert.IsTrue((await host.Renew(CancellationToken)).HasLease);
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Lease_a_stopped_host_held_is_taken_once_it_expires(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var domain = host.Domains.Add();
        await using (var dbContext = host.CreateDbContext())
            await dbContext.Metadata.ExecuteUpdateAsync(x => x
                .SetProperty(y => y.LeaseOwner, "a-host-that-stopped")
                .SetProperty(y => y.LeaseExpirationTime, host.Time.GetUtcNow().UtcDateTime.AddMinutes(10)), CancellationToken);

        Assert.IsFalse((await host.Renew(CancellationToken)).HasLease);

        host.Time.Advance(TimeSpan.FromMinutes(11));
        var result = await host.Renew(CancellationToken);
        Assert.IsTrue(result.HasLease);
        CollectionAssert.AreEqual(new[] { domain }, result.Issued.ToArray());
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Certificate_that_could_not_be_kept_is_no_failure_of_its_order(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var domain = host.Domains.Add();
        await host.Renew(CancellationToken); // the account, kept while the protector works
        var second = host.Domains.Add();
        host.Protector.FailProtect = true;

        var result = await host.Renew(CancellationToken);

        CollectionAssert.AreEqual(new[] { second }, result.Failed.ToArray());
        var info = await host.GetInfo(second, CancellationToken);
        Assert.IsFalse(info?.HasCertificate);
        Assert.AreEqual(0, info?.ErrorCount, "the authority signed: no wait on the next order");
        Assert.IsNull(info?.ErrorCode);

        host.Protector.FailProtect = false;
        CollectionAssert.AreEqual(new[] { second }, (await host.Renew(CancellationToken)).Issued.ToArray());
        Assert.IsTrue((await host.GetInfo(domain, CancellationToken))?.HasCertificate);
    }

    [TestMethod]
    [EveryDatabase]
    public async Task One_account_orders_every_certificate(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        host.Domains.Add();
        await host.Renew(CancellationToken);
        host.Domains.Add();
        await host.Renew(CancellationToken);

        Assert.AreEqual(1, host.AcmeClient.AccountCount);
        await using var dbContext = host.CreateDbContext();
        Assert.AreEqual(1, await dbContext.Accounts.CountAsync(CancellationToken));
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Domain_in_another_form_is_one_domain(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var domain = host.Domains.Add();
        host.Domains.Add(domain.ToUpperInvariant() + ".");
        host.Domains.Add("not a domain");

        var result = await host.Renew(CancellationToken);

        CollectionAssert.AreEqual(new[] { domain }, result.Issued.ToArray());
        Assert.AreEqual(1, host.AcmeClient.Orders.Count);
    }
}
