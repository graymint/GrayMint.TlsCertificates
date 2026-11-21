using System.Net;

namespace GrayMint.TlsCertificates.Test.Helper;

// The renewal's own read of a challenge, answered as the host's web server answers it, by the store; a domain the test
// points elsewhere answers as another server would.
public sealed class TestChallengeHandler(TlsCertificateStore store, TestDomains domains) : HttpMessageHandler
{
    private const string PathPrefix = "/.well-known/acme-challenge/";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("The read has no address.");
        if (domains.IsForeign(uri.Host))
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("another server") };

        var answer = uri.AbsolutePath.StartsWith(PathPrefix, StringComparison.Ordinal)
            ? await store.GetKeyAuthorization(uri.Host, uri.AbsolutePath[PathPrefix.Length..], cancellationToken)
            : null;
        return answer == null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer) };
    }
}
