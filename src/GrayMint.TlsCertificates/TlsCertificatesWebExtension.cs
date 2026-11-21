using GrayMint.Common.Acme;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.TlsCertificates;

/// <summary>Serves the certificates from a host's web server: the challenges on HTTP, and each domain's certificate.</summary>
public static class TlsCertificatesWebExtension
{
    extension(IApplicationBuilder app)
    {
        /// <summary>
        /// Answers the challenges of the orders under way, <c>GET /.well-known/acme-challenge/token</c>, on every
        /// listener: the authority reads port 80, and may follow a redirect to HTTPS. Any other request goes on, so put
        /// it first, before an HTTPS redirect.
        /// </summary>
        /// <returns>The application, to add more.</returns>
        public IApplicationBuilder UseTlsCertificateChallenges()
        {
            var store = app.ApplicationServices.GetRequiredService<TlsCertificateStore>();
            return app.UseAcmeChallenge(store.GetKeyAuthorization);
        }
    }

    extension(ListenOptions listenOptions)
    {
        /// <summary>
        /// HTTPS on the listener, each connection with the certificate of the domain it names, as in
        /// <c>kestrel.ListenAnyIP(443, listen =&gt; listen.UseTlsCertificates())</c>.
        /// </summary>
        /// <returns>The listener's options, to set more.</returns>
        public ListenOptions UseTlsCertificates()
        {
            var selector = listenOptions.ApplicationServices.GetRequiredService<TlsCertificateSelector>();
            return TlsCertificateSelectorExtensions.UseHttps(listenOptions, selector);
        }
    }
}
