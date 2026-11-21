using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrayMint.TlsCertificates;

// Runs the renewal every interval, the next an interval after the last one ended, so two never overlap in a host. A run
// that fails, as on a database that is down for a while, is logged and the next one runs: a stopped renewal would let
// every certificate expire without a word.
internal sealed class TlsCertificateRenewalJob(
    IServiceScopeFactory serviceScopeFactory,
    IOptions<TlsCertificateRenewalOptions> options,
    TimeProvider timeProvider,
    ILogger<TlsCertificateRenewalJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await Task.Delay(options.Value.Interval, timeProvider, stoppingToken);
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<TlsCertificateRenewer>().Run(stoppingToken);
                if (result.Issued.Count + result.Failed.Count + result.Removed.Count > 0)
                    logger.LogInformation(
                        "The certificates' renewal ran. Issued: {Issued}, Failed: {Failed}, Removed: {Removed}",
                        result.Issued.Count, result.Failed.Count, result.Removed.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                return;
            }
            catch (Exception ex) {
                logger.LogError(ex, "The certificates' renewal failed; it runs again after its interval.");
            }
        }
    }
}
