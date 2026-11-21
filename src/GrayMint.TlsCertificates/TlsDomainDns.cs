using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrayMint.TlsCertificates;

// The module's DNS for the domain checks: the host's resolver and the other servers, all asked at once, as one of them can
// still cache a record a user has just added or changed as it was. A server that fails or doesn't answer in time is no
// answer; a name that doesn't exist is one.
internal sealed class TlsDomainDns : ITlsDomainDns
{
    private readonly ILogger _logger;
    private readonly LookupClient[] _clients;

    public TlsDomainDns(IOptions<TlsDomainCheckOptions> options, ILogger<TlsDomainDns> logger)
    {
        _logger = logger;
        var timeout = options.Value.LookupTimeout;
        var clients = new List<LookupClient>();
        if (options.Value.UseSystemDnsServers)
            clients.Add(new LookupClient(Configure(new LookupClientOptions(), timeout)));
        clients.AddRange(options.Value.DnsServers
            .Select(server => new LookupClient(Configure(new LookupClientOptions(server), timeout))));
        _clients = [.. clients];
    }

    // no cache: a user who has fixed a record and saves again is asked about it again
    private static LookupClientOptions Configure(LookupClientOptions options, TimeSpan timeout)
    {
        options.UseCache = false;
        options.Timeout = timeout;
        options.Retries = 0;
        options.ThrowDnsErrors = false;
        options.UseTcpFallback = true;
        return options;
    }

    public async Task<string?> GetCName(string name, string expectedTarget, CancellationToken cancellationToken)
    {
        // the expected target settles it at once; another one waits for the other servers, which may have it now
        string? otherTarget = null;
        var target = await Ask(name, QueryType.CNAME, response => {
            foreach (var record in response.Answers.CnameRecords().Where(x => IsOf(x, name))) {
                var recordTarget = record.CanonicalName.Value.TrimEnd('.');
                if (TlsDomain.NormalizeName(recordTarget) == expectedTarget)
                    return recordTarget;
                otherTarget ??= recordTarget;
            }

            return null;
        }, cancellationToken);
        return target ?? otherTarget;
    }

    public async Task<bool> HasTxt(string name, string value, CancellationToken cancellationToken)
    {
        var record = await Ask(name, QueryType.TXT,
            response => response.Answers.TxtRecords().FirstOrDefault(x => IsOf(x, name) && Holds(x, value)),
            cancellationToken);
        return record != null;
    }

    // a long value comes in pieces, which make one value; a record from before may hold it in one piece of several
    private static bool Holds(TxtRecord record, string value)
    {
        return string.Concat(record.Text).Trim() == value || record.Text.Any(x => x.Trim() == value);
    }

    // A record of the name asked: an answer also holds the records of the name its CNAME leads to, which whoever has that
    // name publishes.
    private static bool IsOf(ResourceRecordInfo record, string name)
    {
        return string.Equals(record.DomainName.Value.TrimEnd('.'), name, StringComparison.OrdinalIgnoreCase);
    }

    // What settle finds in the first answer that has it, all servers asked at once; null when the servers that answered
    // have none of it.
    private async Task<T?> Ask<T>(string name, QueryType type, Func<IDnsQueryResponse, T?> settle,
        CancellationToken cancellationToken) where T : class
    {
        using var lookupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var queries = _clients.Select(client => Query(client, name, type, lookupCancellation.Token)).ToList();
        var answered = false;
        try {
            while (queries.Count > 0) {
                var query = await Task.WhenAny(queries);
                queries.Remove(query);
                cancellationToken.ThrowIfCancellationRequested();

                var response = await query;
                if (response == null)
                    continue;

                answered = true;
                var settled = settle(response);
                if (settled != null)
                    return settled;
            }
        }
        finally {
            // the other servers' answers are needless now
            await lookupCancellation.CancelAsync();
        }

        return answered
            ? null
            : throw new TlsDomainLookupException($"No DNS server gave a usable answer for the {type} record of {name}.");
    }

    // The server's answer, or null when it failed, refused or didn't answer in time. Never throws: the queries an answer
    // makes needless are cancelled, and nobody waits for them.
    private async Task<IDnsQueryResponse?> Query(LookupClient client, string name, QueryType type,
        CancellationToken cancellationToken)
    {
        try {
            var response = await client.QueryAsync(name, type, QueryClass.IN, cancellationToken);
            if (!response.HasError || response.Header.ResponseCode == DnsHeaderResponseCode.NotExistentDomain)
                return response;

            _logger.LogDebug("A DNS server answered with an error. Name: {Name}, Type: {Type}, Server: {Server}, Error: {Error}",
                name, type, response.NameServer, response.ErrorMessage);
            return null;
        }
        catch (Exception ex) {
            if (!cancellationToken.IsCancellationRequested)
                _logger.LogDebug(ex, "A DNS server didn't answer. Name: {Name}, Type: {Type}", name, type);
            return null;
        }
    }
}
