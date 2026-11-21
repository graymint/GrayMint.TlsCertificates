namespace GrayMint.TlsCertificates.Test.Tests;

[TestClass]
public class DomainTest
{
    [TestMethod]
    [DataRow("checkout.example.com", "checkout.example.com")]
    [DataRow("  Checkout.EXAMPLE.com. ", "checkout.example.com")]
    [DataRow("checkout.example.com:443", "checkout.example.com")]
    [DataRow("bücher.example", "xn--bcher-kva.example")]
    [DataRow("xn--bcher-kva.example", "xn--bcher-kva.example")]
    [DataRow("a-b.example.com", "a-b.example.com")]
    public void Domain_takes_its_one_form(string domain, string expected)
    {
        Assert.AreEqual(expected, TlsDomain.Normalize(domain));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("localhost")]
    [DataRow("127.0.0.1")]
    [DataRow("[::1]")]
    [DataRow("::1")]
    [DataRow("*.example.com")]
    [DataRow("under_score.example.com")]
    [DataRow("-dash.example.com")]
    [DataRow("dash-.example.com")]
    [DataRow("double..dot.com")]
    [DataRow("checkout.example.com:notaport")]
    [DataRow("checkout.example.com/path")]
    [DataRow("not a domain")]
    public void What_no_certificate_can_be_for_is_refused(string? domain)
    {
        Assert.IsNull(TlsDomain.Normalize(domain));
    }

    [TestMethod]
    public void Name_or_label_too_long_is_refused()
    {
        Assert.IsNull(TlsDomain.Normalize(new string('a', 64) + ".example.com"));
        Assert.IsNull(TlsDomain.Normalize(string.Join('.', Enumerable.Repeat(new string('a', 60), 5))));
        Assert.IsNotNull(TlsDomain.Normalize(new string('a', 63) + ".example.com"));
    }
}
