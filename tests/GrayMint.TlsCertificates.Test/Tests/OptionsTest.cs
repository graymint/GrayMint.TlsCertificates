using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.TlsCertificates.Test.Tests;

// A wrong option stops the registration, not the first order.
[TestClass]
public class OptionsTest
{
    private static ArgumentException RegisterRenewal(Action<TlsCertificateRenewalOptions> configure)
    {
        return Assert.Throws<ArgumentException>(() => new ServiceCollection().AddGrayMintTlsCertificateRenewal(options => {
            options.SkipSelfCheck = true;
            configure(options);
        }));
    }

    [TestMethod]
    public void Renewal_without_a_self_check_or_its_skip_is_refused()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            new ServiceCollection().AddGrayMintTlsCertificateRenewal(_ => { }));
        StringAssert.Contains(ex.Message, nameof(TlsCertificateRenewalOptions.SelfCheckHandlerFactory));
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
