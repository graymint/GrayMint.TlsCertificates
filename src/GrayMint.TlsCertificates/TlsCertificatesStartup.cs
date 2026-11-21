using GrayMint.TlsCertificates.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GrayMint.TlsCertificates;

// So a host stops at its start, not at its first connection: on a database without the tables, with tables of another
// version, or without the host's domains or protector registered.
internal sealed class TlsCertificatesStartup(IServiceScopeFactory serviceScopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        _ = scope.ServiceProvider.GetRequiredService<ITlsCertificateDomains>();
        _ = scope.ServiceProvider.GetRequiredService<ITlsCertificateProtector>();
        await TlsCertificatesSetup.ThrowIfNotReady(
            scope.ServiceProvider.GetRequiredService<TlsCertificatesDbContext>(), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
