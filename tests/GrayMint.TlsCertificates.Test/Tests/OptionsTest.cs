using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.TlsCertificates.Test.Tests;

// A wrong option stops the registration, not the first order.
[TestClass]
public class OptionsTest
{
    private static ArgumentException RegisterRenewal(Action<TlsCertificateRenewalOptions> configure)
    {
        return Assert.Throws<ArgumentException>(() => new ServiceCollection().AddGrayMintTlsCertificateRenewal(configure));
    }

    private static ArgumentException RegisterDomainChecks(Action<TlsDomainCheckOptions> configure)
    {
        return Assert.Throws<ArgumentException>(() => new ServiceCollection().AddGrayMintTlsDomainChecks(options => {
            options.TargetCName = "target.example.com";
            options.OwnershipFormat = new TlsDomainOwnershipFormat { Purpose = "shop-ownership:" };
            configure(options);
        }));
    }

    [TestMethod]
    public void Renewal_without_a_self_check_is_registered()
    {
        // the authority's read of each challenge is the only one then
        var services = new ServiceCollection().AddGrayMintTlsCertificateRenewal(_ => { });

        Assert.IsTrue(services.Any(x => x.ServiceType == typeof(TlsCertificateRenewer)));
    }

    [TestMethod]
    public void Domain_checks_whose_target_is_no_domain_name_are_refused()
    {
        // a port, which a DNS name never has, is refused rather than dropped
        foreach (var target in new[] { "", "cname", "target.example.com:443", "*.example.com" })
            StringAssert.Contains(RegisterDomainChecks(x => x.TargetCName = target).Message,
                nameof(TlsDomainCheckOptions.TargetCName), target);
    }

    [TestMethod]
    public void Domain_checks_with_no_server_to_ask_are_refused()
    {
        var empty = RegisterDomainChecks(x => {
            x.UseSystemDnsServers = false;
            x.DnsServers = [];
        });
        StringAssert.Contains(empty.Message, nameof(TlsDomainCheckOptions.DnsServers));
        StringAssert.Contains(RegisterDomainChecks(x => x.DnsServers = null!).Message, nameof(TlsDomainCheckOptions.DnsServers));
    }

    [TestMethod]
    public void Domain_checks_with_a_server_no_lookup_can_be_sent_to_are_refused()
    {
        foreach (var server in new[] { new IPEndPoint(IPAddress.Loopback, 0), new IPEndPoint(IPAddress.Any, 53) })
            StringAssert.Contains(RegisterDomainChecks(x => x.DnsServers = [server]).Message,
                nameof(TlsDomainCheckOptions.DnsServers), server.ToString());
    }

    [TestMethod]
    public void Domain_checks_without_a_purpose_of_the_hosts_own_are_refused()
    {
        // a record left for another host, of the same format, would prove an owner of the same key here
        StringAssert.Contains(RegisterDomainChecks(x => x.OwnershipFormat = null).Message,
            nameof(TlsDomainCheckOptions.OwnershipFormat));
        StringAssert.Contains(RegisterDomainChecks(x => x.OwnershipFormat = new TlsDomainOwnershipFormat { Purpose = " " }).Message,
            nameof(TlsDomainOwnershipFormat.Purpose));
    }

    [TestMethod]
    public void An_ownership_record_whose_name_a_host_could_have_is_refused()
    {
        foreach (var label in new[] { "verify", "_two.labels", "_Upper", "_", "_" + new string('a', 63) })
            StringAssert.Contains(
                RegisterDomainChecks(x => x.OwnershipFormat = new TlsDomainOwnershipFormat { Label = label, Purpose = "p:" }).Message,
                nameof(TlsDomainOwnershipFormat.Label), label);
    }

    [TestMethod]
    public void An_ownership_value_whose_prefix_the_comparison_would_lose_is_refused()
    {
        // a value is compared but for the whitespace around it
        foreach (var prefix in new[] { "", " verify=", "verify= ", new string('v', 224) })
            StringAssert.Contains(
                RegisterDomainChecks(x => x.OwnershipFormat = new TlsDomainOwnershipFormat { ValuePrefix = prefix, Purpose = "p:" }).Message,
                nameof(TlsDomainOwnershipFormat.ValuePrefix), $"'{prefix}'");
    }

    [TestMethod]
    public void An_ownership_value_of_no_owner_is_refused()
    {
        // every owner without a key would share one value
        Assert.Throws<ArgumentException>(() => new TlsDomainOwnershipFormat { Purpose = "p:" }.GetValue(" "));
    }

    [TestMethod]
    public void An_ownership_record_too_long_for_a_dns_name_is_refused()
    {
        // a domain of 243 characters, whose record's name would be of 258
        var domain = string.Join('.', Enumerable.Repeat(new string('a', 59), 4)) + ".com";
        Assert.IsNotNull(TlsDomain.NormalizeName(domain));

        Assert.Throws<ArgumentException>(() => new TlsDomainOwnershipFormat { Purpose = "p:" }.GetName(domain));
    }

    [TestMethod]
    public void Renewal_share_of_a_lifetime_outside_zero_and_one_is_refused()
    {
        StringAssert.Contains(RegisterRenewal(x => x.RenewWhenLifetimeLeft = 1).Message, nameof(TlsCertificateRenewalOptions.RenewWhenLifetimeLeft));
        StringAssert.Contains(RegisterRenewal(x => x.RenewWhenLifetimeLeft = 0).Message, nameof(TlsCertificateRenewalOptions.RenewWhenLifetimeLeft));
    }

    [TestMethod]
    public void Renewal_times_that_never_end_or_overlap_are_refused()
    {
        StringAssert.Contains(RegisterRenewal(x => x.Interval = TimeSpan.Zero).Message, nameof(TlsCertificateRenewalOptions.Interval));
        StringAssert.Contains(RegisterRenewal(x => x.RetryInterval = TimeSpan.Zero).Message, nameof(TlsCertificateRenewalOptions.RetryInterval));
        StringAssert.Contains(RegisterRenewal(x => x.LeaseTime = TimeSpan.FromMinutes(2)).Message, nameof(TlsCertificateRenewalOptions.LeaseTime));
    }

    [TestMethod]
    public void Default_domain_no_certificate_can_be_for_is_refused()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            new ServiceCollection().AddGrayMintTlsCertificates(x => x.DefaultDomain = "127.0.0.1"));
        StringAssert.Contains(ex.Message, nameof(TlsCertificatesOptions.DefaultDomain));
    }

    [TestMethod]
    public void Reads_whose_times_never_end_are_refused()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new ServiceCollection().AddGrayMintTlsCertificates(x => x.ChallengeTimeout = TimeSpan.Zero));
        StringAssert.Contains(ex.Message, nameof(TlsCertificatesOptions.ChallengeTimeout));
    }
}
