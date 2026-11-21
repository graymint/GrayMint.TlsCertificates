using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using GrayMint.TlsCertificates.Test.Helper;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.TlsCertificates.Test.Tests;

// The checks of a user's domain against real DNS servers: the zones of tests/dns, which compose.yaml's CoreDNS serves on
// port 15353, and on 15354 as a resolver that still caches some records as they were.
[TestClass]
public class DomainCheckTest(TestContext testContext)
{
    private const string Target = "target.example.test";
    private const string Purpose = "domain-ownership:";

    // alice's value in the default format with this purpose, which the zone holds: a change of the format asks every user
    // for a record again
    private const string AliceValue = "domain-verify=75de7c9244c20069d6cd66d637372cf0";

    private static readonly IPEndPoint TestDnsServer = new(IPAddress.Loopback, 15353);
    private static readonly IPEndPoint StaleDnsServer = new(IPAddress.Loopback, 15354);

    private CancellationToken CancellationToken => testContext.CancellationToken;

    private static TlsDomainChecker CreateChecker(Action<TlsDomainCheckOptions>? configure = null,
        ITlsDomainDns? hostDns = null, bool addLogging = true)
    {
        var services = new ServiceCollection();
        if (addLogging)
            services.AddLogging();
        if (hostDns != null)
            services.AddSingleton(hostDns);
        services.AddGrayMintTlsDomainChecks(options => {
            options.TargetCName = Target;
            options.OwnershipFormat = new TlsDomainOwnershipFormat { Purpose = Purpose };
            options.UseSystemDnsServers = false;
            options.DnsServers = [TestDnsServer];
            configure?.Invoke(options);
        });
        return services.BuildServiceProvider().GetRequiredService<TlsDomainChecker>();
    }

    [TestMethod]
    public async Task A_domain_whose_cname_names_the_target_points_here()
    {
        var result = await CreateChecker().CheckTarget("Shop.Example.Test.", CancellationToken);

        Assert.AreEqual(TlsDomainTargetStatus.PointsHere, result.Status);
        Assert.AreEqual(Target, result.Target);
    }

    [TestMethod]
    public async Task The_target_in_other_letters_is_the_same_target()
    {
        var checker = CreateChecker(options => options.TargetCName = "Target.EXAMPLE.test.");

        var result = await checker.CheckTarget("upper.example.test", CancellationToken);

        Assert.AreEqual(TlsDomainTargetStatus.PointsHere, result.Status);
        Assert.AreEqual(Target, checker.TargetCName, "the target to show a user is in its one form");
    }

    [TestMethod]
    public async Task A_domain_whose_cname_names_another_target_says_which()
    {
        var result = await CreateChecker().CheckTarget("other.example.test", CancellationToken);

        Assert.AreEqual(TlsDomainTargetStatus.OtherTarget, result.Status);
        Assert.AreEqual("elsewhere.example.test", result.Target);
    }

    [TestMethod]
    public async Task A_domain_that_reaches_the_target_only_through_another_name_does_not_point_here()
    {
        var result = await CreateChecker().CheckTarget("chain.example.test", CancellationToken);

        Assert.AreEqual(TlsDomainTargetStatus.OtherTarget, result.Status);
        Assert.AreEqual("shop.example.test", result.Target);
    }

    [TestMethod]
    public async Task A_domain_without_a_cname_or_that_does_not_exist_has_none()
    {
        var checker = CreateChecker();

        var bare = await checker.CheckTarget("bare.example.test", CancellationToken);
        var missing = await checker.CheckTarget("missing.example.test", CancellationToken);

        Assert.AreEqual(TlsDomainTargetStatus.NoCName, bare.Status);
        Assert.IsNull(bare.Target);
        Assert.AreEqual(TlsDomainTargetStatus.NoCName, missing.Status);
    }

    [TestMethod]
    public async Task A_server_that_still_has_a_record_as_it_was_does_not_outvote_one_that_has_it_now()
    {
        foreach (var servers in new[] { new[] { StaleDnsServer, TestDnsServer }, new[] { TestDnsServer, StaleDnsServer } }) {
            var result = await CreateChecker(options => options.DnsServers = servers)
                .CheckTarget("moved.example.test", CancellationToken);

            Assert.AreEqual(TlsDomainTargetStatus.PointsHere, result.Status, $"servers {string.Join(", ", servers.Select(x => x.Port))}");
        }

        // alone, it says where the domain pointed
        var stale = await CreateChecker(options => options.DnsServers = [StaleDnsServer])
            .CheckTarget("moved.example.test", CancellationToken);
        Assert.AreEqual(TlsDomainTargetStatus.OtherTarget, stale.Status);
        Assert.AreEqual("old.example.test", stale.Target);
    }

    [TestMethod]
    public async Task The_owners_record_proves_the_domain_is_theirs_and_no_one_elses()
    {
        var checker = CreateChecker();

        Assert.IsTrue(await checker.HasOwnershipRecord("shop.example.test", "alice", CancellationToken));
        Assert.IsFalse(await checker.HasOwnershipRecord("shop.example.test", "bob", CancellationToken), "another owner's value");
        Assert.IsFalse(await checker.HasOwnershipRecord("other.example.test", "alice", CancellationToken), "another domain");
    }

    [TestMethod]
    public async Task An_ownership_record_a_cname_leads_to_proves_nothing_for_the_domain()
    {
        // _domain-verify.alias is a CNAME to alice's record of shop, which whoever has that name publishes
        Assert.IsFalse(await CreateChecker().HasOwnershipRecord("alias.example.test", "alice", CancellationToken));
    }

    [TestMethod]
    public async Task A_record_in_pieces_is_one_value()
    {
        Assert.IsTrue(await CreateChecker().HasOwnershipRecord("split.example.test", "carol", CancellationToken));
    }

    [TestMethod]
    public void The_record_is_the_formats_whatever_form_the_domain_comes_in()
    {
        var record = CreateChecker().GetOwnershipRecord(" Shop.EXAMPLE.test. ", "alice");

        Assert.AreEqual("_domain-verify.shop.example.test", record.Name);
        Assert.AreEqual(AliceValue, record.Value);
    }

    [TestMethod]
    public void A_format_of_the_hosts_own_gives_its_own_record()
    {
        var checker = CreateChecker(options => options.OwnershipFormat = new TlsDomainOwnershipFormat {
            Label = "_shop-verify",
            ValuePrefix = "shop-verify=",
            Purpose = "shop-ownership:"
        });

        var record = checker.GetOwnershipRecord("shop.example.test", "alice");

        Assert.AreEqual("_shop-verify.shop.example.test", record.Name);
        Assert.AreEqual("shop-verify=d5a57162046277e7e9b1d5676d5c87b7", record.Value);
    }

    [TestMethod]
    public async Task A_lookup_no_server_answers_says_so_rather_than_that_nothing_is_there()
    {
        var checker = CreateChecker(options => {
            options.DnsServers = [new IPEndPoint(IPAddress.Loopback, GetClosedUdpPort())];
            options.LookupTimeout = TimeSpan.FromSeconds(1);
        });

        await Assert.ThrowsExactlyAsync<TlsDomainLookupException>(() =>
            checker.CheckTarget("shop.example.test", CancellationToken));
        await Assert.ThrowsExactlyAsync<TlsDomainLookupException>(() =>
            checker.HasOwnershipRecord("shop.example.test", "alice", CancellationToken));
    }

    [TestMethod]
    public async Task A_server_that_does_not_answer_does_not_hold_up_one_that_does()
    {
        using var silent = new TestSilentDnsServer();
        var checker = CreateChecker(options => {
            options.DnsServers = [silent.EndPoint, TestDnsServer];
            options.LookupTimeout = TimeSpan.FromSeconds(5);
        });

        var stopwatch = Stopwatch.StartNew();
        var target = await checker.CheckTarget("shop.example.test", CancellationToken);
        var owned = await checker.HasOwnershipRecord("shop.example.test", "alice", CancellationToken);

        Assert.AreEqual(TlsDomainTargetStatus.PointsHere, target.Status);
        Assert.IsTrue(owned);
        Assert.IsLessThan(TimeSpan.FromSeconds(4), stopwatch.Elapsed, "the answer that settles it ends the wait");
    }

    [TestMethod]
    public async Task A_cancelled_check_stops_at_once()
    {
        using var silent = new TestSilentDnsServer();
        var checker = CreateChecker(options => {
            options.DnsServers = [silent.EndPoint];
            options.LookupTimeout = TimeSpan.FromSeconds(10);
        });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() => checker.CheckTarget("shop.example.test", cancellation.Token));

        Assert.IsLessThan(TimeSpan.FromSeconds(5), stopwatch.Elapsed);
    }

    [TestMethod]
    public async Task A_name_that_is_no_domain_is_refused_before_any_lookup()
    {
        var dns = new TestDomainDns { CName = Target };
        var checker = CreateChecker(hostDns: dns);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => checker.CheckTarget("not a domain", CancellationToken));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => checker.CheckTarget("shop.example.com:443", CancellationToken));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            checker.HasOwnershipRecord("*.example.com", "alice", CancellationToken));
        Assert.IsEmpty(dns.Lookups);
    }

    [TestMethod]
    public async Task A_dns_of_the_hosts_own_answers_the_checks()
    {
        var dns = new TestDomainDns { CName = Target };
        var checker = CreateChecker(hostDns: dns);

        var target = await checker.CheckTarget("shop.example.com", CancellationToken);
        var owned = await checker.HasOwnershipRecord("shop.example.com", "alice", CancellationToken);

        Assert.AreEqual(TlsDomainTargetStatus.PointsHere, target.Status);
        Assert.IsTrue(owned);
        CollectionAssert.AreEqual(
            new[] { $"CNAME shop.example.com {Target}", $"TXT _domain-verify.shop.example.com {AliceValue}" },
            dns.Lookups.ToArray());
    }

    [TestMethod]
    public async Task The_checks_need_no_logging_of_the_host()
    {
        var result = await CreateChecker(addLogging: false).CheckTarget("shop.example.test", CancellationToken);

        Assert.AreEqual(TlsDomainTargetStatus.PointsHere, result.Status);
    }

    // a port nothing listens on: one a socket just had
    private static int GetClosedUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return socket.Client.LocalEndPoint is IPEndPoint endPoint
            ? endPoint.Port
            : throw new InvalidOperationException("The socket has no local port.");
    }
}
