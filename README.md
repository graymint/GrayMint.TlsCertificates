# GrayMint.TlsCertificates

**TLS certificates for the domains your app serves, your users' own among them**: ordered from Let's Encrypt, or another ACME authority, by a renewal that runs in one host; kept in tables of their own on PostgreSQL or SQL Server; and served by Kestrel in any host, each domain with its own certificate.

[![NuGet](https://img.shields.io/nuget/v/GrayMint.TlsCertificates.svg)](https://www.nuget.org/packages/GrayMint.TlsCertificates)
[![License](https://img.shields.io/github/license/graymint/GrayMint.TlsCertificates.svg)](LICENSE)

A user points `shop.example.com` at your server, and your server serves it over HTTPS with a certificate of its own, which the module orders, renews and serves. Your app says which domains it serves; the module does the rest.

```sh
dotnet add package GrayMint.TlsCertificates
```

## What it does

- **Orders and renews.** A domain with no certificate gets one at the renewal's next run. A certificate is renewed when a third of its lifetime is left, as Let's Encrypt advises: 30 days of its 90, 2 of a 6-day one. One that another directory issued, as a staging one, is ordered again.
- **Checks your user's domain.** When your user gives you a domain, `TlsDomainChecker` tells you whether it points to your server, by its CNAME record, and whether it is theirs, by a TXT record only the domain's owner can publish. The authority can tell you neither: it reads a challenge minutes after an order, and finds a domain valid that reaches your server, whoever claims it.
- **Waits after a failure.** A failed order waits 12 minutes, twice as long after each failure in a row, up to a day; the domain's error says why, as a code your app can word for its users. `RetryNow` shortens the wait to 12 minutes after the last failure, as when your user has fixed the domain, but never takes it away: however often your user saves a domain, it stays within the five failed validations an hour Let's Encrypt allows.
- **Runs once.** The renewal holds a lease in the database: a second host with it, or a second start of the first, waits on the one running.
- **Serves.** Every host that registers the module serves the certificates to Kestrel, by the name each TLS connection asks for, and answers the challenges of the orders under way on HTTP.
- **Keeps the keys secret.** The certificates' keys and the account's go to the database through a protector of yours, as ASP.NET Data Protection.

## Use

Your app gives the module two things: its domains, and how to protect the keys.

```csharp
using GrayMint.TlsCertificates;

// the domains your app serves: the renewal asks for all of them once a run, and each connection asks for its own
public class ShopDomains(ShopDomainCache cache) : ITlsCertificateDomains
{
    public Task<IReadOnlyCollection<string>> GetDomains(CancellationToken cancellationToken) =>
        cache.ReadAll(cancellationToken);

    // asked for every connection to a name not in the certificate cache, whatever name a client sends: from memory
    public ValueTask<bool> IsServed(string domain, CancellationToken cancellationToken) =>
        cache.Contains(domain, cancellationToken);
}

// the keys, as ASP.NET Data Protection protects them, with the same key ring in every host
public class KeyProtector(IDataProtectionProvider provider) : ITlsCertificateProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("tls-certificates");
    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
```

Every host that serves the domains, or shows how they stand, registers the module:

```csharp
builder.Services.AddSingleton<ITlsCertificateDomains, ShopDomains>();
builder.Services.AddScoped<ITlsCertificateProtector, KeyProtector>();
builder.Services.AddGrayMintTlsCertificates(options => options.DefaultDomain = "www.example.com");
builder.Services.AddGrayMintTlsCertificatesDb(options => options.UseNpgsql(connectionString));
```

The tables go in the database the connection names, in a schema of their own (`gmtls`): a database of their own, which the setup makes, or yours, beside your tables. One host makes them before it starts, and every host checks them at its start:

```csharp
var app = builder.Build();
await app.Services.UseGrayMintTlsCertificates(CancellationToken.None);
```

The host that renews them, one of them, adds the renewal:

```csharp
builder.Services.AddGrayMintTlsCertificateRenewal(options => {
    options.DirectoryUri = AcmeDirectories.LetsEncryptStaging; // GrayMint.Common.Acme's; Let's Encrypt itself by default
});
```

The host that serves the domains listens on 80 and 443 itself, with no proxy in front of it that ends TLS:

```csharp
builder.WebHost.ConfigureKestrel(kestrel => {
    kestrel.ListenAnyIP(80);
    kestrel.ListenAnyIP(443, listen => listen.UseTlsCertificates());
});

var app = builder.Build();
app.UseTlsCertificateChallenges(); // first: the authority reads the challenge on port 80
```

A connection that names no domain, or one with no certificate, gets the certificate of `DefaultDomain`; a client that checks the name still refuses it for another one.

## Check your user's domain

Before you serve a domain your user saves, check that it points to your server and that it is theirs. Your server answers the challenges of every user's domain, so the authority's success proves only that a domain reaches it, not whose it is. The checks are an option of their own, which needs no tables:

```csharp
builder.Services.AddGrayMintTlsDomainChecks(options => {
    options.TargetCName = "domains.example.com";
    // yours alone, so a record your user left for another app of the same format proves nothing here
    options.OwnershipFormat = new TlsDomainOwnershipFormat { Purpose = "shop-domain-ownership:" };
});
```

Run them when your user saves the domain, and word their answers:

```csharp
var target = await tlsDomainChecker.CheckTarget(domain, cancellationToken);
if (target.Status != TlsDomainTargetStatus.PointsHere)
    return $"Add a CNAME record from {domain} to domains.example.com."; // target.Target says where it points now, if anywhere

if (!await tlsDomainChecker.HasOwnershipRecord(domain, shop.Id, cancellationToken)) {
    var record = tlsDomainChecker.GetOwnershipRecord(domain, shop.Id); // _domain-verify.<domain>, with a value of the shop's
    return $"Add a TXT record named {record.Name} with the value {record.Value}.";
}
```

The checks ask your server's resolver and Cloudflare's, Google's and OpenDNS's at once, and a domain points here when any of them says so, as one can still cache a record your user has just changed as it was. Only a domain's own records count: one its CNAME leads to is whoever has that name's. A domain takes no port, and a lookup no server gives a usable answer to throws `TlsDomainLookupException`, rather than say the record isn't there. The ownership record's format is yours: keep it once your users have records, as a change asks each of them for a new one. Your tests register their own `ITlsDomainDns`, to answer for domains no DNS has.

## How they stand

`TlsCertificateService` lists every domain the module keeps: its certificate's expiry, the directory that issued it, and how its last order went (`ErrorCode`, `ErrorMessage`, `ErrorCount`). Show it to your users, or watch it: a certificate that keeps failing expires.

```csharp
var certificates = await tlsCertificateService.List(cancellationToken);
await tlsCertificateService.RetryNow("shop.example.com", cancellationToken); // your user fixed the domain: 12 minutes after the last failure
```

A domain your app no longer serves keeps its certificate until it expires, unrenewed, and then goes: served again before then, it has it at once.

## Options

| `AddGrayMintTlsCertificates` | Default | |
| --- | --- | --- |
| `DefaultDomain` | none | The certificate of a connection that names no domain, or one without a certificate. |
| `CertificateCacheTimeout` | 10 minutes | How long Kestrel uses a certificate before it reads it again, which takes a renewed one. |
| `MissCacheTimeout` | 10 seconds | How long a served domain found without a certificate is not read again. |
| `ChallengeTimeout` | 15 minutes | How long a challenge is answered after it was published. |

| `AddGrayMintTlsCertificateRenewal` | Default | |
| --- | --- | --- |
| `DirectoryUri` | Let's Encrypt | The authority's ACME directory. |
| `Interval` | 1 minute | The wait between two runs. |
| `RenewWhenLifetimeLeft` | 1/3 | How much of a certificate's lifetime is left when it is renewed. |
| `RetryInterval`, `MaxRetryInterval` | 12 minutes, 1 day | The wait after a failed order, doubling up to the longest. The first is also the shortest after any failure, `RetryNow`'s included, which keeps a domain within the five failed validations an hour Let's Encrypt allows. |
| `NewDomainDelay` | 10 seconds | How long your `IsServed` may take to know a new domain. |
| `SelfCheckHandlerFactory` | none | A connection for the renewal to read each challenge itself before the authority does, so a domain that doesn't reach your server fails without a failed validation there: to public addresses only, checked as it connects, and following no redirect, as the domain is your user's. Without it, the authority's read is the only one, and the wait after a failure keeps a domain within the few failed validations an hour the authority allows. |

| `AddGrayMintTlsDomainChecks` | Default | |
| --- | --- | --- |
| `TargetCName` | required | The name your users' domains point to by their CNAME records. |
| `OwnershipFormat` | required | The ownership TXT record: its label (`_domain-verify`), the prefix of its value (`domain-verify=`), and the `Purpose` of your own that is hashed before the owner's key. |
| `UseSystemDnsServers` | true | Whether your server's resolver is asked too. |
| `DnsServers` | Cloudflare, Google, OpenDNS | The DNS servers asked at once with it. |
| `LookupTimeout` | 5 seconds | How long one DNS server may take to answer. |

A wrong option stops the registration, not the first order.

## Development

The tests run on a real PostgreSQL and a real SQL Server, against Pebble, Let's Encrypt's own test authority, and against CoreDNS with a test zone of their own, all of which `compose.yaml` starts:

```sh
docker compose up -d
dotnet test
```

See [docs/design.md](docs/design.md) for what the code holds, what the database holds, and why.

## License

[MIT](LICENSE)
