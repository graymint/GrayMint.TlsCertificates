# Design

What the code holds, what the database holds, and why each is where it is.

## The parts

- **The protocol is GrayMint.Common.Acme's**: the ACME account and order over Certes, the issuer that publishes a challenge, reads it itself and waits for the authority, the challenge's middleware, and Kestrel's certificate cache (`TlsCertificateSelector`). It keeps nothing.
- **This module keeps and decides**: which certificate a domain has, when it is due, how its last order went, and the one renewal that runs. The host says only which domains it serves (`ITlsCertificateDomains`) and how keys are protected (`ITlsCertificateProtector`): who may have a domain, and what a user is told, are the host's.

A host runs it in two roles, often in two processes:

- **Every host** that registers the module reads: `TlsCertificateStore`, which gives Kestrel a domain's certificate and the challenge's middleware its answer, and `TlsCertificateService`, which lists the certificates and lifts a wait.
- **One host** adds the renewal: `TlsCertificateRenewer`, which a job runs every interval.

## What the database holds

Three tables in the schema `gmtls`, beside whatever else the database holds:

- `Certificates`: one row a domain, by its one form (`TlsDomain.Normalize`): the certificate as protected PFX (its key, itself and its intermediates), its thumbprint and dates, the directory that issued it, the challenge of the order under way, and the last failure with the count of failures in a row.
- `Accounts`: one account an authority, by its directory, with its key protected. A unique index keeps it one.
- `Metadata`: one row: the version of the tables' shape, and the renewal's lease.

The setup makes the tables from the model when there are none, and refuses a database with some of them only. Every host checks them at its start, and refuses tables of another version: a build on tables of another shape would read them wrong. A change to the tables raises `TlsCertificatesSchema.Version` with a step of the setup that takes tables of the version before to it.

## A run of the renewal

1. **The lease.** The run takes the metadata's row by a write that holds only while it is free, expired or its own, so of two runs that meet one alone gets it; it renews it before each order and gives it back at its end. A host that stopped in the middle of a run holds it until it expires (`LeaseTime`).
2. **The domains.** The host's, in their one form. A row the host no longer serves goes once its certificate has expired: served again before then, it has it at once, and orders none the authority has just issued. A new domain gets its row, and the run waits `NewDomainDelay`, so the hosts' `IsServed` knows it when the authority reads its challenge.
3. **What is due.** A domain with no certificate, one from another directory, or one with a third of its lifetime left; not one whose last failure is more recent than its wait. The new first, then the one that expires soonest, one at a time: the authority allows few orders at once, and the domains wait no more than a run for each other.
4. **The order.** The challenge goes to the domain's row, the renewal reads it through the host's connection as the authority will, and the authority checks it. Taking it away matches its token, so one taken away late never takes a later order's.
5. **The certificate**, once signed, goes to the row, protected; its error is cleared. What fails after the authority has signed is the host's, not the order's: it is logged, not kept as the domain's error, and the next run orders again.

Every write of a row is one statement (`ExecuteUpdate`), so a failure kept never carries a certificate with it, nor a certificate a failure.

## The failures

A failed order keeps a code a host can word for its users (`TlsCertificateErrorCode`), and a message: the authority's or the issuer's words, which say what to fix. A failure of the host's own, a database's or a protector's, keeps a message of the module's: its words may hold a secret, and go to the host's log alone.

The wait doubles with each failure in a row, from `RetryInterval` to `MaxRetryInterval`: Let's Encrypt allows an account five failed validations an hour for a domain. `RetryNow` lifts it.

An order cancelled or timed out in the middle may still end at the authority: Certes' calls take no cancellation token, and a certificate the authority signs after the run let go of it is lost. The next run orders again; Let's Encrypt allows five certificates a week for a domain, which such a loss doesn't reach.

## Serving

`TlsCertificateStore` asks the host's `IsServed` first, from memory, so a name a client makes up costs no read of the database. A served domain with no certificate yet is not read again for `MissCacheTimeout`, so connections to it don't each read the database; only a served domain is kept there, so the names a client makes up never fill it. Kestrel's cache keeps a certificate for `CertificateCacheTimeout`, which takes a renewed one, and keeps the one it had when a read fails or what it read won't load.

A challenge is answered for its own domain and token, for `ChallengeTimeout` after it was published: one a renewal left behind, when its host stopped in the middle of an order, is answered no more.

## Choices

- **HTTP-01 only.** It needs nothing of the user but a DNS record to the host, and nothing of the host but port 80. A wildcard certificate needs DNS-01, and the user's DNS provider.
- **One renewal.** A single writer keeps the account, the orders and the challenges simple; the lease keeps a second one out, and its rows are written by statements that hold whatever the order of two writers.
- **The host's domains, asked each time.** The module keeps no list of its own to go stale: the host's data says which domains are served, and the module follows.
- **No renewal information (ARI) yet.** Let's Encrypt's renewal window is advice; a third of the lifetime left keeps to it for the lifetimes Let's Encrypt issues.
