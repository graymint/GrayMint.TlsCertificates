using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using GrayMint.TlsCertificates.Test.Helper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.TlsCertificates.Test.Tests;

// A host's web server, on Kestrel: HTTP answers the challenges, and HTTPS serves each domain with its certificate.
[TestClass]
public class WebTest(TestContext testContext)
{
    private CancellationToken CancellationToken => testContext.CancellationToken;

    private static async Task<WebApplication> StartWebServer(TestTlsHost host, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        host.Register(builder.Services);
        builder.WebHost.UseKestrel(kestrel => {
            kestrel.Listen(IPAddress.Loopback, 0);
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseTlsCertificates());
        });
        var app = builder.Build();
        app.UseTlsCertificateChallenges();
        app.Run(context => context.Response.WriteAsync($"the page of {context.Request.Host.Host}"));
        await app.StartAsync(cancellationToken);
        return app;
    }

    private static int Port(WebApplication app, int index)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
                        ?? throw new InvalidOperationException("The server has no addresses.");
        return new Uri(addresses.ElementAt(index)).Port;
    }

    // the subject of the certificate the server sends for the name
    private static async Task<string> Connect(int port, string? serverName, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
        await using var sslStream = new SslStream(client.GetStream(), false, (_, _, _, _) => true);
        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            // an IP address as the target host sends no name, as a client that names none
            TargetHost = serverName ?? IPAddress.Loopback.ToString()
        }, cancellationToken);
        return sslStream.RemoteCertificate?.Subject ?? throw new InvalidOperationException("No certificate came.");
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Each_domain_is_served_with_its_certificate_and_an_unknown_name_with_the_default(
        TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken,
            configure: options => options.DefaultDomain = "default.example.com");
        host.Domains.Add("default.example.com");
        var domain = host.Domains.Add();
        await host.Renew(CancellationToken);
        await using var app = await StartWebServer(host, CancellationToken);
        var httpsPort = Port(app, 1);

        Assert.AreEqual($"CN={domain}", await Connect(httpsPort, domain, CancellationToken));
        Assert.AreEqual("CN=default.example.com", await Connect(httpsPort, "unknown.example.com", CancellationToken));
        Assert.AreEqual("CN=default.example.com", await Connect(httpsPort, null, CancellationToken));
    }

    [TestMethod]
    [EveryDatabase]
    public async Task Challenge_of_an_order_under_way_is_answered_on_http(TestDatabaseProvider provider)
    {
        await using var host = await TestTlsHost.Create(provider, CancellationToken);
        var domain = host.Domains.Add();
        await using var app = await StartWebServer(host, CancellationToken);
        using var httpClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{Port(app, 0)}") };
        var answers = new List<string>();
        host.AcmeClient.OnValidate = async order => {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/.well-known/acme-challenge/{order.Token}");
            request.Headers.Host = domain;
            using var response = await httpClient.SendAsync(request, CancellationToken);
            answers.Add($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(CancellationToken)}");
        };

        await host.Renew(CancellationToken);

        var order = answers.Single();
        StringAssert.StartsWith(order, "200 ");
        StringAssert.EndsWith(order, ".thumbprint");

        // and the app gets the rest
        using var page = new HttpRequestMessage(HttpMethod.Get, "/");
        page.Headers.Host = domain;
        using var pageResponse = await httpClient.SendAsync(page, CancellationToken);
        Assert.AreEqual($"the page of {domain}", await pageResponse.Content.ReadAsStringAsync(CancellationToken));
    }
}
