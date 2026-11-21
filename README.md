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
- **Checks before it asks.** Before the authority reads a domain's challenge, the renewal reads it itself, through a connection you give it, so a domain that doesn't reach your server yet fails without a failed validation at the authority, which allows few.
- **Waits after a failure.** A failed order waits 5 minutes, twice as long after each failure in a row, up to a day; the domain's error says why, as a code your app can word for its users. `RetryNow` lifts the wait, as when your user has fixed the domain.
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
    // the domain is your user's: connect to public addresses only, checked as you connect, and follow no redirect
    options.SelfCheckHandlerFactory = serviceProvider => CreatePublicOnlyHandler();
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

## How they stand

`TlsCertificateService` lists every domain the module keeps: its certificate's expiry, the directory that issued it, and how its last order went (`ErrorCode`, `ErrorMessage`, `ErrorCount`). Show it to your users, or watch it: a certificate that keeps failing expires.

```csharp
var certificates = await tlsCertificateService.List(cancellationToken);
await tlsCertificateService.RetryNow("shop.example.com", cancellationToken); // your user fixed the domain
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
| `RetryInterval`, `MaxRetryInterval` | 5 minutes, 1 day | The wait after a failed order, doubling up to the longest. |
| `NewDomainDelay` | 10 seconds | How long your `IsServed` may take to know a new domain. |
| `SelfCheckHandlerFactory` | required | The connection of the renewal's own read of a challenge; `SkipSelfCheck` orders without it. |

A wrong option stops the registration, not the first order.

## Development

The tests run on a real PostgreSQL and a real SQL Server, and against Pebble, Let's Encrypt's own test authority, all of which `compose.yaml` starts:

```sh
docker compose up -d
dotnet test
```

See [docs/design.md](docs/design.md) for what the code holds, what the database holds, and why.

## License

[MIT](LICENSE)
