using System.Net;
using System.Net.Sockets;

namespace GrayMint.TlsCertificates.Test.Helper;

// A DNS server that takes every query and answers none, as one whose answers a firewall drops: a lookup of it waits for its
// whole timeout.
public sealed class TestSilentDnsServer : IDisposable
{
    private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Loopback, 0));

    public IPEndPoint EndPoint => _socket.Client.LocalEndPoint as IPEndPoint
                                 ?? throw new InvalidOperationException("The socket has no local address.");

    public void Dispose()
    {
        _socket.Dispose();
    }
}
